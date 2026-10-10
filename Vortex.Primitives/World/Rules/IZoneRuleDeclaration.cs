using System;

namespace Vortex.Primitives.World.Rules;

/// <summary>
/// A condition or action type some module answers at run time (spec Z1 §4.2). The map editor's
/// validator reads these to tell an author which rules have no effect: one nobody declared does
/// nothing in game.
/// </summary>
public interface IZoneRuleDeclaration
{
    /// <summary>The <see cref="ZoneCondition"/> or <see cref="ZoneAction"/> record answered.</summary>
    public Type RuleType { get; }
}
