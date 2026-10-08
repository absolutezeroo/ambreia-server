using Orleans;
using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Primitives.Rpg.Items;

/// <summary>One copy a character holds: in its bag, worn, or waiting in a parcel.</summary>
[GenerateSerializer, Immutable]
public sealed record CharacterItemSnapshot
{
    /// <summary>The <c>rpg_items</c> id; 0 for a copy not written yet.</summary>
    [Id(0)]
    public int ItemId { get; init; }

    [Id(1)]
    public required string DefinitionId { get; init; }

    [Id(2)]
    public int Quantity { get; init; }

    /// <summary>Null for an item that never wears out.</summary>
    [Id(3)]
    public int? Durability { get; init; }

    [Id(4)]
    public bool Bound { get; init; }

    [Id(5)]
    public ItemContainer Container { get; init; }

    /// <summary>The bag case, the equipment slot, or the parcel's number.</summary>
    [Id(6)]
    public int Slot { get; init; }
}
