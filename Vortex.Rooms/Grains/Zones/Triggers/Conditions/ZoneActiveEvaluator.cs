using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

/// <summary>The area has not been deactivated by an action.</summary>
public sealed class ZoneActiveEvaluator : ZoneConditionEvaluator<ZoneActiveCondition>
{
    protected override bool Holds(ZoneActiveCondition condition, ZoneTriggerContext ctx) =>
        !ctx.Zone.InactiveAreas.Contains(condition.Zone);
}
