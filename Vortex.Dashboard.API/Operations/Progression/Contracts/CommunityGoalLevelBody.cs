using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One rung. The level number is assigned server-side from the threshold order, because the client
/// pairs reward limits with levels by position.
/// </summary>
/// <param name="RewardCoins">
/// Whole VortexCoin paid to each of the rung's top contributors when it falls. Optional, so a
/// ladder posted by anything that predates the coin campaign still parses and rewards nothing.
/// </param>
public sealed record CommunityGoalLevelBody(
    int ScoreThreshold,
    int RewardUserLimit,
    int RewardCoins = 0
);
