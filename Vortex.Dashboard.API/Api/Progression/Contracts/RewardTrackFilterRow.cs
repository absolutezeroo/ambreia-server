namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One condition on a step.
/// </summary>
/// <param name="Op">The comparison, by the number the validator and the engine share. See
/// <see cref="FactOption.Operators"/> for which are allowed on a given fact.</param>
/// <param name="IconUrl">
/// The sprite of the furniture this condition names, when it names one by definition id.
/// </param>
/// <remarks>
/// The icon is resolved here rather than remembered by the editor. Kept editor-side it was gone
/// the moment a task was reopened, which is exactly when an operator is reading a condition they
/// wrote last week and needs to see which sofa it is about.
/// </remarks>
public sealed record RewardTrackFilterRow(
    string FactKey,
    int Op,
    string Value,
    RewardTrackValueRow Left,
    RewardTrackValueRow Right,
    string? IconUrl
);
