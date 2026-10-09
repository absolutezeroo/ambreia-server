using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Interaction;

/// <summary>The player aims at this room object — an avatar — or clears its target with 0
/// (spec F3b §7.1, 9010).</summary>
public record RpgTargetSelectMessage : IMessageEvent
{
    public required int ObjectId { get; init; }
}
