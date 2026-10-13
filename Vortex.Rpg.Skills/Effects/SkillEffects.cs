using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>
/// The effect types every module declared, by kind (the last one declared wins), applied in the
/// order of a skill. A kind or a status that is not there when a skill lands (a pack replaced since)
/// is skipped with one warning per id (spec M1b §12).
/// </summary>
public sealed class SkillEffects
{
    private readonly Dictionary<string, ISkillEffect> _effects = new(StringComparer.Ordinal);

    /// <summary>Shared by every zone, which tick at the same time: a concurrent set.</summary>
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
    private readonly ILogger<SkillEffects> _logger;

    public SkillEffects(IEnumerable<ISkillEffect> effects, ILogger<SkillEffects> logger)
    {
        _logger = logger;

        foreach (ISkillEffect effect in effects)
        {
            _effects[effect.Kind] = effect;
        }
    }

    /// <summary>The ids already warned about.</summary>
    public IReadOnlyCollection<string> Warned => [.. _warned.Keys];

    public ISkillEffect? Find(string kind) => _effects.GetValueOrDefault(kind);

    /// <summary>The skill's camp: its first effect's (spec M1b §6.6).</summary>
    public bool IsOffensive(SkillDefinition skill, IRpgContent content) =>
        skill.Effects.FirstOrDefault() is { } first
        && Find(first.Kind) is { } effect
        && effect.IsOffensive(first, content);

    /// <summary>Every effect of the skill on <paramref name="target"/>, in order; a target that dies
    /// gets none of the next ones.</summary>
    public void Apply(SkillContext ctx, Combatant target)
    {
        foreach (SkillEffectDefinition effect in ctx.Skill.Effects)
        {
            if (Find(effect.Kind) is not { } type)
            {
                WarnMissing(effect.Kind);

                continue;
            }

            if (target.IsDead && !type.TargetsDead)
            {
                return;
            }

            type.Apply(ctx, target, effect);
        }
    }

    /// <summary>Once per id: the log says what the content lacks, not every time it lacks it.</summary>
    public void WarnMissing(string id)
    {
        if (_warned.TryAdd(id, 0))
        {
            _logger.LogWarning("Skill content {Id} is not available: skipped.", id);
        }
    }
}
