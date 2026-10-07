using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>Everything the pools can hand out.</summary>
public sealed record LootTableEntryList(int Count, IReadOnlyList<LootTableEntryRow> Items);
