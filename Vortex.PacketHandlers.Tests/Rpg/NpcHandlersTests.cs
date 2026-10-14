using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Quests;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec M2a §3.1: talking and choosing are calls to the player's room; nothing without a player in a room.</summary>
public sealed class NpcHandlersTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task ATalk_GoesToThePlayersRoom()
    {
        await TalkAsync(7, 12, 42);

        _calls.Should().Equal("room 12: player 7 TalkToNpcAsync 42");
    }

    [Fact]
    public async Task AChoice_GoesToThePlayersRoom()
    {
        await ChooseAsync(7, 12, 1);

        _calls.Should().Equal("room 12: player 7 ChooseDialogueAsync 1");
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(7, 0)]
    public async Task WithoutAPlayerInARoom_NothingIsCalled(int playerId, int roomId)
    {
        await TalkAsync(playerId, roomId, 42);
        await ChooseAsync(playerId, roomId, 0);

        _calls.Should().BeEmpty();
    }

    private static MessageContext Context(int playerId, int roomId) =>
        new(
            FakeProxy.Create<ISessionContext>(_ => null),
            new PlayerId(playerId),
            new RoomId(roomId)
        );

    private Task TalkAsync(int playerId, int roomId, int objectId) =>
        new RpgNpcTalkMessageHandler(Grains())
            .HandleAsync(
                new RpgNpcTalkMessage { ObjectId = objectId },
                Context(playerId, roomId),
                CancellationToken.None
            )
            .AsTask();

    private Task ChooseAsync(int playerId, int roomId, int choice) =>
        new RpgDialogueChooseMessageHandler(Grains())
            .HandleAsync(
                new RpgDialogueChooseMessage { Choice = choice },
                Context(playerId, roomId),
                CancellationToken.None
            )
            .AsTask();

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IRoomNpcs>(inner =>
                {
                    ActionContext ctx = (ActionContext)inner.Args![0]!;
                    _calls.Add(
                        $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} {inner.Method.Name} {inner.Args[1]}"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
