using Microsoft.Extensions.Options;
using Vortex.Rpg.Combat.Configuration;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>The attacker crits by its crit chance: damage × <see cref="CombatConfig.CritMultiplier"/>.</summary>
public sealed class CritStep(IOptions<CombatConfig> config) : IHitStep
{
    private readonly double _multiplier = config.Value.CritMultiplier;

    public int Priority => 300;

    public bool Apply(HitContext hit)
    {
        if (hit.Random.NextDouble() * 100 < hit.Attacker.CritPercent)
        {
            hit.Damage *= _multiplier;
            hit.Outcome = HitContext.Crit;
        }

        return true;
    }
}
