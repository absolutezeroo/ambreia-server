using System;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>One hit on its way through the steps (spec M1a §6.2): each step reads and changes it.</summary>
public sealed class HitContext
{
    public const string Hit = "hit";
    public const string Crit = "crit";
    public const string Miss = "miss";

    public required ICombatantStats Attacker { get; init; }

    public required ICombatantStats Target { get; init; }

    public required Random Random { get; init; }

    public double Damage { get; set; }

    public string Outcome { get; set; } = Hit;
}
