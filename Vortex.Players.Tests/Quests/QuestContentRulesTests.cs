using System;
using System.Collections.Generic;
using FluentAssertions;
using Vortex.Dashboard.API.Admin.Rules;
using Vortex.Primitives.Quests.Admin;
using Vortex.Progression.Grains;
using Xunit;

namespace Vortex.Players.Tests.Quests;

/// <summary>
///     What the dashboard may save as quest content. These refuse the shapes that produce a feature
///     which looks configured and cannot work — a ladder that never advances, a task nothing can
///     complete.
/// </summary>
public sealed class QuestContentRulesTests
{
    [Fact]
    public void ValidateDailyTask_RejectsATaskWithNoObjective()
    {
        // Nothing would ever advance it, so it can never be completed or claimed.
        QuestContentRules
            .ValidateDailyTask(Task() with { QuestTypeCode = "" })
            .Should()
            .Be("task_quest_type_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ValidateDailyTask_RejectsANonPositiveRepeatCount(int repeats)
    {
        QuestContentRules
            .ValidateDailyTask(Task() with { RequiredRepeats = repeats })
            .Should()
            .Be("task_repeats_invalid");
    }

    [Fact]
    public void ValidateDailyTask_RejectsARewardWithNoType()
    {
        QuestContentRules
            .ValidateDailyTask(Task(new DailyTaskRewardSpec(0, "  ", "", 10)))
            .Should()
            .Be("reward_type_required");
    }

    [Fact]
    public void ValidateDailyTask_RejectsARewardOfNothing()
    {
        QuestContentRules
            .ValidateDailyTask(Task(new DailyTaskRewardSpec(0, "credits", "", 0)))
            .Should()
            .Be("reward_amount_invalid");
    }

    [Fact]
    public void ValidateDailyTask_AcceptsATaskWithNoRewardAtAll()
    {
        // A task can be worth doing for the badge count alone; only a malformed reward is refused.
        QuestContentRules.ValidateDailyTask(Task()).Should().BeNull();
    }

    private static DailyTaskSpec Task(params DailyTaskRewardSpec[] rewards) =>
        new(
            TaskCode: "visit_rooms",
            QuestTypeCode: "RoomEntry",
            IsBonus: false,
            ImageVersion: "",
            CatalogName: "",
            RequiredRepeats: 3,
            Enabled: true,
            SortOrder: 0,
            Rewards: new List<DailyTaskRewardSpec>(rewards)
        );
}
