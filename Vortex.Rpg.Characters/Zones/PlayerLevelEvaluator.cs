using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Characters.Zones;

/// <summary>The character's level is from Min to Max, both included; Max 0 has no cap (spec Z1 §4.4).
/// No character, no level: false.</summary>
public sealed class PlayerLevelEvaluator : ZoneConditionEvaluator<PlayerLevelCondition>
{
    protected override bool Holds(PlayerLevelCondition condition, ZoneRuleContext ctx) =>
        ctx.FactsOf<CharacterZoneFacts>() is { Level: var level }
        && level >= condition.Min
        && (condition.Max <= 0 || level <= condition.Max);
}
