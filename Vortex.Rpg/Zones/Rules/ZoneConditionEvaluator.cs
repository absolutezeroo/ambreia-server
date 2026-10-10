using System;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

public abstract class ZoneConditionEvaluator<TCondition> : IZoneConditionEvaluator
    where TCondition : ZoneCondition
{
    public Type RuleType => typeof(TCondition);

    public bool Holds(ZoneCondition condition, ZoneRuleContext ctx) =>
        Holds((TCondition)condition, ctx);

    protected abstract bool Holds(TCondition condition, ZoneRuleContext ctx);
}
