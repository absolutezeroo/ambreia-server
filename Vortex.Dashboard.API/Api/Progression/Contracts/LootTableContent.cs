using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// The prize pools, what is in them, and what draws from them.
/// </summary>
/// <param name="ProductTypes">The four kinds an entry may be, so the editor offers the real
/// vocabulary rather than a free string.</param>
public sealed record LootTableContent(
    LootTableList Pools,
    LootTableEntryList Entries,
    IReadOnlyList<LootTableWeightTotal> Totals,
    LootTableBindingList Bindings,
    IReadOnlyList<string> ProductTypes
);
