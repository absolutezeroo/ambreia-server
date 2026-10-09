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

    public HitOptions Options { get; init; } = HitOptions.None;

    public double Damage { get; set; }

    /// <summary>Taken by a shield instead of the target (spec M1b §7.2).</summary>
    public double Absorbed { get; set; }

    public string Outcome { get; set; } = Hit;
}
