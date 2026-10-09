using Vortex.Rpg.Content.Definitions;

namespace Vortex.Database.Tests.Rpg;

/// <summary>Items of content/item, for the character grain's item tests.</summary>
internal static class ItemContent
{
    public static readonly ItemDefinition Sword = new()
    {
        Id = "item.weapon.rusted_sword",
        Kind = "item",
        Category = ItemCategory.Equipment,
        Rarity = ItemRarity.Common,
        Slot = EquipmentSlot.Weapon,
        AllowedClasses = ["class.warrior"],
        Stats = new StatBlock { Strength = 3 },
        MaxDurability = 50,
    };

    public static readonly ItemDefinition Shard = new()
    {
        Id = "item.material.amber_shard",
        Kind = "item",
        Category = ItemCategory.Material,
        Rarity = ItemRarity.Common,
        MaxStack = 50,
    };

    /// <summary>What an import's replace rule moves <see cref="Shard"/> to.</summary>
    public static readonly ItemDefinition NewShard = Shard with { Id = "item.material.new_shard" };
}
