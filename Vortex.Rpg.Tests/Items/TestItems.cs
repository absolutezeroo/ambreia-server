using System.Linq;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Items;

namespace Vortex.Rpg.Tests.Items;

/// <summary>Item definitions shaped like content/item, and inventories over them.</summary>
internal static class TestItems
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

    public static readonly ItemDefinition Ring = new()
    {
        Id = "item.ring.copper_ring",
        Kind = "item",
        Category = ItemCategory.Equipment,
        Rarity = ItemRarity.Common,
        Slot = EquipmentSlot.Ring,
        Stats = new StatBlock { Agility = 1 },
        MaxDurability = 40,
    };

    /// <summary>Rare: binds when equipped, and only from level 5.</summary>
    public static readonly ItemDefinition Blade = new()
    {
        Id = "item.weapon.amber_blade",
        Kind = "item",
        Category = ItemCategory.Equipment,
        Rarity = ItemRarity.Rare,
        Slot = EquipmentSlot.Weapon,
        RequiredLevel = 5,
        Stats = new StatBlock { Strength = 9 },
        Binding = ItemBinding.OnEquip,
    };

    public static readonly ItemDefinition Shard = new()
    {
        Id = "item.material.amber_shard",
        Kind = "item",
        Category = ItemCategory.Material,
        Rarity = ItemRarity.Common,
        MaxStack = 50,
    };

    public static readonly ItemDefinition Letter = new()
    {
        Id = "item.quest.sealed_letter",
        Kind = "item",
        Category = ItemCategory.Quest,
        Rarity = ItemRarity.Common,
        Binding = ItemBinding.OnPickup,
    };

    public static readonly ItemDefinition Retired = Shard with
    {
        Id = "item.material.old_shard",
        Retired = true,
    };

    private static readonly ItemDefinition[] All = [Sword, Ring, Blade, Shard, Letter, Retired];

    public static CharacterInventory Inventory(
        int bagSize,
        int maxParcels,
        params ItemInstance[] items
    ) => CharacterInventory.Load(new InventoryLimits(bagSize, maxParcels), Find, items);

    public static CharacterInventory Inventory(params ItemInstance[] items) =>
        Inventory(30, 50, items);

    /// <summary>A written copy (it has an id) in a bag case.</summary>
    public static ItemInstance InBag(ItemDefinition definition, int slot, int quantity = 1) =>
        At(definition, ItemContainer.Bag, slot, quantity);

    public static ItemInstance At(
        ItemDefinition definition,
        ItemContainer container,
        int slot,
        int quantity = 1
    ) =>
        new()
        {
            Id = 100 + ((int)container * 100) + slot,
            DefinitionId = definition.Id,
            Quantity = quantity,
            Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
            Container = container,
            Slot = slot,
        };

    private static ItemDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);
}
