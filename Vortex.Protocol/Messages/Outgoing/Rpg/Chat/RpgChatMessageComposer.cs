using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Chat;

/// <summary>
/// One chat line (spec F3c §10.2, 9010): channel, sender, text with its <c>[item:…]</c> links,
/// server time, and for a whisper the other player of the conversation. Goes through the room and
/// world-chat streams.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RpgChatMessageComposer : IComposer
{
    [Id(0)]
    public required string Channel { get; init; }

    /// <summary>0 for <c>system</c>.</summary>
    [Id(1)]
    public required int SenderId { get; init; }

    [Id(2)]
    public required string SenderName { get; init; }

    [Id(3)]
    public required string Text { get; init; }

    /// <summary>Seconds since the Unix epoch, UTC.</summary>
    [Id(4)]
    public required int SentAt { get; init; }

    /// <summary>For a whisper, the other player's name; empty otherwise.</summary>
    [Id(5)]
    public required string Target { get; init; }
}
