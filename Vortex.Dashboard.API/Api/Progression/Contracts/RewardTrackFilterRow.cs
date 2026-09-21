namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One condition on a step.
/// </summary>
/// <param name="Op">The comparison, by the number the validator and the engine share. See
/// <see cref="FactOption.Operators"/> for which are allowed on a given fact.</param>
/// <param name="IconUrl">
/// The sprite of the furniture this condition names, when it names one by definition id.
/// </param>
/// <param name="FurniWidth">
/// How many tiles across that furniture is, before rotation. Zero when this names none.
/// </param>
/// <param name="FurniLength">
/// And how many along. A sofa is 2x1, a tile puzzle 3x3, a pool 2x6 — and the diagram of the tiles
/// around it is a lie at 1x1, because the sprite says six tiles and the picture says one.
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
    string? IconUrl,
    int FurniWidth,
    int FurniLength
);
