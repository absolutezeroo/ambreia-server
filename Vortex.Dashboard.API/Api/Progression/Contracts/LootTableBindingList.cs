using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>What draws from a pool.</summary>
public sealed record LootTableBindingList(int Count, IReadOnlyList<LootTableBindingRow> Items);
