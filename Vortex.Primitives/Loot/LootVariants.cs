using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Vortex.Primitives.Loot;

/// <summary>
/// Variant handling for prize pools. A variant narrows an entry to one flavour of the furniture that
/// draws it — the key colour for a mystery box, and whatever a future pool decides to key on.
/// </summary>
public static class LootVariants
{
    /// <summary>Trims and lowercases a variant; null or blank becomes empty, meaning "any".</summary>
    public static string Normalize(string? variant) =>
        string.IsNullOrWhiteSpace(variant) ? string.Empty : variant.Trim().ToLowerInvariant();

    /// <summary>Splits a pool's comma-separated variant list. Empty means the pool is free-form.</summary>
    public static ImmutableArray<string> ParseSet(string? variants)
    {
        if (string.IsNullOrWhiteSpace(variants))
        {
            return [];
        }

        List<string> parsed = [];

        foreach (string part in variants.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = Normalize(part);

            if (normalized.Length > 0 && !parsed.Contains(normalized, StringComparer.Ordinal))
            {
                parsed.Add(normalized);
            }
        }

        return [.. parsed];
    }

    /// <summary>
    /// Normalizes an entry's stored variant against the pool's declared set. A variant outside the
    /// set is widened to "any" rather than kept: keeping it would make the entry match nothing and
    /// sit in the pool forever without ever being drawn, which reads as a data bug nobody notices.
    /// </summary>
    public static string NormalizeForSet(string? variant, ImmutableArray<string> set)
    {
        string normalized = Normalize(variant);

        if (normalized.Length == 0 || set.IsDefaultOrEmpty)
        {
            return normalized;
        }

        return set.Contains(normalized, StringComparer.Ordinal) ? normalized : string.Empty;
    }
}
