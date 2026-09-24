using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired;
using Vortex.Protocol.Messages.Incoming.Userdefinedroomevents;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired;

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

        IRoomFloorItem? rentedSpace = null;

        if (await _roomGrain.SecurityModule.CanManipulateFurniAsync(ctx))
        {
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
                throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
            }
        }
        else
        {
            rentedSpace = await _roomGrain.SecurityModule.FindRentedSpaceForPlayerAsync(
                ctx.PlayerId.Value,
                ct
            );

            if (rentedSpace is null)
            {
                throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
            }

            if (
                !await _roomGrain.FurniModule.ValidateNewFloorItemPlacementInRentedSpaceAsync(
                    ctx,
                    floorItem,
                    x,
                    y,
                    rot,
                    rentedSpace
                )
            )
            {
                throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
            }
        }

        // The row enters the room before the room does anything with it. The other order puts the
        // item on the floor for everyone while the database still says it is in the hand, which is
        // what a player would read as having been duplicated -- and the write-behind tick no longer
        // corrects it, because a position flush has stopped asserting where an item lives.
        await using (VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct))
        {
            int claimed = await RoomFurnitureLocationStore.ClaimIntoRoomAsync(
                db,
                floorItem.ObjectId.Value,
                item.OwnerId.Value,
                _roomGrain.RoomId.Value,
                ct
            );

            if (claimed == 0)
            {
                throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
            }
        }

        if (!await _roomGrain.FurniModule.PlaceFloorItemAsync(ctx, floorItem, x, y, rot, ct))
        {
            // The room refused it after the row was already ours. Hand it back rather than leaving
            // a row that says a room holds furniture the room has never heard of.
            await using VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(
                ct
            );

            await RoomFurnitureLocationStore.ReleaseFromRoomAsync(
                db,
                floorItem.ObjectId.Value,
                _roomGrain.RoomId.Value,
                item.OwnerId.Value,
                ct
            );

            return false;
        }

        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(item.OwnerId);

        await inventory.RemoveFurnitureAsync(item.ObjectId, ct);

        if (rentedSpace is not null)
        {
            try
            {
                await using VortexDbContext db =
                    await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct);
                await db
                    .Furnitures.Where(f => f.Id == floorItem.ObjectId.Value && f.DeletedAt == null)
                    .ExecuteUpdateAsync(
                        s =>
                            s.SetProperty(
                                f => f.RentableSpaceFurnitureEntityId,
                                (int?)rentedSpace.ObjectId.Value
                            ),
                        ct
                    );
            }
            catch (Exception ex)
            {
                _roomGrain._logger.LogWarning(
                    ex,
                    "Failed to tag furniture {FurniId} with rentable space {SpaceId}",
                    floorItem.ObjectId.Value,
                    rentedSpace.ObjectId.Value
                );
            }
        }

        // Detached: the room is not waiting for the placer's quest, achievement and daily-task
        // grains to answer before the next click can be served. See RoomGrain.PublishDetached.
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
                snapshot.Definition.Id
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
                throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
            }
        }
        else
        {
            IRoomFloorItem? rentedSpace =
                await _roomGrain.SecurityModule.FindRentedSpaceForOwnedItemAsync(ctx, itemId, ct);

            if (rentedSpace is null)
            {
                throw new VortexException(VortexErrorCodeEnum.NoPermissionToManipulateFurni);
            }

            if (
                !await _roomGrain.FurniModule.ValidateFloorItemMoveInRentedSpaceAsync(
                    ctx,
                    itemId,
                    x,
                    y,
                    rot,
                    rentedSpace
                )
            )
            {
                throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
            }
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

    public async Task<bool> ApplyWiredUpdateAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        WiredUpdateRequest update,
        CancellationToken ct
    )
    {
        // Every wired save the client can send -- action, addon, condition, selector, trigger,
        // variable -- lands here, and none of them checked anything but "the item exists and it is
        // wired". Any visitor standing in a room could rewrite its owner's wired configuration.
        //
        // Rights is the same floor the place and move paths use, and the same one Arcturus applies
        // to this packet (evidence, not authority). A refusal stays silent: what the official server
        // answers to a refused wired save is recorded as unknown in the specs
        // (docs/habbo-specs/unknowns/uk_91fc7e0d6b.yaml), so this returns false through the same
        // path as the six guards already here rather than inventing a validation-error packet.
        if (!await _roomGrain.SecurityModule.CanManipulateFurniAsync(ctx))
        {
            return false;
        }

        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        if (item.Logic is not FurnitureWiredLogic wiredLogic)
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        // The hotel-wide word filter, on the part of the string players read — which only the box
        // knows: a message, a line a bot says. A bot's name, a trigger's keyword and the like are
        // keys and are not the filter's business (FurnitureWiredLogic.ProseInStringParam).
        if (wiredLogic.ProseInStringParam(update.StringParam) is { Length: > 0 } prose)
        {
            string stringParam = update.StringParam;
            (int start, int length) = prose;
            WordFilterResult filtered = _roomGrain._wordFilter.Apply(
                stringParam.Substring(start, length),
                WordFilterSource.Wired,
                ctx.PlayerId
            );

            if (filtered.Refused)
            {
                await _roomGrain
                    ._grainFactory.GetPlayerPresenceGrain(ctx.PlayerId)
                    .SendComposerAsync(
                        WordFilterNotice.Create(_roomGrain._wordFilter.RefusedMessage)
                    )
                    .ConfigureAwait(true);

                return false;
            }

            if (filtered.Changed)
            {
                update = update with
                {
                    StringParam =
                        stringParam[..start] + filtered.Text + stringParam[(start + length)..],
                };
            }
        }

        if (!await wiredLogic.ApplyWiredUpdateAsync(ctx, update, ct))
        {
            return false;
        }

        return true;
    }
}
