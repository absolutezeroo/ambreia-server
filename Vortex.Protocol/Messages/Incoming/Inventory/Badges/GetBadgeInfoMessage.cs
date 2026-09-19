using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Inventory.Badges;

/// <summary>
/// "What do you know about this badge?" — sent by the badge display furni when someone looks at it.
/// </summary>
/// <remarks>
/// One string, the badge code (WIN63 <c>_composers[3159] = _SafePkg_2020/_SafeCls_3448</c>, whose
/// constructor takes a single String). The sender is
/// <c>ui/handler/FurnitureBadgeDisplayWidgetHandler.as:151</c>, and it drops the answer unless the
/// code comes back matching the one it asked for.
/// </remarks>
public record GetBadgeInfoMessage : IMessageEvent
{
    public required string BadgeCode { get; init; }
}
