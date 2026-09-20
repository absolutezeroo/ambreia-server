namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One side of a comparison.
/// </summary>
/// <param name="Kind">By the number the editor, the validator and the engine share: see
/// <see cref="Vortex.Primitives.RewardTracks.StepValueKind"/>.</param>
/// <param name="Arg">A fact key, or the literal text.</param>
/// <param name="Step">Which earlier step to read, for a step reference; <c>-1</c> otherwise.</param>
/// <remarks>
/// Sent for every condition, including the ones stored before sides existed — those arrive as the
/// fact on the left and the literal on the right, which is what they have always been. The editor
/// draws one shape rather than two.
/// </remarks>
public sealed record RewardTrackValueRow(int Kind, string Arg, int Step);
