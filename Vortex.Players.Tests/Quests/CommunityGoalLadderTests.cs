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

    /// <summary>
    ///     Which rungs owe coins. Unlike the progress bar above, an off-by-one here is real money on
    ///     a public chain: a rung paid twice cannot be taken back, and a rung skipped is a reward
    ///     nothing will ever notice is missing.
    /// </summary>
    private static readonly CommunityGoalRung[] PaidLadder =
    [
        new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 10, RewardCoins: 5),
        new(LevelNumber: 2, ScoreThreshold: 500, RewardUserLimit: 10, RewardCoins: 10),
        new(LevelNumber: 3, ScoreThreshold: 1000, RewardUserLimit: 5, RewardCoins: 25),
    ];

    [Fact]
    public void Settle_OwesNothing_BelowTheFirstRung()
    {
        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            PaidLadder,
            communityTotalScore: 99,
            rewardedLevel: 0
        );

        settlement.RewardedLevel.Should().Be(0);
        settlement.Due.Should().BeEmpty();
    }

    [Fact]
    public void Settle_OwesTheRung_WhenExactlyOnItsThreshold()
    {
        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            PaidLadder,
            communityTotalScore: 100,
            rewardedLevel: 0
        );

        settlement.RewardedLevel.Should().Be(1);
        settlement.Due.Should().ContainSingle().Which.RewardCoins.Should().Be(5);
    }

    [Fact]
    public void Settle_OwesNothingTwice_OnceARungIsSettled()
    {
        // The hotel total only ever rises, so this runs on EVERY contribution for the rest of the
        // campaign. If it ever owed again, the campaign would mint its first rung forever.
        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            PaidLadder,
            communityTotalScore: 400,
            rewardedLevel: 1
        );

        settlement.RewardedLevel.Should().Be(1);
        settlement.Due.Should().BeEmpty();
    }

    [Fact]
    public void Settle_OwesEveryRungAJumpCleared()
    {
        // One contribution can cross more than one rung — a late surge, or a ladder an operator
        // edited downwards. Paying only the highest would skip the others with nothing to show it.
        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            PaidLadder,
            communityTotalScore: 1200,
            rewardedLevel: 0
        );

        settlement.RewardedLevel.Should().Be(3);
        settlement.Due.Select(rung => rung.LevelNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Settle_MovesPastAFreeRung_WithoutOwingForIt()
    {
        // rewarded_level means "the highest rung already settled", not "the highest that cost
        // coins": a version that only advanced past paying rungs would re-examine the free ones on
        // every contribution forever.
        CommunityGoalRung[] mixed =
        [
            new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 10, RewardCoins: 0),
            new(LevelNumber: 2, ScoreThreshold: 500, RewardUserLimit: 10, RewardCoins: 10),
        ];

        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            mixed,
            communityTotalScore: 200,
            rewardedLevel: 0
        );

        settlement.RewardedLevel.Should().Be(1);
        settlement.Due.Should().BeEmpty();
    }

    [Fact]
    public void Settle_OwesNothing_ForARungThatPaysNobody()
    {
        // `reward_user_limit` of 0 reads as "everyone" in the client's display. An unbounded number
        // of Ethereum transactions is not something to start by omission.
        CommunityGoalRung[] unbounded =
        [
            new(LevelNumber: 1, ScoreThreshold: 100, RewardUserLimit: 0, RewardCoins: 50),
        ];

        CommunityGoalSettlement settlement = CommunityGoalLadder.Settle(
            unbounded,
            communityTotalScore: 999,
            rewardedLevel: 0
        );

        settlement.RewardedLevel.Should().Be(1);
        settlement.Due.Should().BeEmpty();
    }
}
