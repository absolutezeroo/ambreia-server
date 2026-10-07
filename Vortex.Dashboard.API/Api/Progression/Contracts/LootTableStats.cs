using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>
/// What the pools really paid out over the window, from the loot.awarded audit rows.
/// </summary>
/// <remarks>
/// The half a weights table cannot tell you: a pool can be tuned correctly and still pay out
/// nothing, because the furniture bound to it is unreachable.
/// </remarks>
public sealed record LootTableStats(int Days, int TotalDraws, IReadOnlyList<LootTableDraws> Pools);
