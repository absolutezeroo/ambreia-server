using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Database.Context;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Furniture;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Rooms.Grains.Systems;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomActionModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    /// <summary>
    /// The room's distinct definitions, and its items sharing <paramref name="placed"/>'s logic,
    /// <paramref name="placed"/> included either way. A scan, but only on a placement, which is a
    /// click.
    /// </summary>
    private (int Kinds, int SameLogic) CountAround(IRoomItem placed)
    {
        HashSet<int> kinds = [placed.Definition.Id];
        int sameLogic = 1;

        foreach (IRoomItem item in _roomGrain._state.ItemsById.Values)
        {
            if (item.ObjectId == placed.ObjectId)
            {
                continue;
            }

            kinds.Add(item.Definition.Id);

            if (item.Definition.LogicName == placed.Definition.LogicName)
            {
                sameLogic++;
            }
        }

        return (kinds.Count, sameLogic);
    }

    /// <summary>
    /// Refuses a placement or a move onto a square the item cannot take: the hotel's bubble to the
    /// player, and <c>false</c> to the caller.
    /// </summary>
    /// <remarks>
    /// <c>false</c>, not an exception. The room grain turns either into <c>false</c>, which is what
    /// makes <c>RoomService</c> send the item's real position back so the client's copy snaps home;
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

    /// <param name="fromWired">
    /// True when the room's own wiring is doing this rather than a player.
    /// </param>
    /// <remarks>
    /// <paramref name="fromWired"/> skips the pickup-rights check and always sends the furni to its
    /// own owner, for the same reason <c>KickUserFromWiredAsync</c> and <c>MuteUserFromWiredAsync</c>
    /// exist: there is no actor to authorize. The authorization happened when the box was
    /// configured — saving a wired box goes through <c>CanManipulateFurniAsync</c> — and there is no
    /// <c>ctx.PlayerId</c> at firing time to send anything to, so "send to whoever picked it up" has
    /// no meaning here and would quietly resolve to player 0.
    /// </remarks>
    public async Task<bool> RemoveItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        bool fromWired = false
    )
    {
        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
        }

        FurniturePickupType pickupType = fromWired
            ? FurniturePickupType.SendToOwner
            : await _roomGrain.SecurityModule.GetFurniPickupTypeAsync(ctx);

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

        // The row leaves the room here, not two seconds later on the write-behind tick. Six paths
        // decide an item is free-standing by asking whether room_id is null -- a trade, a wired
        // chest, a jukebox, a marketplace listing, a wired settlement -- and until the flush caught
        // up they all refused an item the client had already put in the player's hand. Picking a
        // sofa up and dragging it straight into a trade did nothing, for as long as
        // DirtyItemsTickMs.
        //
        // Conditional on the row still being this room's, so a pickup racing another room's claim
        // loses rather than overwriting it.
        await using (VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct))
        {
            int released = await RoomFurnitureLocationStore.ReleaseFromRoomAsync(
                db,
                itemId.Value,
                _roomGrain.RoomId.Value,
                pickerId.Value,
                ct
            );

            if (released == 0)
            {
                throw new VortexException(VortexErrorCodeEnum.FloorItemNotFound);
            }
        }

        await _roomGrain.ObjectModule.RemoveObjectAsync(ctx, item, ct, pickerId);

        RoomItemSnapshot snapshot = item.GetSnapshot();

        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(snapshot.OwnerId);

        await inventory.AddFurnitureFromRoomItemSnapshotAsync(snapshot, ct);

        // Detached: the item is already in the inventory and the client already has its answer.
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

        FurnitureUsageType usagePolicy = item.Logic.GetUsagePolicy();

        bool canUse = await _roomGrain.SecurityModule.CanUseFurniAsync(ctx, usagePolicy);

        if (!canUse)
        {
            canUse =
                await _roomGrain.SecurityModule.FindRentedSpaceForOwnedItemAsync(ctx, itemId, ct)
                    is not null;
        }

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
