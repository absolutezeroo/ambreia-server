namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// What a status modifier changes (spec M1b §5.3). <see cref="Power"/> stands for the class's main
/// stat: a zone does not know which one it is (plan M1b, decision 1). Values are never renumbered.
/// </summary>
public enum ModifierStat
{
    Power = 1,
    Armor = 2,
    CritPercent = 3,
    DodgePercent = 4,
    SpeedPercent = 5,
    DamageDealtPercent = 6,
    DamageTakenPercent = 7,
}
