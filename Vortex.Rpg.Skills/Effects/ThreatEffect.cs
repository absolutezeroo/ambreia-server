using System.Collections.Generic;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Extra threat: a percent of this skill's damage on the target (spec M1b §5.2, §8).</summary>
public sealed class ThreatEffect : ISkillEffect
{
    public string Kind => "threat";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.ThreatPercent < 1)
        {
            yield return "needs a threatPercent of 1 or more.";
        }
    }

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => true;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect) =>
        ctx.Zone.AddThreat(
            target,
            ctx.Caster.ObjectId,
            (long)ctx.DamageDone.GetValueOrDefault(target.ObjectId) * effect.ThreatPercent / 100
        );
}
