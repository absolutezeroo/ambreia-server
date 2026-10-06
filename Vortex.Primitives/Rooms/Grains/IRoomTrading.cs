using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Rooms.Grains;

/// <summary>The room-scoped trade session state machine between two present players.</summary>
[Alias("Vortex.Primitives.Rooms.Grains.IRoomTrading")]
public interface IRoomTrading : IGrainWithIntegerKey
{
    /// <summary>Opens a trade between <paramref name="requesterId"/> and the avatar identified by
    /// <paramref name="otherRoomObjectId"/> (a room-object id, as the client sends it). Gated on the
    /// room's trade mode and, for owner/rights-only rooms, on both parties holding rights. On failure
    /// the requester receives a <c>TradeOpenFailed</c>; on success both receive a <c>TradingOpen</c>.</summary>
    public Task OpenTradeAsync(PlayerId requesterId, int otherRoomObjectId, CancellationToken ct);

    /// <summary>Adds one or more of the requester's inventory items to their side of the offer.
    /// Non-owned or non-tradeable ids are skipped. Any change resets both sides' acceptance.</summary>
    public Task AddTradeItemsAsync(
        PlayerId requesterId,
        IReadOnlyList<int> itemIds,
        CancellationToken ct
    );

    /// <summary>Removes an item the requester previously offered. Resets both sides' acceptance.</summary>
    public Task RemoveTradeItemAsync(PlayerId requesterId, int itemId, CancellationToken ct);

    /// <summary>Sets the requester's acceptance in the building phase. When both accept, the trade
    /// advances to the confirmation phase.</summary>
    public Task SetTradeAcceptAsync(PlayerId requesterId, bool accepted, CancellationToken ct);

    /// <summary>In the confirmation phase, records the requester's final confirm
    /// (<paramref name="confirm"/> true) or aborts the trade (false). When both confirm, the item
    /// swap is committed atomically.</summary>
    public Task ConfirmTradeAsync(PlayerId requesterId, bool confirm, CancellationToken ct);

    /// <summary>Cancels the requester's active trade, notifying both parties.</summary>
    public Task CloseTradeAsync(PlayerId requesterId, CancellationToken ct);
}
