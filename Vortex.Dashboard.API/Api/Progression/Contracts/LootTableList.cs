using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>The pools themselves.</summary>
public sealed record LootTableList(int Count, IReadOnlyList<LootTableRow> Items);
