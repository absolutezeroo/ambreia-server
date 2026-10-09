using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Players.Configuration;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat.Rules;

/// <summary>
/// One message per window, per player and per channel (spec F3c §7), the window read from
/// <see cref="ChatConfig.RateLimits"/> by channel key. A channel absent there is not limited here.
/// Only a message that reaches this rule opens a window.
/// </summary>
internal sealed class ChatRateRule(IOptions<ChatConfig> config, ILogger<ChatRateRule> logger)
    : IChatRule
{
    public const string RateLimited = "rate_limited";

    private readonly IReadOnlyDictionary<string, int> _limits = config.Value.RateLimits;

    public int Priority => 200;

    public ValueTask<string?> CheckAsync(ChatSend send, ChatSenderState state, CancellationToken ct)
    {
        if (!_limits.TryGetValue(send.Channel, out int ms) || ms <= 0)
        {
            return ValueTask.FromResult<string?>(null);
        }

        ChatRateWindows windows = state.Get<ChatRateWindows>();

        if (
            windows.LastSent.TryGetValue(send.Channel, out DateTimeOffset last)
            && send.Now - last < TimeSpan.FromMilliseconds(ms)
        )
        {
            logger.LogDebug(
                "Chat rate-limited: player {PlayerId} on {Channel}.",
                send.Sender.Value,
                send.Channel
            );

            return ValueTask.FromResult<string?>(RateLimited);
        }

        windows.LastSent[send.Channel] = send.Now;

        return ValueTask.FromResult<string?>(null);
    }
}
