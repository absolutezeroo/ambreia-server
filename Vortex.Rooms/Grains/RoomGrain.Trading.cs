using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Players;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public Task OpenTradeAsync(PlayerId requesterId, int otherRoomObjectId, CancellationToken ct) =>
        TradingSystem.OpenTradeAsync(requesterId, otherRoomObjectId, ct);

    public Task AddTradeItemsAsync(
        PlayerId requesterId,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    ) => TradingSystem.AddTradeItemsAsync(requesterId, itemIds, ct);

    public Task RemoveTradeItemAsync(PlayerId requesterId, int itemId, CancellationToken ct) =>
        TradingSystem.RemoveTradeItemAsync(requesterId, itemId, ct);

    public Task SetTradeAcceptAsync(PlayerId requesterId, bool accepted, CancellationToken ct) =>
        TradingSystem.SetTradeAcceptAsync(requesterId, accepted, ct);

    public Task ConfirmTradeAsync(PlayerId requesterId, bool confirm, CancellationToken ct) =>
        TradingSystem.ConfirmTradeAsync(requesterId, confirm, ct);

    public Task CloseTradeAsync(PlayerId requesterId, CancellationToken ct) =>
        TradingSystem.CloseTradeAsync(requesterId, ct);

    /// <summary>Tears down any trade a leaving/disconnecting player is in, notifying the participant
    /// who is still present. Called from <c>RoomGrain.Avatar.cs</c> when an avatar is removed.</summary>
    internal Task CloseTradeForLeavingPlayerAsync(PlayerId playerId, CancellationToken ct) =>
        TradingSystem.CloseTradeForLeavingPlayerAsync(playerId, ct);
}
