using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>An item (spec Objets §4): what any copy of it is, wherever it lies.</summary>
public sealed record ItemDefinition : ContentDefinition
{
    public required ItemCategory Category { get; init; }

    public required ItemRarity Rarity { get; init; }

    /// <summary>Required for equipment, forbidden otherwise.</summary>
    public EquipmentSlot? Slot { get; init; }

    public int RequiredLevel { get; init; } = 1;

    /// <summary>Class ids; empty = every class.</summary>
    public List<string> AllowedClasses { get; init; } = [];

    /// <summary>Equipment only.</summary>
    public StatBlock? Stats { get; init; }

    /// <summary>0 = never wears out. Equipment only.</summary>
    public int MaxDurability { get; init; }

    public int MaxStack { get; init; } = 1;

    /// <summary>What a merchant pays, in gold.</summary>
    public int SellValue { get; init; }

    public ItemBinding Binding { get; init; }

    /// <summary>The furniture_definitions id a furniture item places. Furniture only.</summary>
    public int? FurnitureDefinition { get; init; }
}
