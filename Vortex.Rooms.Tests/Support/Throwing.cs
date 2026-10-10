using System;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A module's condition that is broken: it throws.</summary>
internal sealed class Throwing<TCondition> : ZoneConditionEvaluator<TCondition>
    where TCondition : ZoneCondition
{
    protected override bool Holds(TCondition condition, ZoneRuleContext ctx) =>
        throw new InvalidOperationException("broken piece");
}
