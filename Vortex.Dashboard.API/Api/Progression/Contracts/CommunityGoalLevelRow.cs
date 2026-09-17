namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One rung of a goal.
/// </summary>
/// <param name="RewardUserLimit">How many players the rung pays out to, 0 for everyone.</param>
/// <param name="RewardCoins">
/// Whole VortexCoin each of those players is paid when the rung falls; 0 for a rung that rewards
/// nothing on the chain.
/// <para>
/// <b>0 in <paramref name="RewardUserLimit" /> does NOT mean "everyone" here.</b> The limit's "0 for
/// everyone" reading is about what the client draws; a coin reward with no ceiling would be an
/// unbounded number of Ethereum transactions, so the payout treats 0 as "nobody" and skips the
/// rung. A rung that is meant to pay coin names how many players it pays.
/// </para>
/// </param>
public sealed record CommunityGoalLevelRow(
    int Id,
    int LevelNumber,
    int ScoreThreshold,
    int RewardUserLimit,
    int RewardCoins,
    bool Reached
);
