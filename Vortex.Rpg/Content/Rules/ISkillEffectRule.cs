using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Content.Rules;

/// <summary>
/// What an effect type of a skill requires of its fields (spec M1b §5.2, §5.4). Declared by DI by the
/// module that runs the effect, so the import refuses a pack the zone could not play, and the core
/// names no effect.
/// </summary>
public interface ISkillEffectRule
{
    /// <summary>The <see cref="SkillEffectDefinition.Kind"/> it answers to.</summary>
    public string Kind { get; }

    /// <summary>One sentence per field missing or wrong; empty when the effect is valid.</summary>
    public IEnumerable<string> Problems(SkillEffectDefinition effect);
}
