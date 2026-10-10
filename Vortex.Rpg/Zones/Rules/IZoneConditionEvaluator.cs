using Vortex.Primitives.World.Rules;

namespace Vortex.Rpg.Zones.Rules;

/// <summary>
/// Answers one kind of zone condition, declared by the module that owns what it reads
/// (<see cref="ZoneRuleServices.AddZoneCondition{T}"/>). Synchronous: it runs on every step a
/// restricted area or a passage checks.
/// </summary>
public interface IZoneConditionEvaluator : IZoneRuleDeclaration
{
    public bool Holds(ZoneCondition condition, ZoneRuleContext ctx);
}
