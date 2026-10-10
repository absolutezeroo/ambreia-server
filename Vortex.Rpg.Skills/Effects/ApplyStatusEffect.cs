using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Statuses;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Puts a status on the target (spec M1b §5.2, §7).</summary>
public sealed class ApplyStatusEffect : ISkillEffect
{
    public string Kind => "apply_status";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (string.IsNullOrEmpty(effect.Status))
        {
            yield return "needs a status.";
        }
    }

    /// <summary>A debuff, a damage over time, a stun or a slow.</summary>
    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) =>
        effect.Status is { } id
        && content.Get<StatusDefinition>(id) is { } status
        && !StatusApplier.IsBuff(status);

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect)
    {
        if (
            effect.Status is not { } id
            || ctx.Content.Get<StatusDefinition>(id) is not { Retired: false } status
        )
        {
            ctx.Effects.WarnMissing(effect.Status ?? Kind);

            return;
        }

        // Turned away by the target's rank: shown as such (spec M1c §7.1).
        if (StatusApplier.IsImmune(target, status))
        {
            ctx.Report(target, 0, 0, RpgSkillOutcome.Immune);

            return;
        }

        ctx.Statuses.Apply(ctx.Zone, ctx.Caster, target, status, ctx.Now);
    }
}
