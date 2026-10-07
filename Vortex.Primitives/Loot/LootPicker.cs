using System;
using System.Collections.Generic;
using Vortex.Primitives.Loot.Snapshots;

namespace Vortex.Primitives.Loot;

/// <summary>
/// Weighted draw over a prize pool. Kept as a pure function (the roll is injected) so the odds are
/// testable and so the outcome is never influenced by anything the client sends.
/// </summary>
public static class LootPicker
{
    /// <summary>
    /// Picks an entry from <paramref name="entries"/> restricted to <paramref name="poolCode"/> and
    /// to entries whose variant is empty (any) or equal to <paramref name="variant"/>. Returns null
    /// when nothing is eligible. <paramref name="roll"/> receives the total weight and must return a
    /// value in <c>[0, total)</c>.
    /// </summary>
    public static LootEntrySnapshot? Pick(
        IReadOnlyList<LootEntrySnapshot> entries,
        string poolCode,
        string variant,
        Func<int, int> roll
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(roll);

        string normalizedVariant = LootVariants.Normalize(variant);

        List<LootEntrySnapshot> eligible = [];
        int total = 0;

        foreach (LootEntrySnapshot entry in entries)
        {
            if (
                !string.Equals(entry.PoolCode, poolCode, StringComparison.Ordinal)
                || entry.Weight <= 0
            )
            {
                continue;
            }

            if (
                entry.Variant.Length > 0
                && !string.Equals(entry.Variant, normalizedVariant, StringComparison.Ordinal)
            )
            {
                continue;
            }

            eligible.Add(entry);
            total += entry.Weight;
        }

        if (total <= 0)
        {
            return null;
        }

        int target = roll(total);

        // A caller handing back an out-of-range roll must not silently fall through the loop and
        // return null — that would read as "empty pool" to the caller and swallow a real bug.
        if (target < 0 || target >= total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roll),
                target,
                $"Roll must fall in [0, {total})."
            );
        }

        foreach (LootEntrySnapshot entry in eligible)
        {
            target -= entry.Weight;

            if (target < 0)
            {
                return entry;
            }
        }

        return eligible[^1];
    }

    /// <summary>Production overload: draws with <see cref="Random.Shared"/>.</summary>
    public static LootEntrySnapshot? Pick(
        IReadOnlyList<LootEntrySnapshot> entries,
        string poolCode,
        string variant
    ) => Pick(entries, poolCode, variant, Random.Shared.Next);
}
