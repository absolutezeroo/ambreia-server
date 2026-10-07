using System;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

/// <summary>Answers one kind of condition (plan D12).</summary>
public interface IZoneConditionEvaluator
{
    public Type ConditionType { get; }

    public bool Holds(ZoneCondition condition, ZoneTriggerContext ctx);
}
