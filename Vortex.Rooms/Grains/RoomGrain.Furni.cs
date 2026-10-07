using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<bool> RemoveItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct
    )
    {
        try
        {
            if (!await ActionModule.RemoveItemByIdAsync(ctx, itemId, ct))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to remove item {ItemId} from room {RoomId}",
                itemId,
                _state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> UseItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        try
        {
            if (!await ActionModule.UseItemByIdAsync(ctx, itemId, ct, param))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to use item {ItemId} in room {RoomId}",
                itemId,
                _state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> ClickItemByIdAsync(
        ActionContext ctx,
        RoomObjectId itemId,
        CancellationToken ct,
        int param = -1
    )
    {
        try
        {
            if (!await ActionModule.ClickItemByIdAsync(ctx, itemId, ct, param))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to click item {ItemId} in room {RoomId}",
                itemId,
                _state.RoomId
            );

            return false;
        }
    }

    public Task<ImmutableDictionary<PlayerId, string>> GetAllOwnersAsync(CancellationToken ct) =>
        FurniModule.GetAllOwnersAsync(ct);

    public Task<RoomItemSnapshot?> GetItemSnapshotByIdAsync(
        RoomObjectId itemId,
        CancellationToken ct
    ) =>
        Task.FromResult(
            _state.ItemsById.TryGetValue(itemId, out IRoomItem? item) ? item.GetSnapshot() : null
        );

    public async Task SetFloorItemStateAsync(RoomObjectId itemId, int state, CancellationToken ct)
    {
        if (!_state.ItemsById.TryGetValue(itemId, out IRoomItem? item))
        {
            return;
        }

        await item.Logic.SetStateAsync(state);
    }

    internal async Task FlushDirtyItemsAsync(CancellationToken ct)
    {
        // A zone's decor has no furniture rows: its ids can equal another room's rows, and a flush
        // would overwrite them. It only changes by publishing a new version.
        if (_state.Zone is not null)
        {
            _state.DirtyItemIds.Clear();

            return;
        }

        if (_state.DirtyItemIds.Count == 0)
        {
            return;
        }

        // The ids going over, held so that exactly those can be dropped once they are accepted.
        RoomObjectId[] flushing = [.. _state.DirtyItemIds];

        List<RoomItemSnapshot> batch = new List<RoomItemSnapshot>();

        batch.AddRange(
            flushing
                .Select(x =>
                    _state.ItemsById.TryGetValue(x, out IRoomItem? item) ? item.GetSnapshot() : null
                )
                .Where(x => x is not null)!
        );

        await _grainFactory
            .GetRoomPersistenceGrain(_state.RoomId)
            .EnqueueDirtyItemsAsync(_state.RoomId, batch, ct);

        // Only now. Clearing before the handover meant a failed enqueue left the snapshots in
        // neither queue -- the room had forgotten them and the persistence grain never received
        // them -- so the positions stayed lost until something moved the item again. The
        // persistence grain does keep its batch across a failed SaveChanges, but that protection
        // only starts once it has accepted the batch; this is the window before that.
        //
        // Removing the flushed ids rather than clearing the set, because anything marked dirty
        // while the call was in flight has to survive it.
        foreach (RoomObjectId id in flushing)
        {
            _state.DirtyItemIds.Remove(id);
        }
    }
}
