using System.Collections.Generic;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Propose de ranimer un allié KO (M1d §2.3) : le joueur accepte ou refuse, rien ne le
/// relève ici.</summary>
public sealed class ResurrectEffect : ISkillEffect
{
    public string Kind => "resurrect";

    public bool TargetsDead => true;

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.HealthPercent is < 1 or > 100)
        {
            yield return "needs a healthPercent between 1 and 100.";
        }
    }

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => false;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect)
    {
        if (!target.IsPlayer || !target.IsGhost)
        {
            return;
        }

        ctx.Zone.Offer(target, ctx.Caster.ObjectId, effect.HealthPercent);
    }
}
