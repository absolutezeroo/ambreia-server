using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Emote;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Emote;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>An RP action is one call to the player's room; nothing without a player in a room.</summary>
public sealed class EmoteHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task AnAction_GoesToThePlayersRoom()
    {
        await HandleAsync(playerId: 7, roomId: 12);

        _calls.Should().Equal("room 12: player 7 wave");
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(7, 0)]
    public async Task WithoutAPlayerInARoom_NothingIsCalled(int playerId, int roomId)
    {
        await HandleAsync(playerId, roomId);

        _calls.Should().BeEmpty();
    }

    private Task HandleAsync(int playerId, int roomId) =>
        new RpgEmoteMessageHandler(Grains())
            .HandleAsync(
                new RpgEmoteMessage { Key = "wave" },
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
                ? FakeProxy.Create<IRoomEmotes>(inner =>
                {
                    ActionContext ctx = (ActionContext)inner.Args![0]!;
                    _calls.Add(
                        $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} {inner.Args[1]}"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
