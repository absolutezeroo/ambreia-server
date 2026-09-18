using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Notifications;

/// <summary>
/// The player opened an inventory tab, so everything filed under it has now been seen
/// (<c>UnseenItemTracker.resetCategory</c>, header 699).
/// </summary>
public record ResetUnseenItemsMessage : IMessageEvent
{
    /// <summary>The tab, as <c>InventoryCategory</c> numbers it.</summary>
    public required int Category { get; init; }
}
