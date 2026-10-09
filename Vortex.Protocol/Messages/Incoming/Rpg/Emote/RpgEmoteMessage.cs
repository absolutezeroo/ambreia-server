using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Emote;

/// <summary>The player does an RP action (9012): a key from <c>Vortex:Rooms:EmoteKeys</c>
/// (<c>wave</c>, <c>laugh</c>, <c>clap</c>, <c>dance</c>, <c>sad</c>, <c>surprised</c>).</summary>
public record RpgEmoteMessage : IMessageEvent
{
    public required string Key { get; init; }
}
