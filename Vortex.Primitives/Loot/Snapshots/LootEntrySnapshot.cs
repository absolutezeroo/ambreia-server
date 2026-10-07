using Orleans;
using Vortex.Primitives.Furniture.Enums;

namespace Vortex.Primitives.Loot.Snapshots;

/// <summary>One weighted entry of a prize pool (an enabled <c>loot_table_entries</c> row).</summary>
[GenerateSerializer, Immutable]
public sealed record LootEntrySnapshot
{
    [Id(0)]
    public required int Id { get; init; }

    /// <summary>Code of the pool this entry is drawn from.</summary>
    [Id(1)]
    public required string PoolCode { get; init; }

    /// <summary>Variant this entry is restricted to; empty means any variant.</summary>
    [Id(2)]
    public required string Variant { get; init; }

    [Id(3)]
    public required ProductType ProductType { get; init; }

    [Id(4)]
    public required int FurnitureDefinitionId { get; init; }

    [Id(5)]
    public required string ExtraParam { get; init; }

    [Id(6)]
    public required int Weight { get; init; }
}
