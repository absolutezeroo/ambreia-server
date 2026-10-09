using System;
using Vortex.Rpg.Combat.Hits;

namespace Vortex.Rpg.Skills.Hits;

/// <summary>Damage dealt and taken in percent, from buffs and debuffs (spec M1b §7.3): after the crit,
/// before the armor.</summary>
public sealed class DamageModifierStep : IHitStep
{
    public int Priority => 350;

    public bool Apply(HitContext hit)
    {
        hit.Damage = Math.Max(
            0,
            hit.Damage
                * (1 + hit.Attacker.DamageDealtPercent / 100)
                * (1 + hit.Target.DamageTakenPercent / 100)
        );

        return true;
    }
}
