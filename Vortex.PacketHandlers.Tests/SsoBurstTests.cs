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
using Vortex.Primitives.Server.Grains;
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

    private readonly List<object> _sent = [];

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

    private async Task LoginAsync(int startRoomId, int? testRoomId = null)
    {
        SSOTicketMessageHandler handler = new(
            FakeProxy.Create<IAuthenticationService>(call =>
                call.Method.Name == nameof(IAuthenticationService.GetPlayerIdFromTicketAsync)
                    ? Task.FromResult(PLAYER_ID)
                    : null
            ),
            FakeProxy.Create<ISessionGateway>(_ => null),
            FakeProxy.Create<IGrainFactory>(call =>
                call.Method.Name != nameof(IGrainFactory.GetGrain) ? null
                : call.Method.GetGenericArguments()[0] == typeof(IServerConfigGrain)
                    ? FakeProxy.Create<IServerConfigGrain>(inner =>
                        inner.Method.Name == nameof(IServerConfigGrain.GetIntAsync)
                        && (string)inner.Args![0]! == SSOTicketMessageHandler.START_ROOM_KEY
                            ? Task.FromResult(startRoomId)
                            : null
                    )
                : call.Method.GetGenericArguments()[0] == typeof(IZoneTestGrain)
                    ? FakeProxy.Create<IZoneTestGrain>(_ =>
                        Task.FromResult(testRoomId is { } room ? (RoomId?)new RoomId(room) : null)
                    )
                : call.Method.GetGenericArguments()[0] == typeof(IPlayerPresenceGrain)
                    ? FakeProxy.Create<IPlayerPresenceGrain>(inner =>
                    {
                        if (inner.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync))
                        {
                            _sent.AddRange(((object[])inner.Args![0]!).Cast<object>());
                        }

                        return null;
                    })
                : null
            ),
            FakeProxy.Create<IPermissionService>(call =>
                call.Method.Name == nameof(IPermissionService.ResolveForPlayerAsync)
                    ? Task.FromResult(PermissionSet.Empty)
                    : null
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
