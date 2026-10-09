using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Removes up to <c>amount</c> dispellable statuses — debuffs from an ally, buffs from an
/// enemy — the newest first (spec M1b §5.2).</summary>
public sealed class DispelEffect : ISkillEffectRule
{
    public string Kind => "dispel";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Amount < 1)
        {
            yield return "needs an amount of 1 or more.";
        }
    }
}
