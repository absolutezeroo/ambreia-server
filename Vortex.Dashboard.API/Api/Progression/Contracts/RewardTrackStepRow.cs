using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One step of a multi-step task.
/// </summary>
/// <param name="Tree">The same conditions arranged rather than listed, and the one the editor
/// draws. A step stored flat arrives as a list of tests under the implicit <c>All</c> a step has
/// always been.</param>
public sealed record RewardTrackStepRow(
    int StepIndex,
    string ActionCode,
    IReadOnlyList<RewardTrackFilterRow> Filters,
    IReadOnlyList<RewardTrackNodeRow> Tree
);
