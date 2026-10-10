using System;
using Vortex.Primitives.World.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers.Conditions;

/// <summary>
/// Answers one kind of condition the room itself owns (plan D12): it reads the room. A module's
/// condition is a <c>Vortex.Rpg.Zones.Rules.IZoneConditionEvaluator</c> instead (spec Z1 §4.2).
/// </summary>
public interface IRoomConditionEvaluator
{
    public Type ConditionType { get; }

    public bool Holds(ZoneCondition condition, ZoneTriggerContext ctx);
}
