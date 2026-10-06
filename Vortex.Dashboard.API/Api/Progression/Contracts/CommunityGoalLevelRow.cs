namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One rung of a goal.
/// </summary>
/// <param name="RewardUserLimit">How many players the rung pays out to, 0 for everyone.</param>
public sealed record CommunityGoalLevelRow(
    int Id,
    int LevelNumber,
    int ScoreThreshold,
    int RewardUserLimit,
    bool Reached
);
