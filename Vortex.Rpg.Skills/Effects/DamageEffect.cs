using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Damage <c>base + coefficient × power</c> through the hit chain (spec M1b §5.2).</summary>
public sealed class DamageEffect : ISkillEffect
{
    public string Kind => "damage";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Base <= 0 && effect.Coefficient <= 0)
        {
            yield return "needs a base or a coefficient above 0.";
        }
    }

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => true;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect)
    {
        (int amount, int absorbed, string outcome) = ctx.Hits.Resolve(
            ctx.Caster,
            target,
            ctx.Random,
            new HitOptions
            {
                BaseDamage = effect.Base + effect.Coefficient * ctx.Caster.Power,
                Unavoidable = ctx.Skill.Unavoidable,
            }
        );

        ctx.Zone.Damage(ctx.Caster, target, amount, ctx.Now);
        ctx.DamageDone[target.ObjectId] =
            ctx.DamageDone.GetValueOrDefault(target.ObjectId) + amount;
        ctx.Report(
            target,
            amount,
            absorbed,
            outcome switch
            {
                HitContext.Crit => RpgSkillOutcome.Crit,
                HitContext.Miss => RpgSkillOutcome.Dodge,
                _ => RpgSkillOutcome.Hit,
            }
        );
    }
}
