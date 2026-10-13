using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.Rpg.Quests.Grains;
using Vortex.Primitives.World;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Quests.Signals;
using Vortex.Rpg.Quests.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Quests.Tests.Signals;

/// <summary>What the game reports to quest logs (spec M2a §3.4): the producers, not the kinds.</summary>
public sealed class SignalsTests
{
    private readonly List<string> _calls = [];
    private int _zoneReads;

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            long player = (long)call.Args![0]!;

            return FakeProxy.Create<IQuestLogGrain>(inner =>
            {
                if (inner.Method.Name == nameof(IQuestLogGrain.CountAsync))
                {
                    QuestSignal s = (QuestSignal)inner.Args![0]!;
                    _calls.Add($"{player} {s.Name} {s.Zone} {string.Join(",", s.Subject)}");
                }
                else if (inner.Method.Name == nameof(IQuestLogGrain.RefreshHeldAsync))
                {
                    _calls.Add($"{player} held");
                }

                return Task.CompletedTask;
            });
        });

    private ZoneKeys Keys() =>
        new(
            FakeProxy.Create<IZoneRoomSource>(call =>
            {
                _zoneReads++;

                return call.Method.Name == nameof(IZoneRoomSource.GetPublishedAsync)
                    ? Task.FromResult<ZoneDocument?>(
                        ((RoomId)call.Args![0]!).Value == 2 ? ZoneSample.Camp : null
                    )
                    : null;
            })
        );

    private static IRpgContent Content() =>
        FakeProxy.Create<IRpgContent>(call =>
            call.Method.Name == nameof(IRpgContent.Get) ? TestContent.Wolf : null
        );

    [Fact]
    public async Task AKill_IsASignalForEachParticipant_WithItsZoneKeyAndRank()
    {
        MonsterKilledSignals handler = new(Grains(), Keys(), Content());

        await handler.HandleAsync(
            new MonsterKilledEvent(
                "monster.forest.wolf",
                2,
                new RoomId(2),
                [new PlayerId(7), new PlayerId(8)]
            ),
            null!,
            CancellationToken.None
        );

        _calls
            .Should()
            .Equal(
                "7 monster_killed camp [monster, monster.forest.wolf],[rank, normal]",
                "8 monster_killed camp [monster, monster.forest.wolf],[rank, normal]"
            );
    }

    [Fact]
    public async Task AKillInAnOrdinaryRoom_SignalsNothing()
    {
        MonsterKilledSignals handler = new(Grains(), Keys(), Content());

        await handler.HandleAsync(
            new MonsterKilledEvent("monster.forest.wolf", 2, new RoomId(1), [new PlayerId(7)]),
            null!,
            CancellationToken.None
        );

        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ZoneKeys_AreReadOncePerRoom()
    {
        ZoneKeys keys = Keys();

        (await keys.KeyOfAsync(new RoomId(2))).Should().Be("camp");
        (await keys.KeyOfAsync(new RoomId(2))).Should().Be("camp");

        _zoneReads.Should().Be(1);
    }

    [Fact]
    public async Task AnItemOrALevel_RefreshesHeldObjectives()
    {
        await new ItemGrantedHoldings(Grains()).HandleAsync(
            new ItemGrantedEvent(new PlayerId(7), "k", 1, "item.material.amber_shard", 2, false),
            null!,
            CancellationToken.None
        );
        await new ItemRemovedHoldings(Grains()).HandleAsync(
            new ItemRemovedEvent(new PlayerId(7), 1, "item.material.amber_shard", 1),
            null!,
            CancellationToken.None
        );

        _calls.Should().Equal("7 held", "7 held");
    }
}
