namespace Vortex.Rpg.Combat.Hits;

/// <summary>What the hit chain reads of a fighter (spec M1a §6.2).</summary>
public interface ICombatantStats
{
    public bool IsPlayer { get; }

    /// <summary>A player's primary stat: his damage base.</summary>
    public int Power { get; }

    /// <summary>A monster's damage roll.</summary>
    public int DamageMin { get; }

    public int DamageMax { get; }

    public int Armor { get; }

    public double CritPercent { get; }

    public double DodgePercent { get; }
}
