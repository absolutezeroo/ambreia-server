using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Progression.Contracts;

/// <summary>One pool's draws over the window, broken down two ways.</summary>
public sealed record LootTableDraws(
    string Pool,
    int Draws,
    IReadOnlyList<LootEntryDraws> Entries,
    IReadOnlyList<LootSourceDraws> Sources
);
