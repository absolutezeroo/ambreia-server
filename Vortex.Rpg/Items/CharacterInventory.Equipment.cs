using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Items;

/// <summary>Equipment (spec Objets §7, §8): ten slots, worn items swapped back to the case they
/// came from, rings in ring 1 then ring 2.</summary>
public sealed partial class CharacterInventory
{
    /// <summary>The equipment in <paramref name="bagSlot"/> to its slot, for a character of
    /// <paramref name="classId"/> at <paramref name="level"/>; what was worn there comes back to
    /// that case. An item that binds on equip is bound. A copy at durability 0 is worn, and gives
    /// no stats.</summary>
    public ItemOutcome Equip(int bagSlot, string classId, int level)
    {
        if (!InBag(bagSlot) || At(ItemContainer.Bag, bagSlot) is not { } item)
        {
            return ItemOutcome.NotFound;
        }

        if (Definition(item) is not { Category: ItemCategory.Equipment, Slot: { } slot } definition)
        {
            return ItemOutcome.NotEquipment;
        }

        if (level < definition.RequiredLevel)
        {
            return ItemOutcome.LevelTooLow;
        }

        if (definition.AllowedClasses.Count > 0 && !definition.AllowedClasses.Contains(classId))
        {
            return ItemOutcome.WrongClass;
        }

        int target = slot == EquipmentSlot.Ring ? RingSlot() : (int)slot;
        ItemInstance? worn = At(ItemContainer.Equipment, target);

        item.Container = ItemContainer.Equipment;
        item.Slot = target;
        item.Bound |= definition.Binding == ItemBinding.OnEquip;
        Touch(item);

        if (worn is not null)
        {
            worn.Container = ItemContainer.Bag;
            worn.Slot = bagSlot;
            Touch(worn);
        }

        return ItemOutcome.Ok;
    }

    public ItemOutcome Unequip(int equipmentSlot)
    {
        if (
            !EquipmentSlots.IsValid(equipmentSlot)
            || At(ItemContainer.Equipment, equipmentSlot) is not { } item
        )
        {
            return ItemOutcome.NotFound;
        }

        if (FirstEmptyBagSlot() is not { } slot)
        {
            return ItemOutcome.BagFull;
        }

        item.Container = ItemContainer.Bag;
        item.Slot = slot;
        Touch(item);

        return ItemOutcome.Ok;
    }

    /// <summary>What the worn items add (spec Objets §8): nothing from a copy at durability 0, nor
    /// from a definition the content no longer has.</summary>
    public IReadOnlyList<StatBlock> EquippedStats() =>
        [
            .. Live.Where(i => i.Container == ItemContainer.Equipment)
                .Select(i => (Item: i, Definition: Definition(i)))
                .Where(x =>
                    x.Definition?.Stats is not null
                    && (x.Definition.MaxDurability == 0 || x.Item.Durability > 0)
                )
                .Select(x => x.Definition!.Stats!),
        ];

    /// <summary>Ring 1 if free, else ring 2 if free, else ring 1, whose ring is swapped out.</summary>
    private int RingSlot() =>
        At(ItemContainer.Equipment, EquipmentSlots.Ring1) is null ? EquipmentSlots.Ring1
        : At(ItemContainer.Equipment, EquipmentSlots.Ring2) is null ? EquipmentSlots.Ring2
        : EquipmentSlots.Ring1;
}
