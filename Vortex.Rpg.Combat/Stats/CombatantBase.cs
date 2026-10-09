namespace Vortex.Rpg.Combat.Stats;

/// <summary>A fighter's stats before any modifier: a player's from his character, a monster's from
/// its definition at its level (spec M1b §7.3).</summary>
public sealed record CombatantBase
{
    public int Power { get; init; }

    public int Armor { get; init; }

    public double CritPercent { get; init; }

    public double DodgePercent { get; init; }

    public int SpeedPercent { get; init; } = 100;
}
