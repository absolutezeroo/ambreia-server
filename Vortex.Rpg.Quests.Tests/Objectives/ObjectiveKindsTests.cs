using System.Collections.Generic;
using System.Collections.Immutable;
using FluentAssertions;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Validation;
using Vortex.Rpg.Quests.Objectives;
using Vortex.Rpg.Quests.Tests.Support;
using Xunit;
using static Vortex.Rpg.Quests.Tests.Support.Pieces;

namespace Vortex.Rpg.Quests.Tests.Objectives;

/// <summary>Each shipped objective kind alone (spec M2a §2.1, §3.4): a signal and an objective in, an amount out.</summary>
public sealed class ObjectiveKindsTests
{
    private static readonly ContentCheck Content = new(
        new Dictionary<string, ContentDefinition>
        {
            ["monster.forest.wolf"] = TestContent.Wolf,
            ["item.material.amber_shard"] = TestContent.Shard,
        },
        _ => false
    );

    [Fact]
    public void Kill_MatchesItsMonster_ItsZone_AndItsRank()
    {
        KillObjective kill = new();
        QuestSignal wolf = Signal(
            QuestSignals.MonsterKilled,
            "camp",
            ("monster", "monster.forest.wolf"),
            ("rank", "normal")
        );

        kill.Counts(Objective("kill", 3, ("monster", "monster.forest.wolf")), wolf).Should().Be(1);
        kill.Counts(Objective("kill", 1, ("zone", "camp")), wolf).Should().Be(1);
        kill.Counts(Objective("kill", 1, ("zone", "forest")), wolf).Should().Be(0);
        kill.Counts(Objective("kill", 1, ("zone", "camp"), ("rank", "elite")), wolf).Should().Be(0);
        kill.Counts(
                Objective("kill", 1, ("monster", "monster.forest.wolf")),
                Signal(QuestSignals.AreaEntered, "camp", ("area", "x"))
            )
            .Should()
            .Be(0);
    }

    [Fact]
    public void Kill_NeedsAFilter_AKnownMonster_AndARank()
    {
        KillObjective kill = new();

        kill.Problems(Objective("kill"), Content).Should().NotBeEmpty();
        kill.Problems(Objective("kill", 1, ("monster", "monster.nope")), Content)
            .Should()
            .NotBeEmpty();
        kill.Problems(Objective("kill", 1, ("rank", "legendary")), Content).Should().NotBeEmpty();
        kill.Problems(Objective("kill", 1, ("monster", "monster.forest.wolf")), Content)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Reach_Interact_AndTalk_MatchTheirPlace()
    {
        new ReachObjective()
            .Counts(
                Objective("reach", 1, ("zone", "camp"), ("area", "clearing")),
                Signal(QuestSignals.AreaEntered, "camp", ("area", "clearing"))
            )
            .Should()
            .Be(1);
        new ReachObjective()
            .Counts(
                Objective("reach", 1, ("zone", "camp"), ("area", "clearing")),
                Signal(QuestSignals.AreaEntered, "other", ("area", "clearing"))
            )
            .Should()
            .Be(0);
        new InteractObjective()
            .Counts(
                Objective("interact", 1, ("zone", "camp"), ("object", "old_well")),
                Signal(QuestSignals.DecorUsed, "camp", ("object", "old_well"))
            )
            .Should()
            .Be(1);
        new TalkObjective()
            .Counts(
                Objective("talk", 1, ("zone", "camp"), ("npc", "scout")),
                Signal(QuestSignals.NpcTalked, "camp", ("npc", "scout"))
            )
            .Should()
            .Be(1);
        new TalkObjective()
            .Counts(
                Objective("talk", 1, ("zone", "camp"), ("npc", "scout")),
                Signal(QuestSignals.NpcTalked, "camp", ("npc", "elder"))
            )
            .Should()
            .Be(0);
        new ReachObjective()
            .Problems(Objective("reach", 1, ("zone", "camp")), Content)
            .Should()
            .NotBeEmpty("area is required");
    }

    [Fact]
    public void Collect_CountsTheHoldings_AndNeedsAKnownItem()
    {
        CollectObjective collect = new();
        QuestHoldings holdings = new(
            ImmutableDictionary<string, int>.Empty.Add("item.material.amber_shard", 4),
            2
        );

        collect
            .Current(Objective("collect", 5, ("item", "item.material.amber_shard")), holdings)
            .Should()
            .Be(4);
        collect
            .Problems(Objective("collect", 5, ("item", "item.nope")), Content)
            .Should()
            .NotBeEmpty();
        collect
            .Problems(
                Objective("collect", 5, ("item", "item.material.amber_shard"), ("consume", true)),
                Content
            )
            .Should()
            .BeEmpty();
    }
}
