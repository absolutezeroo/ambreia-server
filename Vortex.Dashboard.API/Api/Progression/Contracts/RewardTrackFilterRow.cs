namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One condition on a step.
/// </summary>
/// <param name="Op">The comparison, by the number the validator and the engine share. See
/// <see cref="FactOption.Operators"/> for which are allowed on a given fact.</param>
public sealed record RewardTrackFilterRow(
    string FactKey,
    int Op,
    string Value,
    RewardTrackValueRow Left,
    RewardTrackValueRow Right
);
