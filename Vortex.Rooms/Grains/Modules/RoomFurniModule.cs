using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        try
        {
            foreach (IRoomFloorItem item in floorItems)
            {
                await AttachLoadedItemAsync(item, ct);
            }
        }
        finally
        {
            _roomGrain._state.IsTileComputationPaused = false;
        }

        _roomGrain.MapModule.ComputeAllTiles();
        _roomGrain._state.DirtyHeightTileIds.Clear();

        foreach (IRoomWallItem item in wallItems)
        {
            await AttachLoadedItemAsync(item, ct);
        }

        _roomGrain._state.IsFurniLoaded = true;
    }

    /// <summary>
    /// One bad row -- a footprint past the map edge after a model change, a logic that no longer
    /// exists -- leaves that item out of the room, logged, instead of keeping the room shut: an
    /// exception here used to abort the load, and every retry then tripped over the items the
    /// failed attempt had already attached.
    /// </summary>
    private async Task AttachLoadedItemAsync(IRoomItem item, CancellationToken ct)
    {
        try
        {
            if (await _roomGrain.ObjectModule.AttatchObjectAsync(item, ct))
            {
                return;
            }

            _roomGrain._logger.LogWarning(
                "Item {ItemId} could not be placed in room {RoomId}; left out of the room.",
                item.ObjectId,
                _roomGrain.RoomId
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Item {ItemId} failed to load in room {RoomId}; left out of the room.",
                item.ObjectId,
                _roomGrain.RoomId
            );
        }
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

        // A player-driven "use", for room event listeners. Distinct from RoomItemStateChangedEvent, which fires for any state change and carries no using player.
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
