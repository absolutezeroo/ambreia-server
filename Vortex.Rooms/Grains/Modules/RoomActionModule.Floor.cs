using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomActionModule
{
    public async Task<bool> PlaceFloorItemAsync(
        ActionContext ctx,
        FurnitureItemSnapshot snapshot,
        int x,
        int y,
        Rotation rot,
        CancellationToken ct
    )
    {
        IRoomItem item = _roomGrain._itemsLoader.CreateFromFurnitureItemSnapshot(snapshot);

        if (item is not IRoomFloorItem floorItem)
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        if (!await _roomGrain.SecurityModule.CanManipulateFurniAsync(ctx))
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
        }

        if (
            !await _roomGrain.FurniModule.ValidateNewFloorItemPlacementAsync(
                ctx,
                floorItem,
                x,
                y,
                rot
            )
        )
        {
            return await RefusePlacementAsync(ctx);
        }

        // The row enters the room before the room does anything with it. The other order puts the
        // item on the floor for everyone while the database still says it is in the hand, which is
        // what a player would read as having been duplicated -- and the write-behind tick no longer
        // corrects it, because a position flush has stopped asserting where an item lives.
        if (
            !await _roomGrain._furnitureLocations.ClaimIntoRoomAsync(
                floorItem.ObjectId.Value,
                item.OwnerId.Value,
                _roomGrain.RoomId.Value,
                ct
            )
        )
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
        }

        if (!await _roomGrain.FurniModule.PlaceFloorItemAsync(ctx, floorItem, x, y, rot, ct))
        {
            // The room refused it after the row was already ours. Hand it back rather than leaving
            // a row that says a room holds furniture the room has never heard of.
            await _roomGrain._furnitureLocations.ReleaseFromRoomAsync(
                floorItem.ObjectId.Value,
                _roomGrain.RoomId.Value,
                item.OwnerId.Value,
                ct
            );

            return false;
        }

        // Detached: the room is not waiting for the event's subscribers to answer before the next
        // click can be served. See RoomGrain.PublishDetached.
        _roomGrain.PublishDetached(
            new ItemPlacedEvent(
                item.ObjectId.Value,
                ctx.PlayerId.Value,
                item.OwnerId.Value,
                _roomGrain.RoomId.Value,
                JsonSerializer.Serialize(
                    new
                    {
                        x,
                        y,
                        rotation = rot.ToString(),
                    }
                ),
                snapshot.Definition.Id,
                LogicName: snapshot.Definition.LogicName
            )
        );

        return true;
    }

    public async Task<bool> MoveFloorItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int x,
        int y,
        Rotation rot,
        CancellationToken ct
    )
    {
        if (await _roomGrain.SecurityModule.CanManipulateFurniAsync(ctx))
        {
            if (
                !await _roomGrain.FurniModule.ValidateFloorItemPlacementAsync(
                    ctx,
                    itemId,
                    x,
                    y,
                    rot
                )
            )
            {
                return await RefusePlacementAsync(ctx);
            }
        }
        else
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToManipulateFurni);
        }

        // Read before the move, because after it there is nothing left to compare against: the
        // client turns a piece by moving it to the square it is already on.
        bool rotatedInPlace =
            _roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? before)
            && before is IRoomFloorItem previous
            && previous.GetSnapshot() is { } was
            && was.X == x
            && was.Y == y
            && was.Rotation != rot;

        if (!await _roomGrain.FurniModule.MoveFloorItemByIdAsync(ctx, itemId, x, y, null, rot, ct))
        {
            return false;
        }

        // Detached, and this is the one that fires most: a drag across a room is a move per tile.
        _roomGrain.PublishDetached(
            new ItemMovedEvent(
                itemId.Value,
                ctx.PlayerId.Value,
                _roomGrain.RoomId.Value,
                JsonSerializer.Serialize(
                    new
                    {
                        x,
                        y,
                        rotation = rot.ToString(),
                    }
                ),
                rotatedInPlace
            )
        );

        return true;
    }
}
