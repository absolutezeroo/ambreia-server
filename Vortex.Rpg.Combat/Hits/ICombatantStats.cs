namespace Vortex.Rpg.Combat.Hits;

/// <summary>What the hit chain reads of a fighter (spec M1a §6.2).</summary>
public interface ICombatantStats
{
    /// <summary>The fighter's room object id: a step (a shield, a buff, M1b) finds its state by it.</summary>
    public int ObjectId { get; }

    public bool IsPlayer { get; }

    /// <summary>A player's primary stat: his damage base.</summary>
    public int Power { get; }

    /// <summary>A monster's damage roll.</summary>
    public int DamageMin { get; }

    public int DamageMax { get; }

    public int Armor { get; }

    public double CritPercent { get; }

    public double DodgePercent { get; }

    /// <summary>Damage it deals, in percent more (or less) (spec M1b §7.3); 0 without modifier.</summary>
    public double DamageDealtPercent => 0;

    /// <summary>Damage it takes, in percent more (or less).</summary>
    public double DamageTakenPercent => 0;
}
