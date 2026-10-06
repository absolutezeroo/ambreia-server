using System;
using System.Collections.Generic;

namespace Vortex.Primitives.Quests.Admin;

/// <summary>Outcome of a quest-content admin write, same shape as the quest admin result.</summary>
public sealed record QuestContentAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static QuestContentAdminResult Ok(int id) => new(true, id, null);

    public static QuestContentAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// Create/update spec for a community goal. <paramref name="Levels"/> replaces the whole ladder:
/// rungs carry no player state, so rewriting them is safe and saves the operator a rung-by-rung
/// dance to reshape a goal.
/// </summary>
public sealed record CommunityGoalSpec(
    string Code,
    string CampaignCode,
    int ScorePerQuest,
    bool Enabled,
    DateTime? EndsAt,
    int SortOrder,
    IReadOnlyList<CommunityGoalLevelSpec> Levels
);

/// <summary>One rung: the total that unlocks it and how many contributors it rewards.</summary>
public sealed record CommunityGoalLevelSpec(
    int LevelNumber,
    int ScoreThreshold,
    int RewardUserLimit
);

/// <summary>
/// Create/update spec for a daily task. <paramref name="Rewards"/> replaces the task's reward list
/// wholesale — rewards are pure definition, and an assignment never points at one.
/// </summary>
public sealed record DailyTaskSpec(
    string TaskCode,
    string QuestTypeCode,
    bool IsBonus,
    string ImageVersion,
    string CatalogName,
    int RequiredRepeats,
    bool Enabled,
    int SortOrder,
    IReadOnlyList<DailyTaskRewardSpec> Rewards
);

public sealed record DailyTaskRewardSpec(
    short ProductItemTypeId,
    string RewardTypeId,
    string ExtraParams,
    int Amount
);
