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
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Pets.Snapshots;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Inventory.Pets;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Room.Pets;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.Rooms.Grains.Systems;

public sealed partial class RoomPetSystem
{
    public async Task<PetSnapshot?> PlacePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    )
    {
        if (
            !_roomGrain._state.RoomSnapshot.AllowPets
            && _roomGrain._state.RoomSnapshot.OwnerId != ctx.PlayerId
        )
        {
            await SendPetPlacingErrorAsync(ctx, PetPlacementForbiddenInFlatError);
            return null;
        }

        await EnsureRoomReadyForPetPlacementAsync(ct);

        // (0, 0) from the client means "I dropped this from the inventory, put it somewhere",
        // not the corner tile.
        if (!TryResolvePetPlacementTile(x, y, out x, out y))
        {
            await SendPetPlacingErrorAsync(ctx, PetPlacementSelectedTileNotFreeError);

            return null;
        }

        double z = GetTileHeightForPet(x, y);

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity? pet = await dbCtx.Pets.SingleOrDefaultAsync(
            p =>
                p.Id == petId && p.OwnerPlayerEntityId == ctx.PlayerId.Value && p.DeletedAt == null,
            ct
        );

        if (pet is null)
        {
            _roomGrain._logger.LogDebug(
                "Pet placement ignored because pet {PetId} was not found for player {PlayerId}",
                petId,
                ctx.PlayerId
            );
            return null;
        }

        if (pet.RoomEntityId is not null && pet.RoomEntityId != _roomGrain.RoomId.Value)
        {
            _roomGrain._logger.LogDebug(
                "Pet placement ignored because pet {PetId} is already in room {ExistingRoomId}",
                petId,
                pet.RoomEntityId
            );
            return null;
        }

        bool wasInInventory = pet.RoomEntityId is null;

        if (wasInInventory)
        {
            ApplyOfflineDecay(pet, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        pet.RoomEntityId = _roomGrain.RoomId.Value;
        pet.X = x;
        pet.Y = y;
        pet.Z = z;
        pet.Direction = (int)direction;

        await dbCtx.SaveChangesAsync(ct);

        PetSnapshot snapshot = RoomPetRuntime.ToSnapshot(pet);
        _roomGrain._state.PetsById[pet.Id] = snapshot;

        if (wasInInventory)
        {
            await SendPetRemovedFromInventoryAsync(snapshot);
        }

        await SendPetAddedAsync(snapshot, ct);

        await _roomGrain._events.PublishAsync(
            new PetPlacedEvent(ctx.PlayerId, pet.Id, pet.Type, _roomGrain.RoomId.Value),
            ct
        );

        return snapshot;
    }

    public async Task<PetSnapshot?> MovePetAsync(
        ActionContext ctx,
        int petId,
        int x,
        int y,
        Rotation direction,
        CancellationToken ct
    )
    {
        await EnsureRoomReadyForPetPlacementAsync(ct);

        double z = GetTileHeightForPet(x, y);

        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity? pet = await LoadPlacedPetAsync(dbCtx, petId, ct);

        if (pet is null)
        {
            return null;
        }

        EnsurePetOwner(ctx, pet);

        // Position is applied to the in-memory entity but not saved here — it rides the
        // periodic dirty-pet flush (FlushDirtyPetsAsync) alongside stats, same as furniture
        // position uses the timer-flush pattern. Nothing reads the DB row before that flush.
        pet.X = x;
        pet.Y = y;
        pet.Z = z;
        pet.Direction = (int)direction;

        PetSnapshot snapshot = RoomPetRuntime.ToSnapshot(pet);

        // Preserve live in-memory stats (may not have been flushed to DB yet)
        if (_roomGrain._state.PetsById.TryGetValue(pet.Id, out PetSnapshot? prev))
        {
            snapshot = snapshot with
            {
                Nutrition = prev.Nutrition,
                Energy = prev.Energy,
                Experience = prev.Experience,
                Level = prev.Level,
                Respect = prev.Respect,
            };
        }

        _roomGrain._state.PetsById[pet.Id] = snapshot;
        GetMotionState(snapshot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).IsStatsDirty =
            true;

        await SendPetUpdatedAsync(snapshot, ct);

        return snapshot;
    }

    public async Task<PetSnapshot?> PickUpPetAsync(
        ActionContext ctx,
        int petId,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);

        PetEntity? pet = await LoadPlacedPetAsync(dbCtx, petId, ct);

        if (pet is null)
        {
            return null;
        }

        EnsurePetOwner(ctx, pet);

        // Sync live in-memory stats so pickup persists the latest values
        if (_roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? liveSnapshot))
        {
            pet.Nutrition = liveSnapshot.Nutrition;
            pet.Energy = liveSnapshot.Energy;
            pet.Experience = liveSnapshot.Experience;
            pet.Level = liveSnapshot.Level;
            pet.Respect = liveSnapshot.Respect;
        }

        pet.RoomEntityId = null;

        await dbCtx.SaveChangesAsync(ct);

        _roomGrain._state.PetsById.Remove(pet.Id);
        _motionByPetId.Remove(petId);

        PetSnapshot snapshot = RoomPetRuntime.ToSnapshot(pet);

        await _roomGrain.SendComposerToRoomAsync(
            new UserRemoveMessageComposer { ObjectId = RoomPetRuntime.ToRoomObjectId(pet.Id) }
        );

        await SendPetAddedToInventoryAsync(snapshot, ct);

        await _roomGrain._events.PublishAsync(
            new PetPickedUpEvent(ctx.PlayerId, pet.Id, pet.Type, _roomGrain.RoomId.Value),
            ct
        );

        return snapshot;
    }

    public async Task<PetSnapshot?> GetPlacedPetSnapshotAsync(int petId, CancellationToken ct)
    {
        await EnsurePetsLoadedAsync(ct);

        return _roomGrain._state.PetsById.TryGetValue(petId, out PetSnapshot? snapshot)
            ? snapshot
            : null;
    }

    public async Task<ImmutableArray<PetSnapshot>> GetPlacedPetSnapshotsAsync(CancellationToken ct)
    {
        await EnsurePetsLoadedAsync(ct);

        return _roomGrain._state.PetsById.Values.OrderBy(p => p.PetId).ToImmutableArray();
    }

    public async Task<ImmutableArray<RoomAvatarSnapshot>> GetPlacedPetAvatarSnapshotsAsync(
        CancellationToken ct
    )
    {
        await EnsurePetsLoadedAsync(ct);

        List<RoomAvatarSnapshot> snapshots = new(_roomGrain._state.PetsById.Count);

        foreach (PetSnapshot pet in _roomGrain._state.PetsById.Values.OrderBy(p => p.PetId))
        {
            snapshots.Add(await ToAvatarSnapshotAsync(pet, ct));
        }

        return snapshots.ToImmutableArray();
    }
}
