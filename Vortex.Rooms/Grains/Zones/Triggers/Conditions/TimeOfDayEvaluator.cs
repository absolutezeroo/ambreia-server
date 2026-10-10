using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

/// <summary>From (included) to To (excluded), server hours; From after To spans midnight; equal is all day.</summary>
public sealed class TimeOfDayEvaluator : RoomConditionEvaluator<TimeOfDayCondition>
{
    protected override bool Holds(TimeOfDayCondition condition, ZoneTriggerContext ctx)
    {
        int hour = ctx.LocalNow.Hour;

        return condition.From < condition.To
            ? hour >= condition.From && hour < condition.To
            : condition.From == condition.To || hour >= condition.From || hour < condition.To;
    }
}
