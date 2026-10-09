using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;
using Vortex.Players.Chat.Channels;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Rpg.Chat;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// The presence routes its player's chat (spec F3c §4): a refusal back to the player alone, a
/// whisper to the recipient's presence and a copy to the sender, a system line to the player.
/// </summary>
public sealed class PresenceChatTests
{
    private const int ANN = 42;
    private const int BOB = 43;

    private readonly List<IComposer> _ann = [];
    private readonly List<IComposer> _bob = [];
    private readonly List<string> _directoryCalls = [];
    private bool _bobOnline = true;

    [Fact]
    public async Task WithoutASocket_NothingIsSent()
    {
        PlayerPresenceGrain grain = Build();

        await grain.SendChatAsync("guild", string.Empty, "x", CancellationToken.None);

        _directoryCalls.Should().BeEmpty();
        _ann.Should().BeEmpty();
    }

    [Fact]
    public async Task ARefusal_ComesBackToTheSenderAlone()
    {
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.SendChatAsync("guild", string.Empty, "x", CancellationToken.None);

        (await ReceivedAsync(1))
            .Should()
            .Equal(new RpgChatResultMessageComposer { Channel = "guild", Code = "not_in_guild" });
        _bob.Should().BeEmpty();
    }

    [Fact]
    public async Task AWhisper_ReachesTheRecipient_AndTheSendersCopy()
    {
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.SendChatAsync("whisper", "bob", "salut", CancellationToken.None);

        RpgChatMessageComposer toBob = _bob.Cast<RpgChatMessageComposer>().Single();
        (toBob.Channel, toBob.SenderId, toBob.SenderName, toBob.Text, toBob.Target)
            .Should()
            .Be(("whisper", ANN, "Ann", "salut", "Ann"));
        toBob.SentAt.Should().BeCloseTo((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 60);

        RpgChatMessageComposer copy = (await ReceivedAsync(1))
            .Cast<RpgChatMessageComposer>()
            .Single();
        copy.Should().Be(toBob with { Target = "Bob" });
    }

    [Fact]
    public async Task AWhisper_ToAnOfflinePlayer_IsRefused()
    {
        _bobOnline = false;
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.SendChatAsync("whisper", "Bob", "salut", CancellationToken.None);

        (await ReceivedAsync(1))
            .Should()
            .Equal(
                new RpgChatResultMessageComposer { Channel = "whisper", Code = "player_offline" }
            );
        _bob.Should().BeEmpty();
    }

    [Fact]
    public async Task ASystemLine_ReachesThePlayer()
    {
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.SendSystemChatAsync("quest.done", CancellationToken.None);

        RpgChatMessageComposer line = (await ReceivedAsync(1))
            .Cast<RpgChatMessageComposer>()
            .Single();
        (line.Channel, line.SenderId, line.SenderName, line.Text, line.Target)
            .Should()
            .Be(("system", 0, string.Empty, "quest.done", string.Empty));
    }

    /// <summary>Two players whispering each other at once each ask the other's presence while in
    /// their own turn: without interleaving, both wait out the call timeout.</summary>
    [Fact]
    public void IsOnline_IsInterleaved()
    {
        typeof(IPlayerPresenceGrain)
            .GetMethod(nameof(IPlayerPresenceGrain.IsOnlineAsync))!
            .GetCustomAttribute<AlwaysInterleaveAttribute>()
            .Should()
            .NotBeNull();
    }

    /// <summary>A system line comes from a grain the presence may be waiting on (the character
    /// during the world entry, a room during a room entry): non-interleaved, both would wait out
    /// the call timeout.</summary>
    [Fact]
    public void SendSystemChat_IsInterleaved()
    {
        typeof(IPlayerPresenceGrain)
            .GetMethod(nameof(IPlayerPresenceGrain.SendSystemChatAsync))!
            .GetCustomAttribute<AlwaysInterleaveAttribute>()
            .Should()
            .NotBeNull();
    }

    /// <summary>The directory is one grain for the whole server: the sender's own name is not
    /// asked of it for every line.</summary>
    [Fact]
    public async Task TheSendersName_IsReadOnce()
    {
        PlayerPresenceGrain grain = await SignedInAsync();

        await grain.SendChatAsync("guild", string.Empty, "un", CancellationToken.None);
        await grain.SendChatAsync("guild", string.Empty, "deux", CancellationToken.None);

        _directoryCalls
            .Count(c => c == nameof(IPlayerDirectoryGrain.GetPlayerNameAsync))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task ARename_ReachesTheNextLine()
    {
        PlayerPresenceGrain grain = await SignedInAsync();
        await grain.SendChatAsync("guild", string.Empty, "avant", CancellationToken.None);

        await grain.OnPlayerUpdatedAsync(
            new PlayerSummarySnapshot
            {
                PlayerId = new PlayerId(ANN),
                Name = "Annie",
                Motto = string.Empty,
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Female,
                AchievementScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            CancellationToken.None
        );
        await grain.SendChatAsync("whisper", "Bob", "salut", CancellationToken.None);

        _bob.Cast<RpgChatMessageComposer>().Single().SenderName.Should().Be("Annie");
    }

    /// <summary>The outgoing queue drains off the caller's turn: wait for it, two seconds at most.</summary>
    private async Task<List<IComposer>> ReceivedAsync(int count)
    {
        for (int i = 0; i < 200 && _ann.Count < count; i++)
        {
            await Task.Delay(10);
        }

        return _ann;
    }

    private async Task<PlayerPresenceGrain> SignedInAsync()
    {
        PlayerPresenceGrain grain = Build();
        long generation = await grain.BeginSessionAsync(CancellationToken.None);

        await grain.RegisterSessionObserverAsync(
            FakeProxy.Create<ISessionContextObserver>(call =>
            {
                lock (_ann)
                {
                    _ann.Add((IComposer)call.Args![0]!);
                }

                return Task.CompletedTask;
            }),
            generation
        );

        return grain;
    }

    private PlayerPresenceGrain Build() =>
        GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            ANN,
            Grains(),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<PlayerPresenceGrain>.Instance,
            FakeProxy.Create<IVortexMetrics>(_ => null),
            Options.Create(new PlayerPresenceConfig()),
            FakeProxy.Create<IPermissionService>(_ => null),
            FakeProxy.Create<IZoneRoomSource>(_ => null),
            TestChat.Pipeline(
                new WhisperChatChannel(),
                new SystemChatChannel(),
                new InactiveChatChannel("guild", "not_in_guild")
            )
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            Type grain = call.Method.GetGenericArguments()[0];

            if (grain == typeof(IPlayerDirectoryGrain))
            {
                return FakeProxy.Create<IPlayerDirectoryGrain>(inner =>
                {
                    _directoryCalls.Add(inner.Method.Name);

                    return inner.Method.Name switch
                    {
                        nameof(IPlayerDirectoryGrain.GetPlayerNameAsync) => Task.FromResult(
                            ((PlayerId)inner.Args![0]!).Value == BOB ? "Bob" : "Ann"
                        ),
                        nameof(IPlayerDirectoryGrain.GetPlayerIdByNameAsync) => Task.FromResult(
                            string.Equals(
                                (string)inner.Args![0]!,
                                "bob",
                                StringComparison.OrdinalIgnoreCase
                            )
                                ? (PlayerId?)new PlayerId(BOB)
                                : null
                        ),
                        _ => null,
                    };
                });
            }

            return grain == typeof(IPlayerPresenceGrain) && (long)call.Args![0]! == BOB
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                    inner.Method.Name switch
                    {
                        nameof(IPlayerPresenceGrain.IsOnlineAsync) => Task.FromResult(_bobOnline),
                        nameof(IPlayerPresenceGrain.SendComposerAsync) => RecordAsync(inner),
                        _ => null,
                    }
                )
                : null;
        });

    private Task RecordAsync(ProxyCall call)
    {
        _bob.Add((IComposer)call.Args![0]!);

        return Task.CompletedTask;
    }
}
