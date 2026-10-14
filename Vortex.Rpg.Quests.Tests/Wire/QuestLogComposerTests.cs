using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Quests.Wire;
using Xunit;
using static Vortex.Rpg.Quests.Tests.Support.Pieces;

namespace Vortex.Rpg.Quests.Tests.Wire;

/// <summary>RpgQuestLog from the log (spec M2a §3.2): the state's name, each objective's kind and counter.</summary>
public sealed class QuestLogComposerTests
{
    private static readonly QuestDefinition Wolves = new()
    {
        Id = "quest.camp.wolves",
        Kind = "quest",
        Level = 1,
        Objectives =
        [
            Objective("kill", 3, ("monster", "monster.forest.wolf")),
            Objective("talk", 1, ("zone", "camp"), ("npc", "scout")),
        ],
    };

    [Fact]
    public void AQuest_IsItsStateAndItsLines()
    {
        RpgQuestLogMessageComposer log = QuestLogComposer.Of(
            [new QuestEntrySnapshot("quest.camp.wolves", QuestStatus.Active, [2, 0])],
            id => id == Wolves.Id ? Wolves : null
        );

        RpgQuestLogEntry quest = log.Quests.Single();
        (quest.QuestId, quest.State).Should().Be(("quest.camp.wolves", "active"));
        quest
            .Objectives.Select(o => (o.Kind, o.Current, o.Required))
            .Should()
            .Equal(("kill", 2, 3), ("talk", 0, 1));
    }

    [Fact]
    public void AQuestTheContentLost_KeepsItsCounters() =>
        QuestLogComposer
            .Of([new QuestEntrySnapshot("quest.gone", QuestStatus.Done, [1])], _ => null)
            .Quests.Single()
            .Objectives.Single()
            .Should()
            .Be(
                new RpgQuestObjectiveLine
                {
                    Kind = string.Empty,
                    Current = 1,
                    Required = 1,
                }
            );
}
