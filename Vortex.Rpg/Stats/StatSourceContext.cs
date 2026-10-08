using System.Collections.Generic;
using Vortex.Primitives.Players;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Stats;

/// <summary>
/// What a stat source is asked about: whose stats, of which class (null when the stored class is
/// gone from the content), at which level, wearing what. The equipment is handed in by the
/// character grain that holds it: a source calling back into that non-reentrant grain would hang,
/// and reading the table would miss what is not flushed yet.
/// </summary>
public sealed record StatSourceContext(PlayerId PlayerId, ClassDefinition? Class, int Level)
{
    /// <summary>The stats of the items worn and not broken (spec Objets §8).</summary>
    public IReadOnlyList<StatBlock> Equipment { get; init; } = [];
}
