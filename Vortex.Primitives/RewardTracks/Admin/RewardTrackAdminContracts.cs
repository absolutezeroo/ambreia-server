using System;
using System.Collections.Generic;

namespace Vortex.Primitives.RewardTracks.Admin;

/// <summary>The outcome of one content write.</summary>
public sealed record RewardTrackAdminResult(bool Success, int? Id, string? ErrorCode)
{
    public static RewardTrackAdminResult Ok(int id) => new(true, id, null);

    public static RewardTrackAdminResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>Create/update spec for a track. Tasks and prizes are managed separately.</summary>
public sealed record RewardTrackSpec(
    string TrackId,
    string Theme,
    RewardTrackStatus Status,
    int SortOrder,
    DateTime? StartsAt,
    DateTime? ProgressEndsAt,
    DateTime? ClaimEndsAt,
    RewardTrackUnlockKind UnlockKind,
    string UnlockValue,
    RewardTrackCompletionPolicy CompletionPolicy,
    bool PremiumEnabled,
    int PremiumBoostPerMille,
    int PremiumInstantPoints,
    int PremiumCostCredits,
    int PremiumCostDiamonds,
    bool Hidden,
    string CampaignCode
);

/// <summary>
/// Create/update spec for a task. <paramref name="Levels"/> replaces the whole stage ladder and
/// <paramref name="Steps"/> the whole sequence.
/// </summary>
/// <remarks>
/// <paramref name="ActionCode"/> is kept in step with the first step by the admin service: it is
/// what the client draws the task's icon from, and a sequence whose picture disagreed with its
/// first action would be a lie the operator cannot see.
/// </remarks>
public sealed record RewardTrackTaskSpec(
    string TaskId,
    string ActionCode,
    string Parameter,
    TaskProgressMode Mode,
    bool Premium,
    int SortOrder,
    IReadOnlyList<RewardTrackTaskLevelSpec> Levels,
    IReadOnlyList<RewardTrackTaskStepSpec>? Steps = null
);

/// <summary>One stage of a task.</summary>
public sealed record RewardTrackTaskLevelSpec(int RequiredCount, int PointsReward, bool Premium);

/// <summary>
/// One action in a task's sequence, with the tests a signal must pass to satisfy it.
/// </summary>
/// <remarks>
/// <paramref name="Tree"/> is the same conditions arranged rather than listed, and it wins when it
/// is there: both spellings say "all of this must hold", so a caller that sends only
/// <paramref name="Filters"/> means exactly what it always meant.
/// </remarks>
public sealed record RewardTrackTaskStepSpec(
    string ActionCode,
    IReadOnlyList<RewardTrackStepFilterSpec> Filters,
    IReadOnlyList<RewardTrackStepNodeSpec>? Tree = null
);

/// <summary>
/// One node of a step's condition tree: a test, or a group holding other nodes.
/// </summary>
/// <remarks>
/// A <see cref="PredicateKind.Test"/> carries <paramref name="Test"/> and no children, and every
/// other kind carries children and no test. The nodes of a step's <c>Tree</c> are the children of
/// an implicit <see cref="PredicateKind.All"/>, which is what a step has always meant.
/// </remarks>
public sealed record RewardTrackStepNodeSpec(
    PredicateKind Kind,
    RewardTrackStepFilterSpec? Test = null,
    IReadOnlyList<RewardTrackStepNodeSpec>? Children = null
);

/// <summary>
/// One test on a signal's facts. <paramref name="Value"/> is a literal, a comma-separated list for
/// <see cref="StepFilterOperator.OneOf"/>, or <c>$N</c> pointing back at step N.
/// </summary>
public sealed record RewardTrackStepFilterSpec(
    string FactKey,
    StepFilterOperator Operator,
    string Value
);

/// <summary>Create/update spec for a milestone. <paramref name="Rewards"/> replaces the whole bundle.</summary>
public sealed record RewardTrackPrizeSpec(
    string PrizeId,
    int RequiredPoints,
    bool Premium,
    int SortOrder,
    IReadOnlyList<RewardTrackRewardSpec> Rewards
);

/// <summary>One reward of a bundle.</summary>
public sealed record RewardTrackRewardSpec(
    RewardKind Kind,
    string RewardTypeId,
    int Amount,
    string ExtraParams,
    int SortOrder
);

/// <summary>Participation and conversion counts for one track, for the content list.</summary>
public sealed record RewardTrackStats(
    string TrackId,
    RewardTrackStatus Status,
    int TaskCount,
    int PrizeCount,
    int Participants,
    int Completions,
    int PremiumHolders,
    int PrizesClaimed
);

/// <summary>One player's standing on one track, for the progression inspector.</summary>
public sealed record PlayerRewardTrackAdminRow(
    string TrackId,
    int Points,
    bool PremiumUnlocked,
    DateTime? PremiumUnlockedAt,
    DateTime? CompletedAt,
    int TasksStarted,
    int PrizesClaimed
);

/// <summary>
/// A content problem. Reported rather than thrown so an operator sees every problem at once.
/// </summary>
/// <param name="TrackId">Which track it is in; empty for a problem spanning the catalog.</param>
public sealed record RewardTrackContentProblem(string TrackId, string Code, string Detail);

/// <summary>The validator's whole answer.</summary>
public sealed record RewardTrackContentReport(IReadOnlyList<RewardTrackContentProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}
