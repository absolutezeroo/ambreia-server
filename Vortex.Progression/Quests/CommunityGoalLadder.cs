using System;
using System.Collections.Generic;
using System.Linq;

namespace Vortex.Progression.Quests;

/// <summary>One rung of a community goal, as the ladder maths sees it.</summary>
public readonly record struct CommunityGoalRung(
    int LevelNumber,
    int ScoreThreshold,
    int RewardUserLimit
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

    /// <summary>The per-rung reward limits in level order, as the client reads them.</summary>
    public static IReadOnlyList<int> RewardUserLimits(IEnumerable<CommunityGoalRung> rungs) =>
        [
            .. rungs
                .OrderBy(r => r.ScoreThreshold)
                .ThenBy(r => r.LevelNumber)
                .Select(r => r.RewardUserLimit),
        ];
}
