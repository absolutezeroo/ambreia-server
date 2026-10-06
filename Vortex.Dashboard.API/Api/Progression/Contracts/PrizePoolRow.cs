namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// One prize pool.
/// </summary>
/// <param name="Variants">Which variants this pool distinguishes, as the pool stores them.</param>
public sealed record PrizePoolRow(
    int Id,
    string Code,
    string Name,
    string? Variants,
    string? Notes,
    bool Enabled
);
