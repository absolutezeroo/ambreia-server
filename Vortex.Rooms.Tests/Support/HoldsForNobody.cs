using Vortex.Primitives.Players;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Tests.Support;

/// <summary>A module's condition that holds only when asked for nobody: proves a spawn's question
/// reaches the declared pieces as <see cref="PlayerId.Invalid"/>.</summary>
internal sealed class HoldsForNobody<TCondition> : ZoneConditionEvaluator<TCondition>
    where TCondition : ZoneCondition
{
    protected override bool Holds(TCondition condition, ZoneRuleContext ctx) =>
        ctx.PlayerId == PlayerId.Invalid && ctx.Facts.Count == 0;
}
