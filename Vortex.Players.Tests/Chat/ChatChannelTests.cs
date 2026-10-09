using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Players.Chat.Channels;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Chat;
using Vortex.Primitives.Players;
using Xunit;

namespace Vortex.Players.Tests.Chat;

/// <summary>The channels F3c ships (spec F3c §4.4): zone, world, whisper, system, and group and guild
/// ready but idle.</summary>
public sealed class ChatChannelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingChatRouting _routing = new();

    [Fact]
    public async Task General_IsPublishedOnTheSendersRoomStream()
    {
        (await RouteAsync(new ZoneChatChannel(), "general")).Should().BeNull();
        _routing.Deliveries.Select(d => d.To).Should().Equal("RoomStream/7");
    }

    [Fact]
    public async Task General_WithoutARoom_IsNotInZone()
    {
        _routing.Room = null;

        (await RouteAsync(new ZoneChatChannel(), "general")).Should().Be("not_in_zone");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task World_IsPublishedOnTheWorldChatStream()
    {
        (await RouteAsync(new WorldChatChannel(), "world")).Should().BeNull();
        _routing.Deliveries.Select(d => d.To).Should().Equal("world-chat/0");
    }

    [Fact]
    public async Task AWhisper_ReachesTheRecipient_AndCopiesTheSender()
    {
        _routing.Known.Add(new ChatPlayer(new PlayerId(2), "Bob"));
        _routing.Online.Add(new PlayerId(2));

        (await RouteAsync(new WhisperChatChannel(), "whisper", "Bob")).Should().BeNull();
        _routing
            .Deliveries.Select(d => (d.To, d.Line.Target))
            .Should()
            .BeEquivalentTo([("player 2", "Ann"), ("player 1", "Bob")]);
    }

    [Fact]
    public async Task AWhisper_FindsTheNameWhateverItsCase()
    {
        _routing.Known.Add(new ChatPlayer(new PlayerId(2), "Bob"));
        _routing.Online.Add(new PlayerId(2));

        (await RouteAsync(new WhisperChatChannel(), "whisper", "  bOB ")).Should().BeNull();
        _routing.Deliveries.Single(d => d.To == "player 1").Line.Target.Should().Be("Bob");
        _routing.Deliveries.Should().Contain(d => d.To == "player 2");
    }

    [Theory]
    [InlineData("Zed")]
    [InlineData("Ann")]
    [InlineData("")]
    public async Task AWhisper_ToAnUnknownNameOneselfOrNobody_IsNotFound(string target)
    {
        _routing.Known.Add(new ChatPlayer(new PlayerId(1), "Ann"));
        _routing.Online.Add(new PlayerId(1));

        (await RouteAsync(new WhisperChatChannel(), "whisper", target))
            .Should()
            .Be("player_not_found");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task AWhisper_ToAnOfflinePlayer_IsOffline()
    {
        _routing.Known.Add(new ChatPlayer(new PlayerId(2), "Bob"));

        (await RouteAsync(new WhisperChatChannel(), "whisper", "Bob"))
            .Should()
            .Be("player_offline");
        _routing.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemLine_GoesToThePlayerAlone()
    {
        SystemChatChannel system = new();

        system.PlayersCanSend.Should().BeFalse();
        (await RouteAsync(system, "system")).Should().BeNull();
        _routing.Deliveries.Select(d => d.To).Should().Equal("player 1");
    }

    [Theory]
    [InlineData("group", "not_in_group")]
    [InlineData("guild", "not_in_guild")]
    public async Task GroupAndGuild_AreReadyButIdle(string key, string code)
    {
        InactiveChatChannel channel = new(key, code);

        channel.Key.Should().Be(key);
        (await RouteAsync(channel, key)).Should().Be(code);
        _routing.Deliveries.Should().BeEmpty();
    }

    private Task<string?> RouteAsync(IChatChannel channel, string key, string target = "") =>
        channel.RouteAsync(
            new ChatLine(key, new PlayerId(1), "Ann", "hi", T0),
            target,
            _routing,
            CancellationToken.None
        );
}
