using Vortex.Primitives.Players;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Stats;

/// <summary>What a stat source is asked about: whose stats, of which class (null when the stored
/// class is gone from the content), at which level.</summary>
public sealed record StatSourceContext(PlayerId PlayerId, ClassDefinition? Class, int Level);
