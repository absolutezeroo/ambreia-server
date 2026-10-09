namespace Vortex.Rpg.Combat.Stats;

/// <summary>A fighter's stat a modifier may change (spec M1b §7.3). Not persisted.</summary>
public enum CombatStat
{
    Power = 1,
    Armor = 2,
    CritPercent = 3,
    DodgePercent = 4,
    SpeedPercent = 5,
    DamageDealtPercent = 6,
    DamageTakenPercent = 7,
}
