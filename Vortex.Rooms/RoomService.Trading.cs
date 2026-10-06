using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;

namespace Vortex.Rooms;

internal sealed partial class RoomService
{
    public async Task OpenTradeAsync(ActionContext ctx, int otherRoomObjectId, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .OpenTradeAsync(ctx.PlayerId, otherRoomObjectId, ct)
            .ConfigureAwait(false);
    }

    public async Task AddTradeItemsAsync(
        ActionContext ctx,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || itemIds.Count == 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .AddTradeItemsAsync(ctx.PlayerId, itemIds, ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveTradeItemAsync(ActionContext ctx, int itemId, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0 || itemId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .RemoveTradeItemAsync(ctx.PlayerId, itemId, ct)
            .ConfigureAwait(false);
    }

    public async Task SetTradeAcceptAsync(ActionContext ctx, bool accepted, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .SetTradeAcceptAsync(ctx.PlayerId, accepted, ct)
            .ConfigureAwait(false);
    }

    public async Task ConfirmTradeAsync(ActionContext ctx, bool confirm, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .ConfirmTradeAsync(ctx.PlayerId, confirm, ct)
            .ConfigureAwait(false);
    }

    public async Task CloseTradeAsync(ActionContext ctx, CancellationToken ct)
    {
        if (ctx.PlayerId <= 0 || ctx.RoomId <= 0)
        {
            return;
        }

        await _grainFactory
            .GetRoomTrading(ctx.RoomId)
            .CloseTradeAsync(ctx.PlayerId, ct)
            .ConfigureAwait(false);
    }
}
