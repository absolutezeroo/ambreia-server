using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One node of a step's condition tree.
/// </summary>
/// <param name="Kind">By the number the editor, the validator and the engine share: see
/// <see cref="Vortex.Primitives.RewardTracks.PredicateKind"/>.</param>
/// <remarks>
/// Sent for every step, including the ones stored as a flat list — those arrive as a list of tests,
/// which is what a flat list is. The editor draws one shape rather than two.
/// </remarks>
public sealed record RewardTrackNodeRow(
    int Kind,
    RewardTrackFilterRow? Test,
    IReadOnlyList<RewardTrackNodeRow> Children
);
