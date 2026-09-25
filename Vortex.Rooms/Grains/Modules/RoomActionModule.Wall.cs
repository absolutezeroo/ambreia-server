using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Database.Context;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Rooms.Grains.Systems;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomActionModule
{
    public async Task<bool> PlaceWallItemAsync(
        ActionContext ctx,
        FurnitureItemSnapshot snapshot,
        int x,
        int y,
        Altitude z,
        int wallOffset,
        Rotation rot,
        CancellationToken ct
    )
    {
        if (!await _roomGrain.SecurityModule.CanPlaceFurniAsync(ctx))
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
        }

        IRoomItem item = _roomGrain._itemsLoader.CreateFromFurnitureItemSnapshot(snapshot);

        if (item is not IRoomWallItem wallItem)
        {
            throw new VortexException(VortexErrorCodeEnum.WallItemNotFound);
        }

        if (
            !await _roomGrain.FurniModule.ValidateNewWallItemPlacementAsync(
                ctx,
                wallItem,
                x,
                y,
                z,
                wallOffset,
                rot
            )
        )
        {
            throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
        }

        // As on the floor: the row enters the room before the room does, so the item is never on a
        // wall for everyone while the database still says it is in somebody's hand.
        await using (VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(ct))
        {
            int claimed = await RoomFurnitureLocationStore.ClaimIntoRoomAsync(
                db,
                wallItem.ObjectId.Value,
                item.OwnerId.Value,
                _roomGrain.RoomId.Value,
                ct
            );

            if (claimed == 0)
            {
                throw new VortexException(VortexErrorCodeEnum.NoPermissionToPlaceFurni);
            }
        }

        if (
            !await _roomGrain.FurniModule.PlaceWallItemAsync(
                ctx,
                wallItem,
                x,
                y,
                z,
                wallOffset,
                rot,
                ct
            )
        )
        {
            await using VortexDbContext db = await _roomGrain._dbCtxFactory.CreateDbContextAsync(
                ct
            );

            await RoomFurnitureLocationStore.ReleaseFromRoomAsync(
                db,
                wallItem.ObjectId.Value,
                _roomGrain.RoomId.Value,
                item.OwnerId.Value,
                ct
            );

            return false;
        }

        IInventoryGrain inventory = _roomGrain._grainFactory.GetInventoryGrain(item.OwnerId);

        await inventory.RemoveFurnitureAsync(item.ObjectId, ct);

        // Detached, as on the floor: see RoomGrain.PublishDetached.
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
                        z = z.ToString(),
                        wallOffset,
                        rotation = rot.ToString(),
                    }
                ),
                snapshot.Definition.Id,
                // The one thing a definition id cannot say, and the thing an operator means by
                // "a floor item" when they write a task.
                IsWallItem: true,
                FirstOfItsKindInRoom: IsOnlyOneOfItsKind(wallItem)
            )
        );

        return true;
    }

    public async Task<bool> MoveWallItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        int x,
        int y,
        Altitude z,
        int wallOffset,
        Rotation rot,
        CancellationToken ct
    )
    {
        if (!await _roomGrain.SecurityModule.CanManipulateFurniAsync(ctx))
        {
            throw new VortexException(VortexErrorCodeEnum.NoPermissionToManipulateFurni);
        }

        if (
            !await _roomGrain.FurniModule.ValidateWallItemPlacementAsync(
                ctx,
                itemId,
                x,
                y,
                z,
                wallOffset,
                rot
            )
        )
        {
            throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
        }

        if (
            !await _roomGrain.FurniModule.MoveWallItemByIdAsync(
                ctx,
                itemId,
                x,
                y,
                z,
                wallOffset,
                rot,
                ct
            )
        )
        {
            return false;
        }

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
                        z = z.ToString(),
                        wallOffset,
                        rotation = rot.ToString(),
                    }
                )
            )
        );

        return true;
    }
}
