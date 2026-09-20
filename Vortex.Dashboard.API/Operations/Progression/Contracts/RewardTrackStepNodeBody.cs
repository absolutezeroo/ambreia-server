using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One node of a step's condition tree: a test, or a group holding other nodes.
/// </summary>
/// <remarks>
/// <c>Kind</c> is the number the editor, the validator and the engine share — see
/// <see cref="Vortex.Primitives.RewardTracks.PredicateKind"/>. A test carries <c>Test</c> and no
/// children; every other kind carries children and no test, and the validator refuses the mixtures.
/// </remarks>
public sealed record RewardTrackStepNodeBody(
    int Kind,
    RewardTrackStepFilterBody? Test,
    IReadOnlyList<RewardTrackStepNodeBody>? Children
);
