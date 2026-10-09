using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>A heal of <c>base + coefficient × power</c>, which may crit (spec M1b §5.2).</summary>
public sealed class HealEffect : ISkillEffectRule
{
    public string Kind => "heal";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Base <= 0 && effect.Coefficient <= 0)
        {
            yield return "needs a base or a coefficient above 0.";
        }
    }
}
