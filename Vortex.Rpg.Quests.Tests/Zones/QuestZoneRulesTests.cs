using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.Rpg.Quests.Grains;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Quests.Zones;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Quests.Tests.Zones;

/// <summary>The rules engine's quest pieces (spec M2a §3.3): facts, the QuestState condition, the StartQuest action.</summary>
public sealed class QuestZoneRulesTests
{
    private static readonly PlayerId Player = new(7);

    private static QuestZoneFacts Facts(params (string Quest, string State)[] states) =>
        new() { States = states.ToImmutableDictionary(s => s.Quest, s => s.State) };

    private static ZoneRuleContext Context(QuestZoneFacts? facts, bool requirementsHold = true) =>
        new(
            Player,
            facts is null
                ? new Dictionary<Type, ZoneFacts>()
                : new Dictionary<Type, ZoneFacts> { [typeof(QuestZoneFacts)] = facts },
            DateTimeOffset.UnixEpoch,
            null!,
            null!
        )
        {
            AllHold = _ => requirementsHold,
        };

    [Fact]
    public void QuestState_ReadsTheFacts_AndNoneWhenAbsent()
    {
        QuestStateEvaluator evaluator = new();

        evaluator
            .Holds(
                new QuestStateCondition("quest.a", "ready"),
                Context(Facts(("quest.a", "ready")))
            )
            .Should()
            .BeTrue();
        evaluator
            .Holds(new QuestStateCondition("quest.a", "done"), Context(Facts(("quest.a", "ready"))))
            .Should()
            .BeFalse();
        evaluator
            .Holds(new QuestStateCondition("quest.b", "none"), Context(Facts(("quest.a", "ready"))))
            .Should()
            .BeTrue();
        evaluator
            .Holds(new QuestStateCondition("quest.b", "none"), Context(null))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void TheFactsSource_RaisesQuestState_ForEachQuestThatMoved()
    {
        QuestFactsSource source = new(null!);

        source
            .Changes(
                Facts(("quest.a", "active"), ("quest.b", "active")),
                Facts(("quest.a", "ready"), ("quest.b", "active"), ("quest.c", "active"))
            )
            .Should()
            .Equal(
                new ZoneTriggerSignal(ZoneTriggerType.QuestState, "quest.a"),
                new ZoneTriggerSignal(ZoneTriggerType.QuestState, "quest.c")
            );
    }

    [Fact]
    public async Task StartQuest_ChecksTheRequirements_ThenAccepts()
    {
        List<string> accepted = [];
        IGrainFactory grains = FakeProxy.Create<IGrainFactory>(_ =>
            FakeProxy.Create<IQuestLogGrain>(inner =>
            {
                accepted.Add((string)inner.Args![0]!);

                return Task.FromResult(QuestCodes.Ok);
            })
        );
        QuestDefinition quest = new()
        {
            Id = "quest.a",
            Kind = "quest",
            Level = 1,
            Objectives = [],
            Requires = [new PlayerLevelCondition(5, 0)],
        };
        IRpgContent content = FakeProxy.Create<IRpgContent>(call =>
            call.Method.Name == nameof(IRpgContent.Get) ? quest : null
        );
        StartQuestExecutor executor = new(grains, content);

        await executor.RunAsync(
            new StartQuestAction("quest.a"),
            Context(Facts(), requirementsHold: false),
            CancellationToken.None
        );
        accepted.Should().BeEmpty();

        await executor.RunAsync(
            new StartQuestAction("quest.a"),
            Context(Facts(), requirementsHold: true),
            CancellationToken.None
        );
        accepted.Should().Equal("quest.a");
    }
}
