using System;
using System.Linq;
using Vortex.Primitives.Rpg.Items.Enums;

namespace Vortex.Rpg.Items;

public sealed partial class CharacterInventory
{
    /// <summary>
    /// Dying wears the equipment (spec M1a §8.1): each worn copy that can wear out loses
    /// <paramref name="percent"/> % of its definition's max durability (at least 1), down to 0.
    /// Answers whether a copy changed (the bag must be shown again) and whether one reached 0 now
    /// (its stats are gone until it is repaired).
    /// </summary>
    public (bool Changed, bool Broke) WearEquipped(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        bool changed = false;
        bool broke = false;

        foreach (ItemInstance item in Live.Where(i => i.Container == ItemContainer.Equipment))
        {
            if (
                percent == 0
                || item.Durability is not { } durability
                || durability == 0
                || Definition(item) is not { MaxDurability: > 0 } definition
            )
            {
                continue;
            }

            int loss = Math.Max(1, definition.MaxDurability * percent / 100);
            item.Durability = Math.Max(0, durability - loss);
            Touch(item);
            changed = true;
            broke |= item.Durability == 0;
        }

        return (changed, broke);
    }
}
