using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Kinds;
using Vortex.Rpg.Content.Rules;
using Vortex.Rpg.Content.Validation;
using Xunit;

namespace Vortex.Rpg.Tests.Content;

/// <summary>The rules of a quest (spec M2a §2.1, §2.3): the engine names no piece kind, each kind checks its own fields.</summary>
public sealed class QuestKindTests
{
    private sealed record CountFields(string Monster);

    private sealed class TestObjective : IQuestObjectiveRule
    {
        public string Kind => "test_kill";

        public IEnumerable<string> Problems(
            QuestObjectiveDefinition objective,
            ContentCheck check
        ) => QuestFields.Problem<CountFields>(objective.Fields) is { } problem ? [problem] : [];
    }

    private sealed class TestReward : IQuestRewardRule
    {
        public string Kind => "test_xp";

        public IEnumerable<string> Problems(QuestRewardDefinition reward, ContentCheck check) => [];
    }

    private const string Json = """
        {
          "id": "quest.camp.wolves",
          "kind": "quest",
          "level": 1,
          "requires": [{"kind": "PlayerLevel", "min": 1, "max": 0}],
          "objectives": [{"kind": "test_kill", "monster": "monster.forest.wolf", "count": 3}],
          "rewards": [{"kind": "test_xp", "amount": 120}]
        }
        """;

    private static QuestKind Kind() => new([new TestObjective()], [new TestReward()]);

    private static string[] Codes(QuestDefinition quest) =>
        [
            .. Kind()
                .Check(
                    quest,
                    new ContentCheck(
                        new Dictionary<string, ContentDefinition> { [quest.Id] = quest },
                        _ => false
                    )
                )
                .Select(f => f.Code),
        ];

    private static QuestDefinition Parse(string json) => (QuestDefinition)Kind().Parse(json);

    [Fact]
    public void AQuest_ParsesWithItsPiecesOwnFields()
    {
        QuestDefinition quest = Parse(Json);

        quest.Objectives.Single().Count.Should().Be(3);
        quest.Objectives.Single().Fields["monster"].GetString().Should().Be("monster.forest.wolf");
        quest.Rewards.Single().Fields["amount"].GetInt32().Should().Be(120);
        quest.Requires.Single().Should().BeOfType<PlayerLevelCondition>();
        Codes(quest).Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownObjectiveKind_IsRefused() =>
        Codes(Parse(Json.Replace("\"test_kill\"", "\"fly\""))).Should().Equal("UnknownObjective");

    [Fact]
    public void AnUnknownRewardKind_IsRefused() =>
        Codes(Parse(Json.Replace("\"test_xp\"", "\"gold\""))).Should().Equal("UnknownReward");

    [Fact]
    public void ACountBelowOne_IsRefused() =>
        Codes(Parse(Json.Replace("\"count\": 3", "\"count\": 0"))).Should().Equal("BadCount");

    [Fact]
    public void AnUnknownFieldInAPiece_IsRefused() =>
        Codes(Parse(Json.Replace("\"monster\":", "\"mosnter\":"))).Should().Equal("BadObjective");

    [Fact]
    public void AQuestWithoutObjectives_IsRefused() =>
        Codes(
                Parse(
                    Json.Replace(
                        "[{\"kind\": \"test_kill\", \"monster\": \"monster.forest.wolf\", \"count\": 3}]",
                        "[]"
                    )
                )
            )
            .Should()
            .Equal("NoObjective");

    [Fact]
    public void ARequirementOnAnUnknownQuest_IsRefused() =>
        Codes(
                Parse(
                    Json.Replace(
                        "{\"kind\": \"PlayerLevel\", \"min\": 1, \"max\": 0}",
                        "{\"kind\": \"QuestState\", \"quest\": \"quest.nope\", \"state\": \"done\"}"
                    )
                )
            )
            .Should()
            .Equal("BrokenReference");

    [Fact]
    public void TheObjectiveTexts_AreTextKeys() =>
        Kind().TextKeys(Parse(Json)).Should().Equal("quest.camp.wolves.objective.0");

    [Fact]
    public void QuestFields_ReadsStrictly()
    {
        Dictionary<string, JsonElement> fields = new()
        {
            ["monster"] = JsonDocument.Parse("\"monster.forest.wolf\"").RootElement,
        };

        QuestFields.Read<CountFields>(fields).Monster.Should().Be("monster.forest.wolf");
        QuestFields
            .Problem<CountFields>(new Dictionary<string, JsonElement>())
            .Should()
            .NotBeNull();
    }
}
