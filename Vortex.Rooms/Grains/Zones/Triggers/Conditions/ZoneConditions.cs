using System;
using System.Collections.Generic;
using System.Linq;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

/// <summary>
/// Every condition must hold. The RPG ones (level, quest, item, faction) have no evaluator until
/// their systems exist, and an unanswerable condition is false: content gated on them stays shut.
/// </summary>
public static class ZoneConditions
{
    private static readonly Dictionary<Type, IZoneConditionEvaluator> Evaluators =
        new IZoneConditionEvaluator[]
        {
            new TimeOfDayEvaluator(),
            new ZoneActiveEvaluator(),
        }.ToDictionary(e => e.ConditionType);

    public static bool AllHold(IEnumerable<ZoneCondition> conditions, ZoneTriggerContext ctx) =>
        conditions.All(c =>
            Evaluators.TryGetValue(c.GetType(), out IZoneConditionEvaluator? evaluator)
            && evaluator.Holds(c, ctx)
        );
}
