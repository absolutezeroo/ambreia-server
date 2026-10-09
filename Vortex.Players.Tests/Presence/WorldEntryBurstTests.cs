using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Players.Configuration;
using Vortex.Players.Grains;
using Vortex.Players.Tests.Support;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Players.Tests.Presence;

/// <summary>
/// The sign-in sends exactly what the client needs to be in the world: who it is, what it may do,
/// and where to go. Anything more is a system the cut removed coming back through the login. Where
/// it lands: a pending editor test entry, else the room held in grace, else the character's last
/// zone if still published, else the start zone.
/// </summary>
public sealed class WorldEntryBurstTests
{
    private const int PLAYER_ID = 501;

    private readonly List<string> _calls = [];
    private int _startRoomId = 42;
    private int? _testRoomId;
    private int? _lastRoomId;
    private bool _lastRoomPublished = true;
    private bool _characterFails;
    private bool _pushFails;

    [Fact]
    public async Task TheBurst_IsOnlyTheCoreComposers()
    {
        IComposer[] burst = await BurstAsync();

        burst
            .Select(c => c.GetType())
            .Should()
            .Equal(
                typeof(AuthenticationOKMessage),
                typeof(UserRightsMessage),
                typeof(RoomForwardMessageComposer)
            );
        Forward(burst).Should().Be(new RoomId(42));
    }

    [Fact]
    public async Task ATester_IsForwardedToTheZoneUnderTest()
    {
        _testRoomId = 77;

        Forward(await BurstAsync()).Should().Be(new RoomId(77));
    }

    [Fact]
    public async Task WithoutAStartZone_ThePlayerStillSignsIn()
    {
        _startRoomId = 0;

        (await BurstAsync())
            .Select(c => c.GetType())
            .Should()
            .Equal(typeof(AuthenticationOKMessage), typeof(UserRightsMessage));
    }

    [Fact]
    public async Task TheLastZone_IsWhereTheCharacterSignsIn()
    {
        _lastRoomId = 12;

        Forward(await BurstAsync()).Should().Be(new RoomId(12));
    }

    [Fact]
    public async Task ALastZoneNoLongerPublished_FallsBackToTheStart()
    {
        _lastRoomId = 12;
        _lastRoomPublished = false;

        Forward(await BurstAsync()).Should().Be(new RoomId(42));
    }

    [Fact]
    public async Task ATestEntry_StillComesFirst()
    {
        _testRoomId = 30;
        _lastRoomId = 12;

        Forward(await BurstAsync()).Should().Be(new RoomId(30));
    }

    [Fact]
    public async Task ACharacterGrainFailure_StillSignsInAtTheStart()
    {
        _lastRoomId = 12;
        _characterFails = true;

        Forward(await BurstAsync()).Should().Be(new RoomId(42));
    }

    /// <summary>The avatar still standing in its room during the grace period is the one resumed.</summary>
    [Fact]
    public async Task ARoomInGrace_ComesBeforeTheLastZone()
    {
        _lastRoomId = 12;
        PlayerPresenceGrain grain = Build();
        grain._resumableRoom = 50;

        Forward(await grain.BuildWorldEntryAsync(CancellationToken.None))
            .Should()
            .Be(new RoomId(50));
    }

    /// <summary>A tester who dropped in the zone under test lands on the cell asked for: kept as a
    /// resume, the room kept the avatar where it stood and the test cell was never used.</summary>
    [Fact]
    public async Task ATestEntry_DropsTheResumeOfTheRoomInGrace()
    {
        _testRoomId = 50;
        PlayerPresenceGrain grain = Build();
        grain._resumableRoom = 50;

        await grain.BuildWorldEntryAsync(CancellationToken.None);

        (await grain.GetResumableRoomAsync()).Should().BeNull();
    }

    /// <summary>Back within the grace period there is no room entry to push it: the sign-in does.</summary>
    [Fact]
    public async Task EnteringTheWorld_PushesTheCharacterState()
    {
        await Build().EnterWorldAsync(CancellationToken.None);

        _calls.Should().Equal("push");
    }

    /// <summary>No zone to land in, no HUD: the player stays on the hotel view.</summary>
    [Fact]
    public async Task WithoutAStartZone_NoStateIsPushed()
    {
        _startRoomId = 0;

        await Build().EnterWorldAsync(CancellationToken.None);

        _calls.Should().BeEmpty();
    }

    /// <summary>The HUD is a view: a push that fails is logged, the sign-in has already happened.</summary>
    [Fact]
    public async Task AFailingPush_DoesNotFailTheSignIn()
    {
        _pushFails = true;

        Func<Task> enter = () => Build().EnterWorldAsync(CancellationToken.None);

        await enter.Should().NotThrowAsync();
    }

    private static RoomId Forward(IComposer[] burst) =>
        burst.OfType<RoomForwardMessageComposer>().Single().RoomId;

    private Task<IComposer[]> BurstAsync() => Build().BuildWorldEntryAsync(CancellationToken.None);

    private PlayerPresenceGrain Build() =>
        GrainActivationContext.CreateWithIntegerKey<PlayerPresenceGrain>(
            PLAYER_ID,
            Grains(),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLogger<PlayerPresenceGrain>.Instance,
            FakeProxy.Create<IVortexMetrics>(_ => null),
            Options.Create(new PlayerPresenceConfig()),
            FakeProxy.Create<IPermissionService>(call =>
                call.Method.Name == nameof(IPermissionService.ResolveForPlayerAsync)
                    ? Task.FromResult(PermissionSet.Empty)
                    : null
            ),
            FakeProxy.Create<IZoneRoomSource>(call =>
                call.Method.Name == nameof(IZoneRoomSource.GetPublishedAsync)
                    ? Task.FromResult(
                        _lastRoomPublished && ((RoomId)call.Args![0]!).Value == _lastRoomId
                            ? ZoneSamples.Minimal()
                            : null
                    )
                    : null
            ),
            TestChat.Pipeline()
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            Type grain = call.Method.GetGenericArguments()[0];

            return grain == typeof(IServerConfigGrain)
                    ? FakeProxy.Create<IServerConfigGrain>(inner =>
                        inner.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                        && (string)inner.Args![0]! == PlayerPresenceGrain.StartRoomKey
                            ? Task.FromResult(_startRoomId)
                            : null
                    )
                : grain == typeof(IZoneTestGrain)
                    ? FakeProxy.Create<IZoneTestGrain>(_ =>
                        Task.FromResult(_testRoomId is { } room ? (RoomId?)new RoomId(room) : null)
                    )
                : grain == typeof(IPlayerCharacterGrain)
                    ? FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                        inner.Method.Name switch
                        {
                            nameof(IPlayerCharacterGrain.PushStateAsync) => PushedAsync(),
                            nameof(IPlayerCharacterGrain.GetLastRoomAsync) => _characterFails
                                ? Task.FromException<RoomId?>(
                                    new InvalidOperationException("player_characters is missing")
                                )
                                : Task.FromResult(
                                    _lastRoomId is { } room ? (RoomId?)new RoomId(room) : null
                                ),
                            _ => null,
                        }
                    )
                : null;
        });

    private Task PushedAsync()
    {
        if (_pushFails)
        {
            return Task.FromException(new InvalidOperationException("character grain down"));
        }

        _calls.Add("push");

        return Task.CompletedTask;
    }
}
