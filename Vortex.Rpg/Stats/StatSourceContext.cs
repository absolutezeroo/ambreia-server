using Vortex.Primitives.Players;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Stats;

/// <summary>
/// What a stat source is asked about: whose stats, of which class (null when the stored class is
/// gone from the content), at which level. Equipment held by the character grain itself must be
/// handed in here by that grain (a field added with the Objets plan): a source calling back into
/// the non-reentrant grain would hang, and reading the table would miss what is not flushed yet.
/// </summary>
public sealed record StatSourceContext(PlayerId PlayerId, ClassDefinition? Class, int Level);
