using System;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rpg.Characters.Zones;

/// <summary>The character owns at least Count of the item, bag and equipment; a Count below one
/// asks for one (spec Z1 §4.4). No character, nothing owned: false.</summary>
public sealed class HasItemEvaluator : ZoneConditionEvaluator<HasItemCondition>
{
    protected override bool Holds(HasItemCondition condition, ZoneRuleContext ctx) =>
        ctx.FactsOf<CharacterZoneFacts>() is { } facts
        && facts.Items.TryGetValue(condition.Item, out int owned)
        && owned >= Math.Max(1, condition.Count);
}
