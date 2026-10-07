namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// The competing weight for one (pool, variant).
/// </summary>
/// <remarks>
/// The set an entry actually competes in is: same pool, and either variantless or locked to the
/// same variant. Grouping it this way is what makes the number an operator reads match what the
/// picker does.
/// </remarks>
public sealed record LootTableWeightTotal(
    int PoolId,
    string? Variant,
    int TotalWeight,
    int Entries
);
