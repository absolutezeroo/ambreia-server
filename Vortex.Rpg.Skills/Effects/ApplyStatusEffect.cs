using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Puts a status on the target (spec M1b §5.2, §7).</summary>
public sealed class ApplyStatusEffect : ISkillEffectRule
{
    public string Kind => "apply_status";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (string.IsNullOrEmpty(effect.Status))
        {
            yield return "needs a status.";
        }
    }
}
