using System;
using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Statuses;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>A heal of <c>base + coefficient × power</c>, which may crit (spec M1b §5.2); its threat
/// spreads to whoever fights the healed (§8).</summary>
public sealed class HealEffect : ISkillEffect
{
    public string Kind => "heal";

    public IEnumerable<string> Problems(SkillEffectDefinition effect)
    {
        if (effect.Base <= 0 && effect.Coefficient <= 0)
        {
            yield return "needs a base or a coefficient above 0.";
        }
    }

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => false;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect)
    {
        double amount = effect.Base + effect.Coefficient * ctx.Caster.Power;

        if (ctx.Random.NextDouble() * 100 < ctx.Caster.CritPercent)
        {
            amount *= ctx.Zone.Config.CritMultiplier;
        }

        int healed = ctx.Zone.Heal(target, (int)Math.Round(amount, MidpointRounding.AwayFromZero));
        HealThreat.Spread(ctx.Zone, ctx.Caster, target, healed, ctx.Config);
        ctx.Report(target, healed, 0, RpgSkillOutcome.Heal);
    }
}
