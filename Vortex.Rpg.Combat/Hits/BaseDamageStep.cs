using System;
using Microsoft.Extensions.Options;
using Vortex.Rpg.Combat.Configuration;

namespace Vortex.Rpg.Combat.Hits;

/// <summary>A player hits for his Power times a roll; a monster for a roll between its min and max.</summary>
public sealed class BaseDamageStep(IOptions<CombatConfig> config) : IHitStep
{
    private readonly CombatConfig _config = config.Value;

    public int Priority => 200;

    public bool Apply(HitContext hit)
    {
        ICombatantStats a = hit.Attacker;

        // A skill or a status brings its own amount (spec M1b §5.2).
        if (hit.Options.BaseDamage is { } given)
        {
            hit.Damage = given;

            return true;
        }

        hit.Damage = a.IsPlayer
            ? a.Power
                * (
                    _config.PlayerDamageMinFactor
                    + hit.Random.NextDouble()
                        * (_config.PlayerDamageMaxFactor - _config.PlayerDamageMinFactor)
                )
            : hit.Random.Next(a.DamageMin, Math.Max(a.DamageMin, a.DamageMax) + 1);

        return true;
    }
}
