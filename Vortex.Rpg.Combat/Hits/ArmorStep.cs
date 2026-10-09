using System;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>The target's armor: damage × 100 / (100 + armor) (spec §5.2).</summary>
public sealed class ArmorStep : IHitStep
{
    public int Priority => 400;

    public bool Apply(HitContext hit)
    {
        hit.Damage = hit.Damage * 100 / (100 + Math.Max(0, hit.Target.Armor));

        return true;
    }
}
