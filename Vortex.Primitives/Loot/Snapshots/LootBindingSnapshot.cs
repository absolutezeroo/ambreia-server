using Orleans;

namespace Vortex.Primitives.Loot.Snapshots;

/// <summary>What a furniture definition draws, and how much work it takes to get there.</summary>
[GenerateSerializer, Immutable]
public sealed record LootBindingSnapshot
{
    [Id(0)]
    public required string PoolCode { get; init; }

    /// <summary>Row id of the pool, for the once-per-player claims that key off it.</summary>
    [Id(2)]
    public required int PoolId { get; init; }

    /// <summary>Hits needed before the prize is handed out; never below one.</summary>
    [Id(1)]
    public required int HitsRequired { get; init; }
}
