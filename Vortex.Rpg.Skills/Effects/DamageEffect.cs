using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Damage <c>base + coefficient × power</c> through the hit chain (spec M1b §5.2).</summary>
public sealed class DamageEffect : ISkillEffectRule
{
    public string Kind => "damage";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Base <= 0 && effect.Coefficient <= 0)
        {
            yield return "needs a base or a coefficient above 0.";
        }
    }
}
