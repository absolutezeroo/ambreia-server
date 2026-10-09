using System.Collections.Generic;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Removes up to <c>amount</c> dispellable statuses — debuffs from an ally, buffs from an
/// enemy — the newest first (spec M1b §5.2).</summary>
public sealed class DispelEffect : ISkillEffect
{
    public string Kind => "dispel";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Amount < 1)
        {
            yield return "needs an amount of 1 or more.";
        }
    }

    /// <summary>Its camp is its target's: a skill that leads with a dispel helps allies.</summary>
    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => false;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect) =>
        ctx.Statuses.Dispel(
            ctx.Zone,
            target,
            buffs: target.IsPlayer != ctx.Caster.IsPlayer,
            effect.Amount
        );
}
