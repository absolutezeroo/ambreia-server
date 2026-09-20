using System;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One side of a comparison.
/// </summary>
/// <remarks>
/// <c>Kind</c> is the number the editor, the validator and the engine share — see
/// <see cref="Vortex.Primitives.RewardTracks.StepValueKind"/>. <c>Arg</c> is a fact key or the
/// literal text, and <c>Step</c> names an earlier step for a step reference.
/// </remarks>
public sealed record RewardTrackStepValueBody(int Kind, string Arg, int Step = -1);
