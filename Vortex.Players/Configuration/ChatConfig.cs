using System;
using System.Collections.Generic;

namespace Vortex.Players.Configuration;

/// <summary>The chat (spec F3c §14.4), section <c>Vortex:Chat</c>.</summary>
public sealed class ChatConfig
{
    public const string SECTION_NAME = "Vortex:Chat";

    /// <summary>The most characters a message keeps after cleaning; past it, refused.</summary>
    public int MaxLength { get; init; } = 200;

    /// <summary>The most links a message keeps; the next ones are removed.</summary>
    public int MaxLinks { get; init; } = 5;

    /// <summary>Per channel key, the least time between two messages of one player, in ms. A
    /// channel absent here is limited by the per-session packet limit alone.</summary>
    public Dictionary<string, int> RateLimits { get; init; } =
        new(StringComparer.Ordinal)
        {
            ["general"] = 1000,
            ["world"] = 10000,
            ["whisper"] = 1000,
            ["trade"] = 30000,
            ["recruitment"] = 30000,
        };
}
