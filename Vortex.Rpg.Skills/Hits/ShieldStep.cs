using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Skills.Statuses;

namespace Vortex.Rpg.Skills.Hits;

/// <summary>A shield takes the damage before the target does (spec M1b §7.2): last, after the
/// minimum, so what it took is what the target would have lost.</summary>
public sealed class ShieldStep : IHitStep
{
    public int Priority => 950;

    public bool Apply(HitContext hit)
    {
        if (hit.Damage > 0 && hit.Target is Combatant target && target.Get<StatusSet>() is { } set)
        {
            hit.Absorbed = set.Absorb(hit.Damage);
            hit.Damage -= hit.Absorbed;
        }

        return true;
    }
}
