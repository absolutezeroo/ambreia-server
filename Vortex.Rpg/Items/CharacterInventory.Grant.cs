using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Items;

/// <summary>Grants (spec Objets §7): server side only, whole or nothing.</summary>
public sealed partial class CharacterInventory
{
    /// <summary>
    /// Every line checked first, then placed on a copy that is adopted only if all of it found
    /// room: an existing stack, then an empty case, then a parcel. Past the parcels nothing is
    /// granted — the caller keeps the item (loot stays on the ground).
    /// </summary>
    public GrantPlan Grant(IReadOnlyList<ItemGrantLine> lines)
    {
        if (lines.Count == 0)
        {
            return GrantPlan.Refused(ItemOutcome.InvalidQuantity);
        }

        foreach (ItemGrantLine line in lines)
        {
            if (_definitions(line.DefinitionId) is not { Retired: false } definition)
            {
                return GrantPlan.Refused(ItemOutcome.UnknownItem);
            }

            // More than bag and parcels could ever hold cannot fit; refusing it here also keeps
            // the arithmetic below far from an int overflow.
            long capacity = (long)definition.MaxStack * (_limits.BagSize + _limits.MaxParcels);

            if (line.Quantity < 1 || line.Quantity > capacity)
            {
                return GrantPlan.Refused(ItemOutcome.InvalidQuantity);
            }
        }

        CharacterInventory work = Clone();
        GrantPlan plan = work.Place(lines);

        if (plan.Outcome == ItemOutcome.Ok)
        {
            _items.Clear();
            _items.AddRange(work._items);
        }

        return plan;
    }

    private GrantPlan Place(IReadOnlyList<ItemGrantLine> lines)
    {
        List<ItemQuantity> placed = [];
        int toBag = 0;
        int toParcels = 0;

        foreach (ItemGrantLine line in lines)
        {
            ItemDefinition definition = _definitions(line.DefinitionId)!;
            bool bound = definition.Binding == ItemBinding.OnPickup;
            int left = line.Quantity;

            if (definition.MaxStack > 1)
            {
                List<ItemInstance> stacks =
                [
                    .. Live.Where(i =>
                            i.Container == ItemContainer.Bag
                            && i.DefinitionId == definition.Id
                            && i.Bound == bound
                            && i.Quantity < definition.MaxStack
                        )
                        .OrderBy(i => i.Slot),
                ];

                foreach (ItemInstance stack in stacks)
                {
                    int add = Math.Min(left, definition.MaxStack - stack.Quantity);
                    stack.Quantity += add;
                    Touch(stack);
                    placed.Add(new ItemQuantity(stack, add));
                    toBag += add;
                    left -= add;

                    if (left == 0)
                    {
                        break;
                    }
                }
            }

            while (left > 0 && FirstEmptyBagSlot() is { } slot)
            {
                int add = Math.Min(left, definition.MaxStack);
                placed.Add(
                    new ItemQuantity(Create(definition, add, bound, ItemContainer.Bag, slot), add)
                );
                toBag += add;
                left -= add;
            }

            while (left > 0)
            {
                if (ParcelCount() >= _limits.MaxParcels)
                {
                    return GrantPlan.Refused(ItemOutcome.ParcelsFull);
                }

                int add = Math.Min(left, definition.MaxStack);
                ItemInstance parcel = Create(
                    definition,
                    add,
                    bound,
                    ItemContainer.Parcel,
                    NextParcelSlot()
                );
                placed.Add(new ItemQuantity(parcel, add));
                toParcels += add;
                left -= add;
            }
        }

        return new GrantPlan(ItemOutcome.Ok, toBag, toParcels, placed);
    }

    private ItemInstance Create(
        ItemDefinition definition,
        int quantity,
        bool bound,
        ItemContainer container,
        int slot
    )
    {
        ItemInstance item = new()
        {
            DefinitionId = definition.Id,
            Quantity = quantity,
            Durability = definition.MaxDurability > 0 ? definition.MaxDurability : null,
            Bound = bound,
            Container = container,
            Slot = slot,
            Dirty = true,
        };
        _items.Add(item);

        return item;
    }
}
