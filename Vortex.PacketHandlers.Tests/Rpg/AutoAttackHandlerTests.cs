using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Combat;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Combat;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>M1d §2.2 : l'attaque auto est un appel à la salle du joueur ; rien sans joueur en salle.</summary>
public sealed class AutoAttackHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task AnAutoAttack_GoesToThePlayersRoom()
    {
        await HandleAsync(7, 12, true);

        _calls.Should().Equal("room 12: player 7 auto-attack True");
    }

    [Fact]
    public async Task ADeathChoice_GoesToThePlayersRoom()
    {
        await new RpgDeathChoiceMessageHandler(Grains()).HandleAsync(
            new RpgDeathChoiceMessage { Choice = RpgDeathChoiceMessage.Release },
            Context(7, 12),
            CancellationToken.None
        );

        _calls.Should().Equal("room 12: player 7 chooses release");
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(7, 0)]
    public async Task WithoutAPlayerInARoom_NothingIsCalled(int playerId, int roomId)
    {
        await HandleAsync(playerId, roomId, false);
        await new RpgDeathChoiceMessageHandler(Grains()).HandleAsync(
            new RpgDeathChoiceMessage { Choice = RpgDeathChoiceMessage.Wait },
            Context(playerId, roomId),
            CancellationToken.None
        );

        _calls.Should().BeEmpty();
    }

    private static MessageContext Context(int playerId, int roomId) =>
        new(
            FakeProxy.Create<ISessionContext>(_ => null),
            new PlayerId(playerId),
            new RoomId(roomId)
        );

    private Task HandleAsync(int playerId, int roomId, bool active) =>
        new RpgAutoAttackMessageHandler(Grains())
            .HandleAsync(
                new RpgAutoAttackMessage { Active = active },
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
                ? FakeProxy.Create<IRoomCombat>(inner =>
                {
                    ActionContext ctx = (ActionContext)inner.Args![0]!;
                    _calls.Add(
                        inner.Method.Name == nameof(IRoomCombat.SetAutoAttackAsync)
                            ? $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} auto-attack {inner.Args[1]}"
                            : $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} chooses {inner.Args[1]}"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
