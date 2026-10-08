using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Items;

/// <summary>
/// The items of one character (spec Objets §5, §7): its bag, its equipment, its parcels. Pure — no
/// grain, no database: the character grain holds one, writes what <see cref="Dirty"/> lists, then
/// calls <see cref="MarkWritten"/>. Every operation checks before it changes anything, so a refusal
/// leaves the inventory exactly as it was. Its parts: the bag (<c>.Bag.cs</c>), grants
/// (<c>.Grant.cs</c>), parcels (<c>.Parcels.cs</c>), equipment (<c>.Equipment.cs</c>).
/// </summary>
public sealed partial class CharacterInventory
{
    private readonly InventoryLimits _limits;
    private readonly Func<string, ItemDefinition?> _definitions;
    private readonly List<ItemInstance> _items;

    private CharacterInventory(
        InventoryLimits limits,
        Func<string, ItemDefinition?> definitions,
        List<ItemInstance> items
    )
    {
        _limits = limits;
        _definitions = definitions;
        _items = items;
    }

    public static CharacterInventory Load(
        InventoryLimits limits,
        Func<string, ItemDefinition?> definitions,
        IEnumerable<ItemInstance> items
    ) => new(limits, definitions, [.. items]);

    /// <summary>The copies held; deleted ones wait in the list for the next write.</summary>
    public IEnumerable<ItemInstance> Live => _items.Where(i => !i.Deleted);

    /// <summary>What changed since the last write, deleted copies included.</summary>
    public IReadOnlyList<ItemInstance> Dirty => [.. _items.Where(i => i.Dirty)];

    /// <summary>A deep copy: what must be written before it counts (a grant) is applied to one,
    /// which replaces the original once written.</summary>
    public CharacterInventory Clone() =>
        new(_limits, _definitions, [.. _items.Select(i => i.Copy())]);

    /// <summary>The dirty copies were written: deleted ones leave, the rest are clean.</summary>
    public void MarkWritten()
    {
        _items.RemoveAll(i => i.Deleted);

        foreach (ItemInstance item in _items)
        {
            item.Dirty = false;
        }
    }

    public ItemInstance? At(ItemContainer container, int slot) =>
        Live.FirstOrDefault(i => i.Container == container && i.Slot == slot);

    public CharacterItemSnapshot[] Snapshot() =>
        [
            .. Live.OrderBy(i => i.Container)
                .ThenBy(i => i.Slot)
                .Select(i => new CharacterItemSnapshot
                {
                    ItemId = i.Id,
                    DefinitionId = i.DefinitionId,
                    Quantity = i.Quantity,
                    Durability = i.Durability,
                    Bound = i.Bound,
                    Container = i.Container,
                    Slot = i.Slot,
                }),
        ];

    private ItemDefinition? Definition(ItemInstance item) => _definitions(item.DefinitionId);

    private bool InBag(int slot) => slot >= 0 && slot < _limits.BagSize;

    private int? FirstEmptyBagSlot()
    {
        for (int slot = 0; slot < _limits.BagSize; slot++)
        {
            if (At(ItemContainer.Bag, slot) is null)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>Two copies of one stackable item merge only when both are bound or both free.</summary>
    private static bool Stacks(ItemInstance a, ItemInstance b) =>
        a.DefinitionId == b.DefinitionId && a.Bound == b.Bound;

    private static void Touch(ItemInstance item) => item.Dirty = true;

    private static void Delete(ItemInstance item)
    {
        item.Deleted = true;
        item.Dirty = true;
    }
}
