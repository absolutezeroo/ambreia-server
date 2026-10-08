using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomActionModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    /// <summary>
    /// Refuses a placement or a move onto a square the item cannot take: the hotel's bubble to the
    /// player, and <c>false</c> to the caller.
    /// </summary>
    /// <remarks>
    /// <c>false</c>, not an exception. The room grain turns either into <c>false</c>, which is what
    /// makes the caller send the item's real position back so the client's copy snaps home;
    /// a throw got there too, but logged a stack trace at error level for what is a player dropping
    /// a chair on a wall.
    /// </remarks>
    private async Task<bool> RefusePlacementAsync(ActionContext ctx)
    {
        if (ctx.PlayerId > 0)
        {
            await _roomGrain
                ._grainFactory.GetPlayerPresenceGrain(ctx.PlayerId)
                .SendComposerAsync(FurniPlacementNotice.Create())
                .ConfigureAwait(true);
        }

        return false;
    }

    /// <summary>
    /// Places an item from a player's hand, floor or wall alike. The row enters the room before the
    /// room does anything with it: the other order puts the item in the room for everyone while the
    /// database still says it is in the hand, which a player reads as a duplicate -- and the
    /// write-behind tick no longer corrects it, because a position flush has stopped asserting
    /// where an item lives. If the room then refuses it -- false or an exception -- the row is
    /// handed back rather than left saying a room holds furniture the room has never heard of.
    /// </summary>
    private async Task<bool> ClaimAndPlaceAsync(
        IRoomItem item,
        Func<Task<bool>> place,
        CancellationToken ct
    )
    {
        if (
            !await _roomGrain._furnitureLocations.ClaimIntoRoomAsync(
                item.ObjectId.Value,
                item.OwnerId.Value,
                _roomGrain.RoomId.Value,
                ct
            )
        )
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
        }

        bool placed;

        try
        {
            placed = await place();
        }
        catch
        {
            // A room that throws refused it as surely as one that answers false.
            await ReleaseAsync(item, ct);

            throw;
        }

        if (!placed)
        {
            await ReleaseAsync(item, ct);
        }

        return placed;
    }

    private Task<bool> ReleaseAsync(IRoomItem item, CancellationToken ct) =>
        _roomGrain._furnitureLocations.ReleaseFromRoomAsync(
            item.ObjectId.Value,
            _roomGrain.RoomId.Value,
            item.OwnerId.Value,
            ct
        );

    public async Task<bool> RemoveItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        FurniturePickupType pickupType = await _roomGrain.SecurityModule.GetFurniPickupTypeAsync(
            ctx
        );

        if (pickupType == FurniturePickupType.None)
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToManipulateFurni);
        }

        PlayerId previousOwnerId = item.OwnerId;
        PlayerId pickerId = item.OwnerId;

        if (pickupType is FurniturePickupType.SendToCtx)
        {
            pickerId = ctx.PlayerId;
        }

        item.SetOwnerId(pickerId);

        // The row leaves the room here, not two seconds later on the write-behind tick. Anything that
        // decides an item is free-standing asks whether room_id is null, and until the flush caught
        // up it would refuse an item the client had already put in the player's hand, for as long
        // as DirtyItemsTickMs.
        //
        // Conditional on the row still being this room's, so a pickup racing another room's claim
        // loses rather than overwriting it.
        if (
            !await _roomGrain._furnitureLocations.ReleaseFromRoomAsync(
                itemId.Value,
                _roomGrain.RoomId.Value,
                pickerId.Value,
                ct
            )
        )
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        await _roomGrain.ObjectModule.RemoveObjectAsync(ctx, item, ct, pickerId);

        RoomItemSnapshot snapshot = item.GetSnapshot();

        // Detached: the item is already back with its owner and the client already has its answer.
        // See RoomGrain.PublishDetached.
        _roomGrain.PublishDetached(
            new ItemPickedUpEvent(
                itemId.Value,
                ctx.PlayerId.Value,
                previousOwnerId.Value,
                pickerId.Value,
                _roomGrain.RoomId.Value
            )
        );

        return true;
    }

    public async Task<bool> UseItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        if (await _roomGrain.ZoneInteractions.TryUseAsync(ctx, item, param, ct))
        {
            return true;
        }

        FurnitureUsageType usagePolicy = item.Logic.GetUsagePolicy();

        bool canUse = await _roomGrain.SecurityModule.CanUseFurniAsync(ctx, usagePolicy);

        if (!canUse || !await _roomGrain.FurniModule.UseItemByIdAsync(ctx, itemId, ct, param))
        {
            return false;
        }

        return true;
    }

    public Task<bool> ClickItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    ) => _roomGrain.FurniModule.ClickItemByIdAsync(ctx, itemId, ct, param);
}
