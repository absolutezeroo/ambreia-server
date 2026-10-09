using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortex.Events.Registry;
using Vortex.Players.Chat;
using Vortex.Players.Chat.Channels;
using Vortex.Players.Chat.Rules;
using Vortex.Players.Configuration;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Chat;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Chat;

/// <summary>
/// The chat pipeline (spec F3c §4–§7, §14): channel, text, declared rules by priority, links,
/// the cancellable event, then the channel routes. Every channel, rule and link kind here is
/// declared by the test: the pipeline names none of them.
/// </summary>
public sealed class ChatPipelineTests
{
    private const string AmberShard = "item.material.amber_shard";

    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingChatRouting _routing = new();
    private readonly ChatSenderState _state = new();
    private readonly List<IEvent> _published = [];

    [Fact]
    public async Task AnUndeclaredChannel_IsUnknown()
    {
        (await SendAsync(Pipeline([Echo("general")]), "trade", "hi"))
            .Should()
            .Be("unknown_channel");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task AChannelKey_IsCaseSensitive()
    {
        (await SendAsync(Pipeline([Echo("general")]), "General", "hi"))
            .Should()
            .Be("unknown_channel");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerOnlyChannel_IsUnknownToPlayers()
    {
        (await SendAsync(Pipeline([Echo("system", playersCanSend: false)]), "system", "hi"))
            .Should()
            .Be("unknown_channel");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001\t\n")]
    public async Task EmptyOrBlankText_IsEmpty(string text)
    {
        (await SendAsync(Pipeline([Echo("general")]), "general", text)).Should().Be("empty");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task ControlCharacters_AreRemoved()
    {
        (await SendAsync(Pipeline([Echo("general")]), "general", "he\u0007llo\n"))
            .Should()
            .BeNull();
        Delivered.Should().Equal("hello");
    }

    [Fact]
    public async Task TwoHundredCharacters_Pass_TwoHundredOne_AreTooLong()
    {
        ChatPipeline pipeline = Pipeline([Echo("general")]);

        (await SendAsync(pipeline, "general", new string('a', 200))).Should().BeNull();
        (await SendAsync(pipeline, "general", new string('a', 201), T0.AddMinutes(1)))
            .Should()
            .Be("too_long");
        Delivered.Should().Equal(new string('a', 200));
    }

    [Fact]
    public async Task AKnownLink_IsKept_AnUnknownOne_Removed()
    {
        await SendAsync(
            Pipeline([Echo("general")]),
            "general",
            $"vend [item:{AmberShard}] et [item:item.fake]"
        );

        Delivered.Should().Equal($"vend [item:{AmberShard}] et");
    }

    [Fact]
    public async Task LinksPastTheFifth_AreRemoved()
    {
        await SendAsync(
            Pipeline([Echo("general")]),
            "general",
            string.Join(' ', Enumerable.Repeat($"[item:{AmberShard}]", 6))
        );

        Delivered.Single().Split($"[item:{AmberShard}]").Length.Should().Be(6, "five links kept");
    }

    [Fact]
    public async Task AMalformedOrUndeclaredToken_StaysText()
    {
        await SendAsync(Pipeline([Echo("general")]), "general", "[item:] [item: x] [quest:q.1]");

        Delivered.Should().Equal("[item:] [item: x] [quest:q.1]");
    }

    [Fact]
    public async Task AMessageOfInvalidLinksOnly_IsEmpty()
    {
        (await SendAsync(Pipeline([Echo("general")]), "general", "[item:item.fake]"))
            .Should()
            .Be("empty");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoWorldMessagesWithinTenSeconds_TheSecondIsRateLimited()
    {
        ChatPipeline pipeline = Pipeline([Echo("world")]);

        (await SendAsync(pipeline, "world", "un")).Should().BeNull();
        (await SendAsync(pipeline, "world", "deux", T0.AddSeconds(9))).Should().Be("rate_limited");
        (await SendAsync(pipeline, "world", "trois", T0.AddSeconds(10))).Should().BeNull();
        Delivered.Should().Equal("un", "trois");
    }

    [Fact]
    public async Task AChannelWithoutALimit_IsNotRateLimited()
    {
        ChatPipeline pipeline = Pipeline([Echo("trade")]);

        (await SendAsync(pipeline, "trade", "un")).Should().BeNull();
        (await SendAsync(pipeline, "trade", "deux")).Should().BeNull();
    }

    [Fact]
    public async Task TheLastChannelDeclared_ForAKey_Wins()
    {
        ChatPipeline pipeline = Pipeline([Refusing("group", "not_in_group"), Echo("group")]);

        (await SendAsync(pipeline, "group", "hi")).Should().BeNull();
        _routing.Deliveries.Select(d => d.To).Should().Equal("echo/0");
    }

    [Fact]
    public async Task ANewChannel_IsRoutedWithoutTouchingTheCore()
    {
        (await SendAsync(Pipeline([Echo("general"), Echo("trade")]), "trade", "vends épée"))
            .Should()
            .BeNull();
        Delivered.Should().Equal("vends épée");
    }

    [Fact]
    public async Task ADeclaredChannel_ReplacesTheShippedPlaceholder_WhateverTheOrder()
    {
        // Program.cs registers the RPG system modules before PlayerModule: a group module's channel
        // is declared before the shipped placeholder, and must still win.
        ChatPipeline pipeline = Pipeline([
            Echo("group"),
            new InactiveChatChannel("group", "not_in_group"),
        ]);

        (await SendAsync(pipeline, "group", "hi")).Should().BeNull();
        _routing.Deliveries.Select(d => d.To).Should().Equal("echo/0");
    }

    [Fact]
    public async Task ADeclaredRule_RefusesAtItsPriority()
    {
        bool muted = true;
        IChatRule mute = Rule(150, () => muted ? "muted" : null);
        ChatPipeline pipeline = Pipeline([Echo("general")], [.. ShippedRules(), mute]);

        (await SendAsync(pipeline, "general", new string('a', 201)))
            .Should()
            .Be("too_long", "the length (100) runs before the declared rule (150)");
        (await SendAsync(pipeline, "general", "hi")).Should().Be("muted");

        muted = false;

        (await SendAsync(pipeline, "general", "hi"))
            .Should()
            .BeNull("the rate rule (200) never ran on the refused message");
    }

    [Fact]
    public async Task ADeclaredLinkKind_KeepsItsValidTokens()
    {
        await SendAsync(
            Pipeline([Echo("general")], kinds: [Kind("quest", "quest.first")]),
            "general",
            "[quest:quest.first] [quest:quest.none]"
        );

        Delivered.Should().Equal("[quest:quest.first]");
    }

    [Fact]
    public async Task ACancelledSendingEvent_DeliversNothing()
    {
        string? reason = "muted";
        ChatPipeline pipeline = Pipeline(
            [Echo("general")],
            sending: _ => new EventContext
            {
                CorrelationId = "test",
                Cancel = true,
                CancelReason = reason,
            }
        );

        (await SendAsync(pipeline, "general", "hi")).Should().Be("muted");

        reason = null;

        (await SendAsync(pipeline, "general", "hi", T0.AddMinutes(1))).Should().Be("refused");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task ASendingEvent_MayRewriteTheText()
    {
        ChatPipeline pipeline = Pipeline(
            [Echo("general")],
            sending: e =>
            {
                e.Text = "réécrit";

                return new EventContext { CorrelationId = "test" };
            }
        );

        await SendAsync(pipeline, "general", "hi");

        Delivered.Should().Equal("réécrit");
    }

    [Fact]
    public async Task ATextRewrittenToNothing_IsEmpty()
    {
        ChatPipeline pipeline = Pipeline(
            [Echo("general")],
            sending: e =>
            {
                e.Text = "  ";

                return new EventContext { CorrelationId = "test" };
            }
        );

        (await SendAsync(pipeline, "general", "hi")).Should().Be("empty");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSentEvent_IsPublishedAfterTheDelivery()
    {
        ChatPipeline pipeline = Pipeline([Echo("general"), Refusing("guild", "not_in_guild")]);

        await SendAsync(pipeline, "general", "hi");
        await SendAsync(pipeline, "guild", "hi");

        _published
            .Should()
            .Equal(new ChatSentEvent(new PlayerId(1), "general", string.Empty, "hi"));
    }

    [Fact]
    public async Task AFailedDelivery_IsNotARefusal()
    {
        _routing.Fails = true;

        (await SendAsync(Pipeline([Echo("general")]), "general", "hi")).Should().BeNull();
        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemLine_GoesThroughTheSystemChannel()
    {
        await Pipeline([Echo("system", playersCanSend: false)])
            .SendSystemAsync("quest.done", _routing, CancellationToken.None);

        ChatLine line = _routing.Deliveries.Single().Line;
        (line.Channel, line.SenderId, line.SenderName, line.Text)
            .Should()
            .Be(("system", new PlayerId(0), string.Empty, "quest.done"));
    }

    private IEnumerable<string> Delivered => _routing.Deliveries.Select(d => d.Line.Text);

    private Task<string?> SendAsync(
        ChatPipeline pipeline,
        string channel,
        string text,
        DateTimeOffset? now = null
    ) =>
        pipeline.SendAsync(
            new ChatSend(new PlayerId(1), "Ann", channel, string.Empty, text, now ?? T0),
            _state,
            _routing,
            CancellationToken.None
        );

    private ChatPipeline Pipeline(
        IChatChannel[] channels,
        IChatRule[]? rules = null,
        IChatLinkKind[]? kinds = null,
        Func<ChatSendingEvent, EventContext>? sending = null
    ) =>
        new(
            channels,
            rules ?? ShippedRules(),
            kinds ?? [Kind("item", AmberShard)],
            Options.Create(new ChatConfig()),
            FakeProxy.Create<ICancellableEventPublisher>(call =>
                Task.FromResult(
                    sending?.Invoke((ChatSendingEvent)call.Args![0]!)
                        ?? new EventContext { CorrelationId = "test" }
                )
            ),
            FakeProxy.Create<IEventPublisher>(call =>
            {
                _published.Add((IEvent)call.Args![0]!);

                return Task.CompletedTask;
            }),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger<ChatPipeline>.Instance
        );

    private static IChatRule[] ShippedRules()
    {
        IOptions<ChatConfig> config = Options.Create(new ChatConfig());

        return
        [
            new ChatLengthRule(config),
            new ChatRateRule(config, NullLogger<ChatRateRule>.Instance),
        ];
    }

    /// <summary>A channel that publishes every line on "echo/0".</summary>
    private static IChatChannel Echo(string key, bool playersCanSend = true) =>
        FakeProxy.Create<IChatChannel>(call =>
            call.Method.Name switch
            {
                "get_Key" => key,
                "get_PlayersCanSend" => playersCanSend,
                nameof(IChatChannel.RouteAsync) => EchoAsync(
                    (ChatLine)call.Args![0]!,
                    (IChatRouting)call.Args[2]!
                ),
                _ => null,
            }
        );

    private static async Task<string?> EchoAsync(ChatLine line, IChatRouting routing)
    {
        await routing.PublishAsync("echo", 0, line);

        return null;
    }

    private static IChatChannel Refusing(string key, string code) =>
        FakeProxy.Create<IChatChannel>(call =>
            call.Method.Name switch
            {
                "get_Key" => key,
                "get_PlayersCanSend" => true,
                nameof(IChatChannel.RouteAsync) => Task.FromResult<string?>(code),
                _ => null,
            }
        );

    private static IChatRule Rule(int priority, Func<string?> code) =>
        FakeProxy.Create<IChatRule>(call =>
            call.Method.Name switch
            {
                "get_Priority" => priority,
                nameof(IChatRule.CheckAsync) => new ValueTask<string?>(code()),
                _ => null,
            }
        );

    private static IChatLinkKind Kind(string prefix, params string[] valid) =>
        FakeProxy.Create<IChatLinkKind>(call =>
            call.Method.Name switch
            {
                "get_Prefix" => prefix,
                nameof(IChatLinkKind.ExistsAsync) => new ValueTask<bool>(
                    valid.Contains((string)call.Args![0]!)
                ),
                _ => null,
            }
        );
}
