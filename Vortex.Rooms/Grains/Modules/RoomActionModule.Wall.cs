using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;

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
            return await RefusePlacementAsync(ctx);
        }

        if (
            !await ClaimAndPlaceAsync(
                item,
                () =>
                    _roomGrain.FurniModule.PlaceWallItemAsync(
                        ctx,
                        wallItem,
                        x,
                        y,
                        z,
                        wallOffset,
                        rot,
                        ct
                    ),
                ct
            )
        )
        {
            return false;
        }

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
                LogicName: snapshot.Definition.LogicName
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
            return await RefusePlacementAsync(ctx);
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
