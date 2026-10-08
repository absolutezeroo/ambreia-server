using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Rpg.Items;

/// <summary>The bag: moving, splitting, destroying, and the server's removal.</summary>
public sealed partial class CharacterInventory
{
    /// <summary>To an empty case it moves; onto the same stackable item it merges up to the stack
    /// size and the rest stays; onto anything else the two swap.</summary>
    public ItemOutcome Move(int from, int to)
    {
        if (!InBag(from) || !InBag(to) || At(ItemContainer.Bag, from) is not { } source)
        {
            return ItemOutcome.NotFound;
        }

        if (from == to)
        {
            return ItemOutcome.Ok;
        }

        ItemInstance? target = At(ItemContainer.Bag, to);

        if (target is null)
        {
            source.Slot = to;
            Touch(source);

            return ItemOutcome.Ok;
        }

        if (Definition(source) is { MaxStack: > 1 } definition && Stacks(source, target))
        {
            int moved = Math.Min(definition.MaxStack - target.Quantity, source.Quantity);

            if (moved > 0)
            {
                target.Quantity += moved;
                Touch(target);
                source.Quantity -= moved;

                if (source.Quantity == 0)
                {
                    Delete(source);
                }
                else
                {
                    Touch(source);
                }
            }

            return ItemOutcome.Ok;
        }

        source.Slot = to;
        target.Slot = from;
        Touch(source);
        Touch(target);

        return ItemOutcome.Ok;
    }

    /// <summary>Part of a stack to an empty case: more than none, less than all of it.</summary>
    public ItemOutcome Split(int from, int quantity, int to)
    {
        if (!InBag(from) || !InBag(to) || At(ItemContainer.Bag, from) is not { } source)
        {
            return ItemOutcome.NotFound;
        }

        if (quantity <= 0 || quantity >= source.Quantity)
        {
            return ItemOutcome.InvalidQuantity;
        }

        if (At(ItemContainer.Bag, to) is not null)
        {
            return ItemOutcome.SlotOccupied;
        }

        ItemInstance part = source.Copy();
        part.Id = 0;
        part.Quantity = quantity;
        part.Slot = to;
        Touch(part);
        _items.Add(part);

        source.Quantity -= quantity;
        Touch(source);

        return ItemOutcome.Ok;
    }

    public ItemOutcome Destroy(int slot, int quantity)
    {
        if (!InBag(slot) || At(ItemContainer.Bag, slot) is not { } item)
        {
            return ItemOutcome.NotFound;
        }

        if (quantity <= 0 || quantity > item.Quantity)
        {
            return ItemOutcome.InvalidQuantity;
        }

        if (quantity == item.Quantity)
        {
            Delete(item);
        }
        else
        {
            item.Quantity -= quantity;
            Touch(item);
        }

        return ItemOutcome.Ok;
    }

    /// <summary>Server side (quests, crafting): from the bag's stacks in case order, all or nothing.</summary>
    public (ItemOutcome Outcome, IReadOnlyList<ItemQuantity> Taken) Remove(
        string definitionId,
        int quantity
    )
    {
        if (quantity <= 0)
        {
            return (ItemOutcome.InvalidQuantity, []);
        }

        List<ItemInstance> stacks =
        [
            .. Live.Where(i => i.Container == ItemContainer.Bag && i.DefinitionId == definitionId)
                .OrderBy(i => i.Slot),
        ];

        if (stacks.Sum(i => (long)i.Quantity) < quantity)
        {
            return (ItemOutcome.NotFound, []);
        }

        List<ItemQuantity> taken = [];
        int left = quantity;

        foreach (ItemInstance stack in stacks)
        {
            int take = Math.Min(left, stack.Quantity);
            taken.Add(new ItemQuantity(stack, take));
            stack.Quantity -= take;
            left -= take;

            if (stack.Quantity == 0)
            {
                Delete(stack);
            }
            else
            {
                Touch(stack);
            }

            if (left == 0)
            {
                break;
            }
        }

        return (ItemOutcome.Ok, taken);
    }
}
