using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One rung. The level number is assigned server-side from the threshold order, because the client
/// pairs reward limits with levels by position.
/// </summary>
public sealed record CommunityGoalLevelBody(int ScoreThreshold, int RewardUserLimit);
