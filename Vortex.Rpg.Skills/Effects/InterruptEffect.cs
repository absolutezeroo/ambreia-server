using System.Collections.Generic;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Stops the target's cast or channel (spec M1b §5.2, §6.4). A monster casts nothing yet
/// (M1c): on one, nothing happens.</summary>
public sealed class InterruptEffect : ISkillEffect
{
    public string Kind => "interrupt";

    public IEnumerable<string> Problems(SkillEffectDefinition effect) => [];

    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content) => true;

    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect) =>
        ctx.Zone.Interrupt(target);
}
