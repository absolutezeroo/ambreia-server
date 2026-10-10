using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A module's condition that never holds.</summary>
internal sealed class AlwaysFalse<TCondition> : ZoneConditionEvaluator<TCondition>
    where TCondition : ZoneCondition
{
    protected override bool Holds(TCondition condition, ZoneRuleContext ctx) => false;
}
