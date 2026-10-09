using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Skills;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec M1b §10.1: a cast and a cancel are one call each to the player's room; nothing
/// without a player in a room.</summary>
public sealed class SkillCastHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task ACast_GoesToThePlayersRoom()
    {
        await CastAsync(playerId: 7, roomId: 12);

        _calls.Should().Equal("room 12: player 7 casts skill.mage.firebolt at 33 (-1, -1)");
    }

    [Fact]
    public async Task ACancel_GoesToThePlayersRoom()
    {
        await new RpgCastCancelMessageHandler(Grains()).HandleAsync(
            new RpgCastCancelMessage(),
            Context(7, 12),
            CancellationToken.None
        );

        _calls.Should().Equal("room 12: player 7 cancels");
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(7, 0)]
    public async Task WithoutAPlayerInARoom_NothingIsCalled(int playerId, int roomId)
    {
        await CastAsync(playerId, roomId);
        await new RpgCastCancelMessageHandler(Grains()).HandleAsync(
            new RpgCastCancelMessage(),
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

    private Task CastAsync(int playerId, int roomId) =>
        new RpgSkillCastMessageHandler(Grains())
            .HandleAsync(
                new RpgSkillCastMessage
                {
                    SkillId = "skill.mage.firebolt",
                    TargetObjectId = 33,
                    X = -1,
                    Y = -1,
                },
                Context(playerId, roomId),
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
                        inner.Method.Name == nameof(IRoomCombat.CastSkillAsync)
                            ? $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} casts {inner.Args[1]} at {inner.Args[2]} ({inner.Args[3]}, {inner.Args[4]})"
                            : $"room {(long)call.Args![0]!}: player {ctx.PlayerId.Value} cancels"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
