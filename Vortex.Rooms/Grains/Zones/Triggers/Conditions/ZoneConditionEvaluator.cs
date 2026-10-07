using System;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

public abstract class ZoneConditionEvaluator<TCondition> : IZoneConditionEvaluator
    where TCondition : ZoneCondition
{
    public Type ConditionType => typeof(TCondition);

    public bool Holds(ZoneCondition condition, ZoneTriggerContext ctx) =>
        Holds((TCondition)condition, ctx);

    protected abstract bool Holds(TCondition condition, ZoneTriggerContext ctx);
}
