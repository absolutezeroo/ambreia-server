using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Quests;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Quests.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec M2a §3.1: abandoning reaches the player's own log, and he is told the answer.</summary>
public sealed class QuestAbandonHandlerTests
{
    private readonly List<string> _calls = [];
    private readonly List<object> _sent = [];

    [Fact]
    public async Task AnAbandon_ReachesTheOwnLog_AndAnswers()
    {
        await AbandonAsync(7);

        _calls.Should().Equal("log 7 abandon quest.camp.wolves");
        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new RpgQuestResultMessageComposer
                {
                    Action = "abandoned",
                    Code = "quest.not_in_log",
                    QuestId = "quest.camp.wolves",
                }
            );
    }

    [Fact]
    public async Task WithoutAPlayer_NothingIsCalled()
    {
        await AbandonAsync(0);

        _calls.Should().BeEmpty();
        _sent.Should().BeEmpty();
    }

    private Task AbandonAsync(int playerId) =>
        new RpgQuestAbandonMessageHandler(Grains())
            .HandleAsync(
                new RpgQuestAbandonMessage { QuestId = "quest.camp.wolves" },
                new MessageContext(
                    FakeProxy.Create<ISessionContext>(_ => null),
                    new PlayerId(playerId),
                    new RoomId(12)
                ),
                CancellationToken.None
            )
            .AsTask();

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            long key = (long)call.Args![0]!;

            if (call.Method.GetGenericArguments()[0] == typeof(IQuestLogGrain))
            {
                return FakeProxy.Create<IQuestLogGrain>(inner =>
                {
                    _calls.Add($"log {key} abandon {inner.Args![0]}");

                    return Task.FromResult("quest.not_in_log");
                });
            }

            return FakeProxy.Create<IPlayerPresenceGrain>(inner =>
            {
                foreach (object? arg in inner.Args ?? [])
                {
                    if (arg is object[] many)
                    {
                        _sent.AddRange(many);
                    }
                    else if (arg is IComposer one)
                    {
                        _sent.Add(one);
                    }
                }

                return Task.CompletedTask;
            });
        });
}
