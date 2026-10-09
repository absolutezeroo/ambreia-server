using System;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Chat;

/// <summary>One chat line as players read it (spec F3c §10.2).</summary>
public sealed record ChatLine(
    string Channel,
    PlayerId SenderId,
    string SenderName,
    string Text,
    DateTimeOffset SentAt
)
{
    /// <summary>The channel only the server writes in (spec F3c §9).</summary>
    public const string SystemChannel = "system";

    /// <summary>For a whisper, the other player of the conversation; empty otherwise.</summary>
    public string Target { get; init; } = string.Empty;
}
