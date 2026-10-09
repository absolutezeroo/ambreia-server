using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Extra threat: a percent of this skill's damage on the target (spec M1b §5.2, §8).</summary>
public sealed class ThreatEffect : ISkillEffectRule
{
    public string Kind => "threat";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.ThreatPercent < 1)
        {
            yield return "needs a threatPercent of 1 or more.";
        }
    }
}
