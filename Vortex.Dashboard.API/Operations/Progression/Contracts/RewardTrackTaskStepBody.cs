using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One action in a task's sequence, with the tests a signal must pass to satisfy it.
/// </summary>
/// <remarks>
/// <c>Tree</c> is the same conditions arranged rather than listed, and it wins when it is there.
/// A caller that sends only <c>Filters</c> means what it has always meant.
/// </remarks>
public sealed record RewardTrackTaskStepBody(
    string ActionCode,
    IReadOnlyList<RewardTrackStepFilterBody>? Filters,
    IReadOnlyList<RewardTrackStepNodeBody>? Tree = null
);
