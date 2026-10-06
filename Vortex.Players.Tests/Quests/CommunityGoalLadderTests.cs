using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Vortex.Progression.Grains;
using Vortex.Progression.Quests;
using Xunit;

namespace Vortex.Players.Tests.Quests;

/// <summary>
///     The community goal's ladder maths. Every case here is a progress bar the player watches, so
///     an off-by-one shows up as a bar that never fills or jumps backwards.
/// </summary>
public sealed class CommunityGoalLadderTests
{
    private static readonly CommunityGoalRung[] Ladder =
    [
        new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 50),
        new(LevelNumber: 2, ScoreThreshold: 500, RewardUserLimit: 20),
        new(LevelNumber: 3, ScoreThreshold: 1000, RewardUserLimit: 5),
    ];

    [Fact]
    public void Resolve_ReportsNothingAchieved_BelowTheFirstRung()
    {
        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(
            Ladder,
            communityTotalScore: 25
        );

        standing.HighestAchievedLevel.Should().Be(0);
        standing.ScoreRemainingUntilNextLevel.Should().Be(75);
        standing.PercentCompletionTowardsNextLevel.Should().Be(25);
    }

    [Fact]
    public void Resolve_CountsAThresholdAsReached_WhenExactlyOnIt()
    {
        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(
            Ladder,
            communityTotalScore: 100
        );

        standing.HighestAchievedLevel.Should().Be(1);
        standing.ScoreRemainingUntilNextLevel.Should().Be(400);
        standing.PercentCompletionTowardsNextLevel.Should().Be(0);
    }

    [Fact]
    public void Resolve_MeasuresProgressBetweenRungs_NotFromZero()
    {
        // 300 is halfway from rung 1 (100) to rung 2 (500), not 60% of 500. Measuring from zero is
        // the classic version of this bug and makes the bar jump backwards on every level-up.
        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(
            Ladder,
            communityTotalScore: 300
        );

        standing.HighestAchievedLevel.Should().Be(1);
        standing.PercentCompletionTowardsNextLevel.Should().Be(50);
    }

    [Fact]
    public void Resolve_IsFinished_OnTheLastRung()
    {
        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(
            Ladder,
            communityTotalScore: 5000
        );

        standing.HighestAchievedLevel.Should().Be(3);
        standing.ScoreRemainingUntilNextLevel.Should().Be(0);
        standing.PercentCompletionTowardsNextLevel.Should().Be(100);
    }

    [Fact]
    public void Resolve_ReportsNoProgress_ForAGoalWithNoRungs()
    {
        // Showing 100% would tell the hotel the goal was complete before anyone contributed.
        CommunityGoalStanding standing = CommunityGoalLadder.Resolve([], communityTotalScore: 900);

        standing.HighestAchievedLevel.Should().Be(0);
        standing.PercentCompletionTowardsNextLevel.Should().Be(0);
    }

    [Fact]
    public void Resolve_SortsRungsByThreshold_WhenTheyAreEnteredOutOfOrder()
    {
        List<CommunityGoalRung> scrambled =
        [
            new(LevelNumber: 3, ScoreThreshold: 1000, RewardUserLimit: 5),
            new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 50),
            new(LevelNumber: 2, ScoreThreshold: 500, RewardUserLimit: 20),
        ];

        CommunityGoalLadder
            .Resolve(scrambled, communityTotalScore: 600)
            .HighestAchievedLevel.Should()
            .Be(2);
    }

    [Fact]
    public void Resolve_TreatsADuplicateThresholdAsAlreadyFull_RatherThanDividingByZero()
    {
        List<CommunityGoalRung> degenerate =
        [
            new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 10),
            new(LevelNumber: 2, ScoreThreshold: 100, RewardUserLimit: 5),
        ];

        CommunityGoalStanding standing = CommunityGoalLadder.Resolve(
            degenerate,
            communityTotalScore: 100
        );

        standing.HighestAchievedLevel.Should().Be(2);
        standing.PercentCompletionTowardsNextLevel.Should().Be(100);
    }

    [Fact]
    public void Resolve_ClampsANegativeTotalToZero()
    {
        CommunityGoalLadder
            .Resolve(Ladder, communityTotalScore: -50)
            .PercentCompletionTowardsNextLevel.Should()
            .Be(0);
    }

    [Fact]
    public void RewardUserLimits_ComeOutInLevelOrder()
    {
        // The client reads them as a flat array and pairs them with levels by position, so the order
        // is the contract.
        CommunityGoalLadder.RewardUserLimits(Ladder).Should().Equal(50, 20, 5);
    }
}
