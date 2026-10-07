using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Pets;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;
using Vortex.Rooms.Configuration;

namespace Vortex.Rooms.Grains.Systems;

/// <summary>
///     Loading, the per-tick loop, dirty-stat flush, and shared helpers (avatar-snapshot
///     building, composer fan-out, motion-state bookkeeping). Placement/movement lives in
///     <c>RoomPetSystem.Placement.cs</c>, following the owner and wandering in
///     <c>RoomPetSystem.Motion.cs</c>, saddles and riding in <c>RoomPetSystem.Riding.cs</c>.
/// </summary>
public sealed partial class RoomPetSystem(RoomGrain roomGrain)
{
    private const int PetPlacementForbiddenInFlatError = 1;
    private const int PetPlacementSelectedTileNotFreeError = 4;
    private readonly Dictionary<int, PetMotionState> _motionByPetId = [];

    private readonly RoomGrain _roomGrain = roomGrain;
    private long _nextPetFlushAtMs = -1;

    private PetTuning? _tuning;
    private long _nextTuningRefreshAtMs = -1;

    /// <summary>
    /// The live tunables. Read once per flush interval rather than per tick -- the tick runs twice a
    /// second per room and cannot afford a grain call -- and never null once the tick has
    /// started, because <see cref="RefreshTuningAsync" /> runs before the loop.
    /// </summary>
    private PetTuning Tuning => _tuning ??= PetTuning.FromDefaults(_roomGrain._roomConfig.Pet);

    private async Task RefreshTuningAsync(long now)
    {
        if (_tuning is not null && now < _nextTuningRefreshAtMs)
        {
            return;
        }

        _nextTuningRefreshAtMs = now + _roomGrain._roomConfig.Pet.StatFlushIntervalMs;

        try
        {
            _tuning = await PetTuning.LoadAsync(
                _roomGrain._grainFactory.GetServerConfigGrain(),
                _roomGrain._roomConfig.Pet
            );
        }
        catch (Exception ex)
        {
            // A config read that fails must not stop pets moving; the compiled defaults stand in.
            _tuning ??= PetTuning.FromDefaults(_roomGrain._roomConfig.Pet);
            _roomGrain._logger.LogError(
                ex,
                "Failed to refresh pet tuning in room {RoomId}",
                _roomGrain.RoomId
            );
        }
    }

    public async Task EnsurePetsLoadedAsync(CancellationToken ct)
    {
        if (_roomGrain._state.IsPetsLoaded)
        {
            return;
        }

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity[] pets = await dbCtx
            .Pets.AsNoTracking()
            .Where(p => p.RoomEntityId == _roomGrain.RoomId.Value && p.DeletedAt == null)
            .ToArrayAsync(ct);

        _roomGrain._state.PetsById.Clear();

        foreach (PetEntity pet in pets)
        {
            _roomGrain._state.PetsById[pet.Id] = RoomPetRuntime.ToSnapshot(pet);
        }

        _roomGrain._state.IsPetsLoaded = true;
    }

    public async Task ProcessPetsAsync(long now, CancellationToken ct)
    {
        if (now < _roomGrain._state.NextPetBoundaryMs)
        {
            return;
        }

        _roomGrain._state.NextPetBoundaryMs = _roomGrain.AdvanceBoundaryPast(
            now,
            _roomGrain._roomConfig.Pet.TickMs
        );

        await EnsurePetsLoadedAsync(ct);

        if (_roomGrain._state.PetsById.Count == 0)
        {
            return;
        }

        await EnsureRoomReadyForPetPlacementAsync(ct);
        await RefreshTuningAsync(now);

        List<RoomAvatarSnapshot> dirtySnapshots = [];

        foreach (
            PetSnapshot pet in _roomGrain._state.PetsById.Values.OrderBy(p => p.PetId).ToArray()
        )
        {
            try
            {
                PetMotionState motion = GetMotionState(pet, now);
                PetSnapshot current = ApplyPendingPetStep(pet, motion);
                RoomPetAvatarSnapshot? update = await ProcessPetMotionAsync(
                    current,
                    motion,
                    now,
                    ct
                );

                if (update is not null)
                {
                    dirtySnapshots.Add(update);
                }
            }
            catch (Exception ex)
            {
                _roomGrain._logger.LogError(
                    ex,
                    "Failed to process pet movement tick for pet {PetId} in room {RoomId}",
                    pet.PetId,
                    _roomGrain.RoomId
                );
            }
        }

        if (dirtySnapshots.Count > 0)
        {
            await _roomGrain.SendComposerToRoomAsync(
                new UserUpdateMessageComposer { Avatars = dirtySnapshots.ToImmutableArray() }
            );
        }

        if (_nextPetFlushAtMs < 0)
        {
            _nextPetFlushAtMs = now + _roomGrain._roomConfig.Pet.StatFlushIntervalMs;
        }
        else if (now >= _nextPetFlushAtMs)
        {
            _nextPetFlushAtMs = now + _roomGrain._roomConfig.Pet.StatFlushIntervalMs;

            try
            {
                await FlushDirtyPetsAsync(ct);
            }
            catch (Exception ex)
            {
                _roomGrain._logger.LogError(
                    ex,
                    "Failed to flush dirty pet stats in room {RoomId}",
                    _roomGrain.RoomId
                );
            }
        }
    }

    /// <summary>
    /// Hands the pets whose stats have moved to the room's persistence grain.
    /// </summary>
    /// <remarks>
    /// This used to open a <c>DbContext</c> and save, inside the room's own turn, every sixty
    /// seconds — a periodic spike on the clock that moves avatars, in a room that may hold a dozen
    /// pets (PET-TICK-044). The furniture has been written from <c>RoomPersistenceGrain</c> all
    /// along, and a comment two files over claimed the pets did too; they did not, and believing it
    /// is what kept this here.
    /// <para>
    /// The dirty flags are cleared on handover rather than after a save, because the save is no
    /// longer this grain's to watch. Nothing is lost by it: the persistence grain keeps its queue
    /// until a write succeeds, which is the same guarantee the old in-line clear was reaching for.
    /// </para>
    /// </remarks>
    public async Task FlushDirtyPetsAsync(CancellationToken ct)
    {
        List<PetSnapshot> dirty = [];

        foreach (KeyValuePair<int, PetMotionState> kvp in _motionByPetId)
        {
            if (
                kvp.Value.IsStatsDirty
                && _roomGrain._state.PetsById.TryGetValue(kvp.Key, out PetSnapshot? snapshot)
            )
            {
                dirty.Add(snapshot);
            }
        }

        if (dirty.Count == 0)
        {
            return;
        }

        await _roomGrain
            ._grainFactory.GetRoomPersistenceGrain(_roomGrain.RoomId)
            .EnqueueDirtyPetsAsync(_roomGrain.RoomId, dirty, ct)
            .ConfigureAwait(true);

        foreach (PetSnapshot pet in dirty)
        {
            if (_motionByPetId.TryGetValue(pet.PetId, out PetMotionState? motion))
            {
                motion.IsStatsDirty = false;
            }
        }
    }

    private PetMotionState GetMotionState(PetSnapshot pet, long now)
    {
        if (_motionByPetId.TryGetValue(pet.PetId, out PetMotionState? motion))
        {
            return motion;
        }

        motion = new PetMotionState { NextWanderAtMs = ScheduleNextWanderAt(now) };
        _motionByPetId[pet.PetId] = motion;

        return motion;
    }

    private long ScheduleNextWanderAt(long now)
    {
        int minMs = Math.Max(_roomGrain._roomConfig.Pet.TickMs, Tuning.WanderIdleMinMs);
        int maxMs = Math.Max(minMs, Tuning.WanderIdleMaxMs);

        return now + Random.Shared.Next(minMs, maxMs + 1);
    }

    private bool TryResolvePetPlacementTile(int x, int y, out int resolvedX, out int resolvedY) =>
        RoomPetRuntime.TryResolveDropTile(
            x,
            y,
            _roomGrain._state.Model?.DoorX ?? 0,
            _roomGrain._state.Model?.DoorY ?? 0,
            _roomGrain.MapModule.Width,
            _roomGrain.MapModule.Height,
            IsTileFreeForPet,
            out resolvedX,
            out resolvedY
        );

    /// <summary>A tile a pet may stand on: in bounds, not disabled, closed or already occupied.</summary>
    private bool IsTileFreeForPet(int x, int y)
    {
        if (!_roomGrain.MapModule.InBounds(x, y))
        {
            return false;
        }

        RoomTileFlags flags = _roomGrain._state.TileFlags[_roomGrain.MapModule.ToIdx(x, y)];

        return !flags.Has(RoomTileFlags.Disabled)
            && !flags.Has(RoomTileFlags.Closed)
            && !flags.Has(RoomTileFlags.AvatarOccupied);
    }

    private double GetTileHeightForPet(int x, int y)
    {
        if (!_roomGrain.MapModule.InBounds(x, y))
        {
            throw new VortexException(VortexErrorCodeEnum.TileOutOfBounds);
        }

        int tileIdx = _roomGrain.MapModule.ToIdx(x, y);
        RoomTileFlags flags = _roomGrain._state.TileFlags[tileIdx];

        if (
            flags.Has(RoomTileFlags.Disabled)
            || flags.Has(RoomTileFlags.Closed)
            || flags.Has(RoomTileFlags.AvatarOccupied)
        )
        {
            throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
        }

        return _roomGrain._state.TileHeights[tileIdx].Value;
    }

    private async Task EnsureRoomReadyForPetPlacementAsync(CancellationToken ct)
    {
        await _roomGrain.MapModule.EnsureMapBuiltAsync(ct);
        await _roomGrain.FurniModule.EnsureFurniLoadedAsync(ct);
    }

    private async Task SendPetAddedAsync(PetSnapshot pet, CancellationToken ct)
    {
        RoomPetAvatarSnapshot snapshot = await ToAvatarSnapshotAsync(pet, ct);

        await _roomGrain.SendComposerToRoomAsync(new UsersMessageComposer { Avatars = [snapshot] });
    }

    private async Task SendPetUpdatedAsync(
        PetSnapshot pet,
        CancellationToken ct,
        string status = ""
    )
    {
        RoomPetAvatarSnapshot snapshot = await ToAvatarSnapshotAsync(pet, status, ct);

        await _roomGrain.SendComposerToRoomAsync(
            new UserUpdateMessageComposer { Avatars = [snapshot] }
        );

        // The avatar update redraws the pet but says nothing about what can be done with it. Every
        // caller here has just changed a stat or a permission, so the status goes out alongside.
        await SendPetStatusAsync(pet);
    }

    private async Task SendPetRemovedFromInventoryAsync(PetSnapshot pet)
    {
        try
        {
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(pet.OwnerId)
                .SendComposerAsync(
                    new PetRemovedFromInventoryEventMessageComposer { PetId = pet.PetId }
                );
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to send pet {PetId} inventory removal for player {PlayerId}",
                pet.PetId,
                pet.OwnerId
            );
        }
    }

    private async Task SendPetPlacingErrorAsync(ActionContext ctx, int errorCode)
    {
        try
        {
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(ctx.PlayerId)
                .SendComposerAsync(new PetPlacingErrorMessageComposer { ErrorCode = errorCode });
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to send pet placing error {ErrorCode} to player {PlayerId}",
                errorCode,
                ctx.PlayerId
            );
        }
    }

    private async Task SendPetAddedToInventoryAsync(PetSnapshot pet, CancellationToken ct)
    {
        try
        {
            // Pets leave in T6; the inventory notice left with the inventory.
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogError(
                ex,
                "Failed to send pet {PetId} inventory add for player {PlayerId}",
                pet.PetId,
                pet.OwnerId
            );
        }
    }

    private async Task<RoomPetAvatarSnapshot> ToAvatarSnapshotAsync(
        PetSnapshot pet,
        CancellationToken ct
    )
    {
        return await ToAvatarSnapshotAsync(pet, string.Empty, PetAnimation.StandPosture, ct);
    }

    private async Task<RoomPetAvatarSnapshot> ToAvatarSnapshotAsync(
        PetSnapshot pet,
        string status,
        CancellationToken ct
    )
    {
        return await ToAvatarSnapshotAsync(pet, status, PetAnimation.StandPosture, ct);
    }

    private async Task<RoomPetAvatarSnapshot> ToAvatarSnapshotAsync(
        PetSnapshot pet,
        string status,
        string posture,
        CancellationToken ct
    )
    {
        string ownerName = await GetOwnerNameAsync(pet.OwnerId, ct);

        return RoomPetRuntime.ToAvatarSnapshot(pet, ownerName, status, posture);
    }

    private async Task<string> GetOwnerNameAsync(PlayerId ownerId, CancellationToken ct)
    {
        if (_roomGrain._state.OwnerNamesById.TryGetValue(ownerId, out string? ownerName))
        {
            return ownerName;
        }

        ownerName = await _roomGrain
            ._grainFactory.GetPlayerDirectoryGrain()
            .GetPlayerNameAsync(ownerId, ct);

        _roomGrain._state.OwnerNamesById[ownerId] = ownerName;

        return ownerName;
    }

    private async Task<PetEntity?> LoadPlacedPetAsync(
        VortexDbContext dbCtx,
        int petId,
        CancellationToken ct
    )
    {
        return await dbCtx.Pets.SingleOrDefaultAsync(
            p => p.Id == petId && p.RoomEntityId == _roomGrain.RoomId.Value && p.DeletedAt == null,
            ct
        );
    }

    private static void EnsurePetOwner(ActionContext ctx, PetEntity pet)
    {
        if (pet.OwnerPlayerEntityId != ctx.PlayerId)
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToManipulatePet);
        }
    }

    private sealed class PetMotionState
    {
        public List<int> TilePath { get; } = [];
        public int NextTileId { get; set; } = -1;
        public long PendingStopAtMs { get; set; }
        public long NextWanderAtMs { get; set; }
        public bool IsStatsDirty { get; set; }

        public void ClearMovement()
        {
            TilePath.Clear();
            NextTileId = -1;
            PendingStopAtMs = 0;
        }
    }
}
