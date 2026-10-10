using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A module's condition that always holds: proves the room asks the declared pieces.</summary>
internal sealed class AlwaysTrue<TCondition> : ZoneConditionEvaluator<TCondition>
    where TCondition : ZoneCondition
{
    protected override bool Holds(TCondition condition, ZoneRuleContext ctx) => true;
}
