using System;
using System.Collections.Generic;

namespace Vortex.Players.Chat.Rules;

/// <summary>A sender's last message per rate-limited channel (spec F3c §7), kept in its
/// <c>ChatSenderState</c>. One entry per channel with a limit: bounded by the config.</summary>
internal sealed class ChatRateWindows
{
    public Dictionary<string, DateTimeOffset> LastSent { get; } = new(StringComparer.Ordinal);
}
