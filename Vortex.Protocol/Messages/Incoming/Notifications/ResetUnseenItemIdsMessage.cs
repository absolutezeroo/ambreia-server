using System.Collections.Immutable;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Notifications;

/// <summary>
/// Some, not all, of a tab's items have been seen (<c>UnseenItemTracker.resetItems</c>, header
/// 3771) — the narrower sibling of <see cref="ResetUnseenItemsMessage"/>.
/// </summary>
public record ResetUnseenItemIdsMessage : IMessageEvent
{
    /// <summary>The tab, as <c>InventoryCategory</c> numbers it.</summary>
    public required int Category { get; init; }

    /// <summary>The items within it the player has now looked at.</summary>
    public required ImmutableArray<int> ItemIds { get; init; }
}
