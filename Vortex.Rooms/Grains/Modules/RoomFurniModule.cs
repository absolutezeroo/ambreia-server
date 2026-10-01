using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Events.RoomItem;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomFurniModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    public Task<ImmutableDictionary<PlayerId, string>> GetAllOwnersAsync(CancellationToken ct)
    {
        return Task.FromResult(_roomGrain._state.OwnerNamesById.ToImmutableDictionary());
    }

    internal async Task EnsureFurniLoadedAsync(CancellationToken ct)
    {
        if (_roomGrain._state.IsFurniLoaded)
        {
            return;
        }

        (
            IReadOnlyList<IRoomFloorItem> floorItems,
            IReadOnlyList<IRoomWallItem> wallItems,
            IReadOnlyDictionary<PlayerId, string> ownerNames
        ) = await _roomGrain._itemsLoader.LoadByRoomIdAsync(
            (RoomId)_roomGrain.GetPrimaryKeyLong(),
            ct
        );

        foreach ((PlayerId id, string name) in ownerNames)
        {
            _roomGrain._state.OwnerNamesById.TryAdd(id, name);
        }

        _roomGrain._state.IsTileComputationPaused = true;

        foreach (IRoomFloorItem item in floorItems)
        {
            await _roomGrain.ObjectModule.AttatchObjectAsync(item, ct);
        }

        _roomGrain._state.IsTileComputationPaused = false;

        _roomGrain.MapModule.ComputeAllTiles();
        _roomGrain._state.DirtyHeightTileIds.Clear();

        foreach (IRoomWallItem item in wallItems)
        {
            await _roomGrain.ObjectModule.AttatchObjectAsync(item, ct);
        }

        _roomGrain._state.IsFurniLoaded = true;
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

        await item.Logic.OnUseAsync(ctx, param, ct);

        // A player-driven "use" feeds the wired "furni is used" (USE_STUFF) trigger. Distinct from
        // RoomItemStateChangedEvent, which fires for any state change and carries no using player.
        await _roomGrain.PublishRoomEventAsync(
            new RoomItemUsedEvent
            {
                RoomId = _roomGrain.RoomId,
                CausedBy = ctx,
                ObjectId = itemId,
            },
            ct
        );

        return true;
    }

    public async Task<bool> ClickItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        // The client clicks its own placement preview before PlaceObject lands, and an item another
        // player has just picked up is still on its screen. Neither is a live item, and neither is
        // an error worth a stack trace.
        if (!_roomGrain._state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            return false;
        }

        await item.Logic.OnClickAsync(ctx, param, ct);

        return true;
    }

    public Task<RoomItemSnapshot?> GetItemSnapshotByIdAsync(
        RoomObjectId objectId,
        CancellationToken ct
    )
    {
        return Task.FromResult(
            _roomGrain._state.ItemsById.TryGetValue(objectId, out IRoomItem? item)
                ? item.GetSnapshot()
                : null
        );
    }
}
