using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Protocol.Messages.Outgoing.Navigator;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    /// <summary>
    /// The largest plan a save will accept. The client's editor caps its own canvas at 4095px and
    /// the tile grid it draws is far smaller than that, so this is a guard against a hand-made
    /// packet rather than against the editor: every per-tile array below is allocated at this size.
    /// </summary>
    private const int MaxFloorPlanTiles = 64 * 64;

    /// <inheritdoc />
    public async Task<bool> UpdateFloorPlanAsync(
        PlayerId actor,
        FloorPlanUpdate update,
        CancellationToken ct
    )
    {
        if (!await IsRoomOwnerAsync(actor).ConfigureAwait(true))
        {
            return false;
        }

        RoomModelSnapshot? previous = _state.Model;

        if (previous is null || string.IsNullOrWhiteSpace(update.Model))
        {
            return false;
        }

        // -1 is "unset" on every field but the plan: the composer sends one, six or seven fields,
        // so a short save keeps the door the room already had rather than moving it to (-1, -1).
        int doorX = update.DoorX >= 0 ? update.DoorX : previous.DoorX;
        int doorY = update.DoorY >= 0 ? update.DoorY : previous.DoorY;
        Rotation doorRotation =
            update.DoorRotation >= 0 ? (Rotation)update.DoorRotation : previous.DoorRotation;

        RoomModelSnapshot compiled;

        try
        {
            compiled = _roomModelProvider.CompileCustomModel(
                previous.Id,
                previous.Name,
                update.Model,
                doorX,
                doorY,
                doorRotation
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Rejected an unparseable floor plan for room {RoomId}.",
                _state.RoomId
            );

            return false;
        }

        if (!IsUsableFloorPlan(compiled))
        {
            return false;
        }

        int modelId = await PersistFloorPlanAsync(compiled, update, ct).ConfigureAwait(true);

        if (modelId <= 0)
        {
            return false;
        }

        // The room now points at its own custom model row, which the provider's reference table
        // does not hold — reload it, or the next time this room is activated `GetModelById()`
        // throws and the room will not open at all.
        //
        // Reloading every model to learn one is coarse, and deliberately so: a floor-plan save is a
        // rare, explicit action, and a targeted insert would put a second write path into a cache
        // that is otherwise only ever built one way.
        await _roomModelProvider.ReloadAsync(ct).ConfigureAwait(true);

        _state.Model = _roomModelProvider.GetModelById(modelId);

        await RebuildMapForNewModelAsync(ct).ConfigureAwait(true);

        ReseatAvatarsInsidePlan();

        await BroadcastFloorPlanAsync().ConfigureAwait(true);

        return true;
    }

    /// <summary>
    /// The two things a plan has to be beyond parseable: small enough to allocate, and standing
    /// under its own door.
    ///
    /// A door on a hole is what makes a room unenterable — every arrival is placed on the door
    /// tile — so it is refused here rather than saved and discovered later.
    /// </summary>
    private bool IsUsableFloorPlan(RoomModelSnapshot compiled)
    {
        if (compiled.Size <= 0 || compiled.Size > MaxFloorPlanTiles)
        {
            _logger.LogWarning(
                "Rejected a floor plan of {Size} tiles for room {RoomId}.",
                compiled.Size,
                _state.RoomId
            );

            return false;
        }

        if (
            compiled.DoorX < 0
            || compiled.DoorY < 0
            || compiled.DoorX >= compiled.Width
            || compiled.DoorY >= compiled.Height
        )
        {
            _logger.LogWarning(
                "Rejected a floor plan whose door ({DoorX}, {DoorY}) is off the plan for room {RoomId}.",
                compiled.DoorX,
                compiled.DoorY,
                _state.RoomId
            );

            return false;
        }

        int doorIdx = (compiled.DoorY * compiled.Width) + compiled.DoorX;

        if (compiled.BaseFlags[doorIdx].Has(RoomTileFlags.Disabled))
        {
            _logger.LogWarning(
                "Rejected a floor plan whose door stands on a hole for room {RoomId}.",
                _state.RoomId
            );

            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes the plan to the room's own <c>room_models</c> row, creating it the first time.
    ///
    /// Habbo gives an edited room a private model rather than editing a shared one: every room
    /// starts on a stock model that other rooms are also using, so saving in place would redraw
    /// the floor of every room built on the same template.
    /// </summary>
    private async Task<int> PersistFloorPlanAsync(
        RoomModelSnapshot compiled,
        FloorPlanUpdate update,
        CancellationToken ct
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(true);

            RoomEntity? room = await dbCtx
                .Rooms.FirstOrDefaultAsync(r => r.Id == _state.RoomId.Value, ct)
                .ConfigureAwait(true);

            if (room is null)
            {
                return 0;
            }

            string customName = $"custom_{_state.RoomId.Value}";

            RoomModelEntity? model = await dbCtx
                .RoomModels.FirstOrDefaultAsync(m => m.Name == customName, ct)
                .ConfigureAwait(true);

            if (model is null)
            {
                model = new RoomModelEntity
                {
                    Name = customName,
                    Model = compiled.Model,
                    DoorX = compiled.DoorX,
                    DoorY = compiled.DoorY,
                    DoorRotation = compiled.DoorRotation,
                    Enabled = true,
                    Custom = true,
                };

                dbCtx.RoomModels.Add(model);
            }
            else
            {
                model.Model = compiled.Model;
                model.DoorX = compiled.DoorX;
                model.DoorY = compiled.DoorY;
                model.DoorRotation = compiled.DoorRotation;
                model.Enabled = true;
                model.Custom = true;
            }

            // The wall and floor thickness and the wall height ride along with the plan, and are
            // room columns rather than model ones: two rooms on the same plan can be dressed
            // differently. Each keeps its current value when the short form left it at -1.
            if (update.WallThickness is >= -2 and <= 1)
            {
                room.ThicknessWall = (RoomThicknessType)update.WallThickness;
            }

            if (update.FloorThickness is >= -2 and <= 1)
            {
                room.ThicknessFloor = (RoomThicknessType)update.FloorThickness;
            }

            room.WallHeight = update.WallHeight;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            room.RoomModelEntityId = model.Id;

            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            _state.RoomSnapshot = _state.RoomSnapshot with
            {
                WallThickness = room.ThicknessWall,
                FloorThickness = room.ThicknessFloor,
                WallHeight = room.WallHeight,
                LastUpdatedUtc = DateTime.UtcNow,
            };

            return model.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the floor plan for room {RoomId}.", _state.RoomId);

            return 0;
        }
    }

    /// <summary>
    /// Throws the tile map away and builds it again for the new model, then puts the furniture back
    /// on it.
    ///
    /// The rebuild is not optional and not incremental: a plan can change the room's *dimensions*,
    /// and all six per-tile arrays are sized from `Model.Size`. `EnsureMapBuiltAsync()` is the one
    /// place that allocates them, so the flag it guards is cleared rather than the allocation being
    /// written a second time here.
    ///
    /// Re-adding the items is what makes the new map agree with the room again — the fresh arrays
    /// have empty stacks, so without this every tile would read as free and the next placement
    /// would stack furniture into furniture. An item now standing off the plan is dropped from the
    /// map rather than added out of bounds; it keeps its stored position, so shrinking a room and
    /// growing it back brings the furniture back with it.
    /// </summary>
    private async Task RebuildMapForNewModelAsync(CancellationToken ct)
    {
        _state.IsMapReady = false;

        await MapModule.EnsureMapBuiltAsync(ct).ConfigureAwait(true);

        _state.IsTileComputationPaused = true;

        foreach (IRoomItem item in _state.ItemsById.Values.ToList())
        {
            if (item is IRoomFloorItem floor && !MapModule.InBounds(floor.X, floor.Y))
            {
                continue;
            }

            MapModule.AddItem(item);
        }

        _state.IsTileComputationPaused = false;

        MapModule.ComputeAllTiles();
        _state.DirtyHeightTileIds.Clear();
    }

    /// <summary>
    /// Moves anyone left standing where the floor no longer is onto the door tile.
    ///
    /// Without it an avatar keeps coordinates outside the new arrays, and the next thing that reads
    /// a tile under them — a walk, a chat range check, a roller — indexes out of bounds.
    /// </summary>
    private void ReseatAvatarsInsidePlan()
    {
        RoomModelSnapshot model = _state.Model!;
        int doorIdx = (model.DoorY * model.Width) + model.DoorX;

        foreach (IRoomAvatar avatar in _state.AvatarsByObjectId.Values.ToList())
        {
            bool stillStanding =
                MapModule.InBounds(avatar.X, avatar.Y)
                && !_state
                    .TileFlags[MapModule.ToIdx(avatar.X, avatar.Y)]
                    .Has(RoomTileFlags.Disabled);

            if (stillStanding)
            {
                // Not a no-op, and this is the half that is easy to miss: `TileAvatarStacks` was
                // reallocated empty a moment ago, so an avatar that did not have to move is on a
                // tile that no longer knows it is there. Leave it and nothing blocks — two people
                // share a tile, and the pathfinder walks through them.
                MapModule.AddAvatar(avatar, false);

                continue;
            }

            avatar.SetPosition(model.DoorX, model.DoorY);

            MapModule.AddAvatarAtIdx(avatar, doorIdx, false);

            avatar.SetHeight(_state.TileHeights[doorIdx]);
        }
    }

    /// <summary>
    /// Sends everyone standing in the room back into it, so they pick the new plan up.
    ///
    /// Pushing a fresh <c>FloorHeightMap</c> and <c>HeightMap</c> at them is not enough and was
    /// tried first: the client builds its room model once, on entry, and a mid-session height map
    /// updates nothing you can see — the floor stays as it was drawn. A guest-room card with
    /// <c>EnterRoom</c> set is what the client treats as "go into this room", exactly as clicking a
    /// search result does (<c>NavigatorIncomingMessages.onGetGuestRoomResult()</c> →
    /// <c>goToRoom()</c>), and the entry it triggers re-sends the whole room including the plan.
    ///
    /// Everyone, not just whoever saved: the tiles moved under all of them, and one of them may be
    /// standing where the floor no longer is.
    ///
    /// The per-player fields on this card are left at their neutral values because the card is only
    /// a trigger — the re-entry it causes fetches its own, with that player's group membership and
    /// mute state on it.
    /// </summary>
    private Task BroadcastFloorPlanAsync()
    {
        return SendComposerToRoomAsync(
            new GetGuestRoomResultMessageComposer
            {
                EnterRoom = true,
                RoomInfo = _state.RoomSnapshot,
                RoomForward = false,
                StaffPick = _state.RoomSnapshot.StaffPick,
                IsGroupMember = false,
                // Not a per-player field like the two around it: the switch is the room's, and a
                // card that says "not muted" would un-draw the button for everyone who can press it.
                AllInRoomMuted = _state.AllInRoomMuted,
                CanMute = false,
                OpeningConnection = false,
            }
        );
    }
}
