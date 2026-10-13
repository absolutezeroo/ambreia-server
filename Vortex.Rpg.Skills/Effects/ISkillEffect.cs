using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Content.Rules;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>
/// An effect type of skills (spec M1b §5.2), declared by DI under its <see cref="ISkillEffectRule.Kind"/>
/// by the module that runs it: the import checks its fields, the zone applies it. A module adds an
/// effect ("push") without touching the zone or the core.
/// </summary>
public interface ISkillEffect : ISkillEffectRule
{
    /// <summary>On one target, still alive — or KO, for an effect that says so.</summary>
    public void Apply(SkillContext ctx, Combatant target, SkillEffectDefinition effect);

    /// <summary>Vrai pour un effet qui vise un mort (une résurrection, M1d §2.3).</summary>
    public bool TargetsDead => false;

    /// <summary>Hostile to its target: an area of a skill whose first effect is offensive touches
    /// enemies, else allies; an offensive skill never leaves or reaches a safe cell.</summary>
    public bool IsOffensive(SkillEffectDefinition effect, IRpgContent content);
}
