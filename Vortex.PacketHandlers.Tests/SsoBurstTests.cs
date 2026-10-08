using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Handshake;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Server.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Grains;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Room.Session;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// The core sign-in sends exactly what the client needs to be in the world: who it is, what it may
/// do, and where to go. Anything more is a system the cut removed coming back through the login.
/// </summary>
public sealed class SsoBurstTests
{
    private const int PLAYER_ID = 501;

    private const int ACCOUNT_ID = 3;

    private readonly List<object> _sent = [];
    private readonly List<string> _calls = [];

    /// <summary>An account ticket opens the character screen, not the world (spec F3a §4).</summary>
    [Fact]
    public async Task AnAccountTicket_OpensTheAccountPhase()
    {
        await LoginAsync(startRoomId: 42, owner: new TicketOwner(0, ACCOUNT_ID));

        _calls.Should().Equal($"account:{ACCOUNT_ID}", "open");
        _sent.Should().BeEmpty("no AuthenticationOK before a character is chosen");
    }

    /// <summary>Back within the grace period there is no room entry to push it: the sign-in does.</summary>
    [Fact]
    public async Task SigningIn_PushesTheCharacterState()
    {
        await LoginAsync(startRoomId: 42);

        _calls.Should().Equal("push");
    }

    [Fact]
    public async Task SsoBurst_SendsOnlyTheCoreComposers()
    {
        await LoginAsync(startRoomId: 42);

        _sent
            .Select(c => c.GetType())
            .Should()
            .Equal(
                typeof(AuthenticationOKMessage),
                typeof(UserRightsMessage),
                typeof(RoomForwardMessageComposer)
            );
        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(42));
    }

    [Fact]
    public async Task SsoBurst_ForATester_ForwardsToTheZoneUnderTest()
    {
        await LoginAsync(startRoomId: 42, testRoomId: 77);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(77));
    }

    [Fact]
    public async Task SsoBurst_WithoutAStartZone_StillLogsIn()
    {
        await LoginAsync(startRoomId: 0);

        _sent
            .Select(c => c.GetType())
            .Should()
            .Equal(typeof(AuthenticationOKMessage), typeof(UserRightsMessage));
    }

    [Fact]
    public async Task TheLastZone_IsWhereTheCharacterSignsIn()
    {
        await LoginAsync(startRoomId: 42, lastRoomId: 12);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(12));
    }

    [Fact]
    public async Task ALastZoneNoLongerPublished_FallsBackToTheStart()
    {
        await LoginAsync(startRoomId: 42, lastRoomId: 12, lastRoomPublished: false);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(42));
    }

    [Fact]
    public async Task ATestEntry_StillComesFirst()
    {
        await LoginAsync(startRoomId: 42, testRoomId: 30, lastRoomId: 12);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(30));
    }

    /// <summary>The avatar still standing in its room during the grace period is the one resumed.</summary>
    [Fact]
    public async Task ACharacterGrainFailure_StillSignsInAtTheStart()
    {
        await LoginAsync(startRoomId: 42, lastRoomId: 12, characterFails: true);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(42));
    }

    [Fact]
    public async Task ARoomInGrace_ComesBeforeTheLastZone()
    {
        await LoginAsync(startRoomId: 42, lastRoomId: 12, graceRoomId: 50);

        _sent.OfType<RoomForwardMessageComposer>().Single().RoomId.Should().Be(new RoomId(50));
    }

    private Task PushedAsync()
    {
        _calls.Add("push");

        return Task.CompletedTask;
    }

    private async Task LoginAsync(
        int startRoomId,
        int? testRoomId = null,
        int? lastRoomId = null,
        bool lastRoomPublished = true,
        int? graceRoomId = null,
        bool characterFails = false,
        TicketOwner? owner = null
    )
    {
        ISessionGateway gateway = FakeProxy.Create<ISessionGateway>(call =>
        {
            if (call.Method.Name == nameof(ISessionGateway.AddSessionToAccountAsync))
            {
                _calls.Add($"account:{call.Args![1]}");
            }

            return null;
        });
        IGrainFactory grains = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name != nameof(IGrainFactory.GetGrain) ? null
            : call.Method.GetGenericArguments()[0] == typeof(IAccountCharactersGrain)
                ? FakeProxy.Create<IAccountCharactersGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IAccountCharactersGrain.OpenAsync))
                    {
                        _calls.Add("open");
                    }

                    return null;
                })
            : call.Method.GetGenericArguments()[0] == typeof(IServerConfigGrain)
                ? FakeProxy.Create<IServerConfigGrain>(inner =>
                    inner.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                    && (string)inner.Args![0]! == WorldEntry.START_ROOM_KEY
                        ? Task.FromResult(startRoomId)
                        : null
                )
            : call.Method.GetGenericArguments()[0] == typeof(IZoneTestGrain)
                ? FakeProxy.Create<IZoneTestGrain>(_ =>
                    Task.FromResult(testRoomId is { } room ? (RoomId?)new RoomId(room) : null)
                )
            : call.Method.GetGenericArguments()[0] == typeof(IPlayerCharacterGrain)
                ? FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                    inner.Method.Name == nameof(IPlayerCharacterGrain.PushStateAsync)
                        ? PushedAsync()
                    : inner.Method.Name != nameof(IPlayerCharacterGrain.GetLastRoomAsync) ? null
                    : characterFails
                        ? Task.FromException<RoomId?>(
                            new InvalidOperationException("player_characters is missing")
                        )
                    : Task.FromResult(lastRoomId is { } room ? (RoomId?)new RoomId(room) : null)
                )
            : call.Method.GetGenericArguments()[0] == typeof(IPlayerPresenceGrain)
                ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                {
                    if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                    {
                        _sent.AddRange(((object[])inner.Args![0]!).Cast<object>());
                    }

                    return inner.Method.Name == nameof(IPlayerPresenceGrain.GetResumableRoomAsync)
                        ? Task.FromResult(
                            graceRoomId is { } room ? (RoomId?)new RoomId(room) : null
                        )
                        : null;
                })
            : null
        );
        SSOTicketMessageHandler handler = new(
            FakeProxy.Create<IAuthenticationService>(call =>
                call.Method.Name == nameof(IAuthenticationService.ResolveTicketAsync)
                    ? Task.FromResult(owner ?? new TicketOwner(PLAYER_ID, 0))
                    : null
            ),
            gateway,
            grains,
            new WorldEntry(
                gateway,
                grains,
                FakeProxy.Create<IPermissionService>(call =>
                    call.Method.Name == nameof(IPermissionService.ResolveForPlayerAsync)
                        ? Task.FromResult(PermissionSet.Empty)
                        : null
                ),
                FakeProxy.Create<IZoneRoomSource>(call =>
                    call.Method.Name == nameof(IZoneRoomSource.GetPublishedAsync)
                        ? Task.FromResult(
                            lastRoomPublished && ((RoomId)call.Args![0]!).Value == lastRoomId
                                ? ZoneSamples.Minimal()
                                : null
                        )
                        : null
                ),
                NullLogger<WorldEntry>.Instance
            ),
            NullLogger<SSOTicketMessageHandler>.Instance
        );

        await handler.HandleAsync(
            new SSOTicketMessage { SSO = "ticket" },
            new MessageContext(FakeProxy.Create<ISessionContext>(_ => null), default, default),
            CancellationToken.None
        );
    }
}
