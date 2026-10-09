using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>Stops the target's cast or channel (spec M1b §5.2, §6.4).</summary>
public sealed class InterruptEffect : ISkillEffectRule
{
    public string Kind => "interrupt";

    public IEnumerable<string> Problems(SkillEffectDefinition effect) => [];
}
