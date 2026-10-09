using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Interaction;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Interaction;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec F3b §7.1: a target choice is one call to the player's room; nothing without a
/// player in a room.</summary>
public sealed class TargetSelectHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task AChoice_GoesToThePlayersRoom()
    {
        await HandleAsync(playerId: 7, roomId: 12, objectId: 202);

        _calls.Should().Equal("room 12: player 7 targets 202");
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(7, 0)]
    public async Task WithoutAPlayerInARoom_NothingIsCalled(int playerId, int roomId)
    {
        await HandleAsync(playerId, roomId, objectId: 202);

        _calls.Should().BeEmpty();
    }

    private Task HandleAsync(int playerId, int roomId, int objectId) =>
        new RpgTargetSelectMessageHandler(Grains())
            .HandleAsync(
                new RpgTargetSelectMessage { ObjectId = objectId },
                new MessageContext(
                    FakeProxy.Create<ISessionContext>(_ => null),
                    new PlayerId(playerId),
                    new RoomId(roomId)
                ),
                CancellationToken.None
            )
            .AsTask();

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IRoomTargets>(inner =>
                {
                    ActionContext ctx = (ActionContext)inner.Args![0]!;
                    _calls.Add(
                        $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} targets {inner.Args[1]}"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
