using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Events.Registry;
using Vortex.Players.Configuration;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Chat;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;

namespace Vortex.Players.Chat;

/// <summary>
/// The chat (spec F3c §4, §14): a message goes through its channel, the cleaned text, the declared
/// rules by priority, the links, then <see cref="ChatSendingEvent"/>, and its channel routes it.
/// Every channel, rule and link kind is declared by DI by the module that owns it; this class names
/// none of them. The last channel declared for a key wins.
/// </summary>
internal sealed class ChatPipeline
{
    public const string Empty = "empty";
    public const string UnknownChannel = "unknown_channel";

    /// <summary>A <see cref="ChatSendingEvent"/> cancelled without a reason.</summary>
    public const string Refused = "refused";

    private readonly Dictionary<string, IChatChannel> _channels = new(StringComparer.Ordinal);
    private readonly IChatRule[] _rules;
    private readonly ChatLinkFilter _links;
    private readonly ICancellableEventPublisher _cancellable;
    private readonly IEventPublisher _events;
    private readonly IVortexMetrics _metrics;
    private readonly ILogger<ChatPipeline> _logger;

    public ChatPipeline(
        IEnumerable<IChatChannel> channels,
        IEnumerable<IChatRule> rules,
        IEnumerable<IChatLinkKind> linkKinds,
        IOptions<ChatConfig> config,
        ICancellableEventPublisher cancellable,
        IEventPublisher events,
        IVortexMetrics metrics,
        ILogger<ChatPipeline> logger
    )
    {
        _cancellable = cancellable;
        _events = events;
        _metrics = metrics;
        _logger = logger;

        foreach (IChatChannel channel in channels)
        {
            if (_channels.TryGetValue(channel.Key, out IChatChannel? replaced))
            {
                logger.LogInformation(
                    "Chat channel {Key}: {Type} replaces {Replaced}.",
                    channel.Key,
                    channel.GetType().Name,
                    replaced.GetType().Name
                );
            }

            _channels[channel.Key] = channel;
        }

        _rules =
        [
            .. rules
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.GetType().FullName, StringComparer.Ordinal),
        ];
        _links = new ChatLinkFilter(linkKinds, config.Value.MaxLinks);

        logger.LogInformation(
            "Chat channels {Channels}; rules in order {Rules}.",
            string.Join(", ", _channels.Keys),
            string.Join(", ", _rules.Select(r => r.GetType().Name))
        );
    }

    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>A player's message: a refusal code for the sender alone, or null once routed — or
    /// once a delivery failed, which is logged and never answered (spec F3c §11).</summary>
    public async Task<string?> SendAsync(
        ChatSend send,
        ChatSenderState state,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        string? refusal = await RefuseOrRouteAsync(send, state, routing, ct);

        if (refusal is not null)
        {
            _metrics.ChatRefused(refusal);
        }

        return refusal;
    }

    /// <summary>A line from the server (spec F3c §9) to the player behind
    /// <paramref name="routing"/>, through the declared system channel; no rule, no link check.</summary>
    public async Task SendSystemAsync(string text, IChatRouting routing, CancellationToken ct)
    {
        if (!_channels.TryGetValue(ChatLine.SystemChannel, out IChatChannel? channel))
        {
            _logger.LogWarning("No chat channel {Key} is declared.", ChatLine.SystemChannel);

            return;
        }

        await channel.RouteAsync(
            new ChatLine(
                ChatLine.SystemChannel,
                new PlayerId(0),
                string.Empty,
                text,
                Time.GetUtcNow()
            ),
            string.Empty,
            routing,
            ct
        );
    }

    private async Task<string?> RefuseOrRouteAsync(
        ChatSend send,
        ChatSenderState state,
        IChatRouting routing,
        CancellationToken ct
    )
    {
        if (
            !_channels.TryGetValue(send.Channel, out IChatChannel? channel)
            || !channel.PlayersCanSend
        )
        {
            return UnknownChannel;
        }

        string text = ChatText.Clean(send.Text);

        if (text.Length == 0)
        {
            return Empty;
        }

        ChatSend cleaned = send with { Text = text };

        foreach (IChatRule rule in _rules)
        {
            if (await rule.CheckAsync(cleaned, state, ct) is { } code)
            {
                return code;
            }
        }

        text = (await _links.FilterAsync(text, ct)).Trim();

        if (text.Length == 0)
        {
            return Empty;
        }

        ChatSendingEvent sending = new(send.Sender, channel.Key, send.Target) { Text = text };
        EventContext ctx = await _cancellable.PublishCancellableAsync(sending, ct);

        if (ctx.Cancel)
        {
            return string.IsNullOrEmpty(ctx.CancelReason) ? Refused : ctx.CancelReason;
        }

        ChatLine line = new(channel.Key, send.Sender, send.SenderName, sending.Text, send.Now);

        try
        {
            if (await channel.RouteAsync(line, send.Target, routing, ct) is { } code)
            {
                return code;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Chat delivery on {Channel} from player {PlayerId} failed.",
                channel.Key,
                send.Sender.Value
            );

            return null;
        }

        _metrics.ChatSent(channel.Key);

        try
        {
            await _events.PublishAsync(
                new ChatSentEvent(send.Sender, channel.Key, send.Target, sending.Text),
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "ChatSentEvent of player {PlayerId} on {Channel} failed.",
                send.Sender.Value,
                channel.Key
            );
        }

        return null;
    }
}
