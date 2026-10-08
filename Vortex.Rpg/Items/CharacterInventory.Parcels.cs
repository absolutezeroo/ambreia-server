using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.Rpg.Items;
using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Rpg.Items;

/// <summary>Parcels (spec Objets §2): what a full bag could not take, kept until claimed.</summary>
public sealed partial class CharacterInventory
{
    /// <summary>The parcel goes into the bag whole — the room left in its stacks, then one empty
    /// case for the rest — or not at all.</summary>
    public ItemOutcome ClaimParcel(int parcel)
    {
        if (At(ItemContainer.Parcel, parcel) is not { } item)
        {
            return ItemOutcome.NotFound;
        }

        int maxStack = Definition(item)?.MaxStack ?? 1;
        List<ItemInstance> stacks =
            maxStack > 1
                ?
                [
                    .. Live.Where(i =>
                            i.Container == ItemContainer.Bag
                            && Stacks(i, item)
                            && i.Quantity < maxStack
                        )
                        .OrderBy(i => i.Slot),
                ]
                : [];
        int room = stacks.Sum(s => maxStack - s.Quantity);
        int? empty = FirstEmptyBagSlot();

        if (room < item.Quantity && empty is null)
        {
            return ItemOutcome.BagFull;
        }

        int left = item.Quantity;

        foreach (ItemInstance stack in stacks)
        {
            int add = Math.Min(left, maxStack - stack.Quantity);
            stack.Quantity += add;
            Touch(stack);
            left -= add;

            if (left == 0)
            {
                break;
            }
        }

        if (left == 0)
        {
            Delete(item);
        }
        else
        {
            item.Quantity = left;
            item.Container = ItemContainer.Bag;
            item.Slot = empty!.Value;
            Touch(item);
        }

        return ItemOutcome.Ok;
    }

    private int ParcelCount() => Live.Count(i => i.Container == ItemContainer.Parcel);

    private int NextParcelSlot() =>
        Live.Where(i => i.Container == ItemContainer.Parcel)
            .Select(i => i.Slot)
            .DefaultIfEmpty(-1)
            .Max() + 1;
}
