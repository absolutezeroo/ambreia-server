using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Rpg.Chat;

/// <summary>A chat message the player typed (spec F3c §10.1, 9011).</summary>
public record RpgChatSendMessage : IMessageEvent
{
    /// <summary>A channel key: <c>general</c>, <c>world</c>, <c>whisper</c>, <c>group</c>, <c>guild</c>…</summary>
    public required string Channel { get; init; }

    /// <summary>A player name for <c>whisper</c>, empty otherwise.</summary>
    public required string Target { get; init; }

    public required string Text { get; init; }
}
