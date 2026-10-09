using System;
using System.Collections.Generic;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Skills.Zones;

namespace Vortex.Rpg.Skills.Effects;

/// <summary>One application of a skill (a cast landing, a channel's pulse): who, what, where its
/// effects report, and the damage it has done so far on each target.</summary>
public sealed class SkillContext
{
    public required CombatZone Zone { get; init; }

    public required Combatant Caster { get; init; }

    public required SkillDefinition Skill { get; init; }

    public required long Now { get; init; }

    public required HitResolver Hits { get; init; }

    public required StatusApplier Statuses { get; init; }

    public required SkillsConfig Config { get; init; }

    public required IRpgContent Content { get; init; }

    public required SkillEffects Effects { get; init; }

    /// <summary>By target object id, for the threat effect.</summary>
    public Dictionary<int, int> DamageDone { get; } = [];

    public Random Random => Zone.Runtime.Random;

    public SkillEvents Events => Zone.GetOrAdd<SkillEvents>();

    /// <summary>What this skill did to <paramref name="target"/>, for the zone (spec M1b §10.2).</summary>
    public void Report(Combatant target, int amount, int absorbed, string outcome) =>
        Events.AddOutcome(
            new RpgSkillOutcome
            {
                SourceObjectId = Caster.ObjectId,
                SourceId = Skill.Id,
                TargetObjectId = target.ObjectId,
                Amount = amount,
                Absorbed = absorbed,
                Outcome = outcome,
                Health = target.Health,
                MaxHealth = target.MaxHealth,
                Killed = target.Health == 0,
            }
        );
}
