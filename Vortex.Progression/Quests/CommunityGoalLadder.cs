using System;
using System.Collections.Generic;
using System.Linq;

namespace Vortex.Progression.Quests;

/// <summary>One rung of a community goal, as the ladder maths sees it.</summary>
/// <param name="RewardCoins">
/// Whole VortexCoin each of the top <paramref name="RewardUserLimit" /> contributors is paid when
/// this rung falls. Zero means the rung rewards nothing on the chain. The ladder maths below never
/// reads it — it rides along because the grain resolves the rungs once and the payout needs the
/// same rows. Defaulted so the ladder's own tests, which are about thresholds, stay about
/// thresholds.
/// </param>
public readonly record struct CommunityGoalRung(
    int LevelNumber,
    int ScoreThreshold,
    int RewardUserLimit,
    int RewardCoins = 0
);

/// <summary>What a community total owes: the rungs to pay now, and where the ladder has got to.</summary>
/// <param name="RewardedLevel">
/// What <c>community_goals.rewarded_level</c> becomes. Equal to the value passed in when nothing new
/// was cleared.
/// </param>
/// <param name="Due">
/// The rungs whose coin reward falls due, in threshold order. Empty is the normal case — a ladder
/// only crosses a rung once.
/// </param>
public readonly record struct CommunityGoalSettlement(
    int RewardedLevel,
    IReadOnlyList<CommunityGoalRung> Due
);

/// <summary>Where a community total sits on the ladder.</summary>
public readonly record struct CommunityGoalStanding(
    int HighestAchievedLevel,
    int ScoreRemainingUntilNextLevel,
    int PercentCompletionTowardsNextLevel
);

/// <summary>
/// Turns a community total into the three numbers the widget draws: the rung reached, how far the
/// next one is, and the percentage between them. Pure, because every off-by-one here is visible as
/// a progress bar that never fills or jumps backwards.
/// </summary>
public static class CommunityGoalLadder
{
    /// <summary>
    /// Resolves the standing. Rungs are sorted by threshold, so an operator entering them out of
    /// order still gets a sane ladder rather than a bar that walks backwards.
    /// </summary>
    public static CommunityGoalStanding Resolve(
        IEnumerable<CommunityGoalRung> rungs,
        int communityTotalScore
    )
    {
        List<CommunityGoalRung> ordered =
        [
            .. rungs.OrderBy(r => r.ScoreThreshold).ThenBy(r => r.LevelNumber),
        ];

        if (ordered.Count == 0)
        {
            // A goal with no rungs can never progress; showing 100% would claim it was finished.
            return new CommunityGoalStanding(0, 0, 0);
        }

        int total = Math.Max(0, communityTotalScore);
        int achievedIndex = -1;

        for (int i = 0; i < ordered.Count; i++)
        {
            if (total >= ordered[i].ScoreThreshold)
            {
                achievedIndex = i;
            }
        }

        // Every rung cleared: the ladder is finished, nothing remains, and the bar is full.
        if (achievedIndex == ordered.Count - 1)
        {
            return new CommunityGoalStanding(ordered[achievedIndex].LevelNumber, 0, 100);
        }

        CommunityGoalRung next = ordered[achievedIndex + 1];
        int floor = achievedIndex < 0 ? 0 : ordered[achievedIndex].ScoreThreshold;
        int achievedLevel = achievedIndex < 0 ? 0 : ordered[achievedIndex].LevelNumber;

        int span = next.ScoreThreshold - floor;
        int into = total - floor;

        // A rung sharing its predecessor's threshold would divide by zero; treat it as already full.
        int percent = span <= 0 ? 100 : (int)Math.Clamp(into * 100L / span, 0, 100);

        return new CommunityGoalStanding(
            achievedLevel,
            Math.Max(0, next.ScoreThreshold - total),
            percent
        );
    }

    /// <summary>
    /// Which rungs owe a coin reward now, and how far <c>rewarded_level</c> should advance.
    /// </summary>
    /// <param name="rewardedLevel">The highest rung already settled; 0 when none has been.</param>
    /// <remarks>
    /// <para>
    /// Pure, and separate from the grain that uses it, because this is the arithmetic that decides
    /// whether real coins are minted. It gets three things right that are easy to get wrong:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b><see cref="CommunityGoalSettlement.RewardedLevel" /> advances past cleared rungs that pay
    /// nothing too</b>, so it means "the highest rung already settled". A version that only advanced
    /// past paying rungs would re-examine the free ones forever.
    /// </item>
    /// <item>
    /// <b>A jump can clear several rungs at once</b> — a big contribution, or a ladder edited
    /// downwards — and each of them pays. Only paying the highest would silently skip rewards.
    /// </item>
    /// <item>
    /// <b>A rung paying coins to nobody pays nothing.</b> <c>reward_user_limit</c> of 0 reads as
    /// "everyone" in the client's display, but an unbounded number of Ethereum transactions is not
    /// something to do by omission, so here 0 means no coin payout.
    /// </item>
    /// </list>
    /// </remarks>
    public static CommunityGoalSettlement Settle(
        IEnumerable<CommunityGoalRung> rungs,
        int communityTotalScore,
        int rewardedLevel
    )
    {
        List<CommunityGoalRung> ordered =
        [
            .. rungs.OrderBy(r => r.ScoreThreshold).ThenBy(r => r.LevelNumber),
        ];

        int total = Math.Max(0, communityTotalScore);
        int highestCleared = rewardedLevel;

        foreach (CommunityGoalRung rung in ordered)
        {
            if (total >= rung.ScoreThreshold)
            {
                highestCleared = Math.Max(highestCleared, rung.LevelNumber);
            }
        }

        if (highestCleared <= rewardedLevel)
        {
            return new CommunityGoalSettlement(rewardedLevel, []);
        }

        return new CommunityGoalSettlement(
            highestCleared,
            [
                .. ordered.Where(r =>
                    r.LevelNumber > rewardedLevel
                    && r.LevelNumber <= highestCleared
                    && r.RewardCoins > 0
                    && r.RewardUserLimit > 0
                ),
            ]
        );
    }

    /// <summary>The per-rung reward limits in level order, as the client reads them.</summary>
    public static IReadOnlyList<int> RewardUserLimits(IEnumerable<CommunityGoalRung> rungs) =>
        [
            .. rungs
                .OrderBy(r => r.ScoreThreshold)
                .ThenBy(r => r.LevelNumber)
                .Select(r => r.RewardUserLimit),
        ];
}
