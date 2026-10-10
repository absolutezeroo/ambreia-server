using System.Linq;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Spawns;

namespace Vortex.Rpg.Monsters.Phases;

/// <summary>
/// A monster's phases (spec M1c §7.2, §7.4): each plays once, the first tick its health is below its
/// threshold (plan M1c decision 1), every threshold crossed playing in turn; a reset puts it back as it
/// came in.
/// </summary>
public static class BossPhases
{
    public static void Check(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;
        Combatant c = brain.Combatant;

        if (m.Definition.Phases.Count == 0 || c.IsDead || c.Invulnerable || c.MaxHealth <= 0)
        {
            return;
        }

        MonsterPhase[] phases = [.. m.Definition.Phases.OrderByDescending(p => p.AtHealthPercent)];

        while (
            m.Phase < phases.Length
            && 100.0 * c.Health / c.MaxHealth < phases[m.Phase].AtHealthPercent
        )
        {
            Enter(brain, phases[m.Phase], ++m.Phase);
        }
    }

    /// <summary>Its fight starts over (spec M1c §7.4): no cast, no cooldown; for a monster with phases,
    /// none played, no phase status, its own skills, its summons gone (no XP).</summary>
    public static void Reset(MonsterBrain brain)
    {
        LiveMonster m = brain.Monster;
        Combatant c = brain.Combatant;

        brain.Combat.ResetCasts(c);

        if (m.PhaseStatus is { } status)
        {
            brain.Combat.RemoveStatus(c, status);
            m.PhaseStatus = null;
        }

        m.Skills = m.Definition.Skills;
        brain.Spawner.Dismiss(m);

        if (m.Definition.Phases.Count > 0)
        {
            m.Phase = 0;
            brain.Metrics.BossReset(m.Definition.Id);
        }
    }

    private static void Enter(MonsterBrain brain, MonsterPhase phase, int number)
    {
        LiveMonster m = brain.Monster;
        Combatant c = brain.Combatant;

        if (phase.Skills.Count > 0)
        {
            m.Skills = phase.Skills;
        }

        if (m.PhaseStatus is { } previous)
        {
            brain.Combat.RemoveStatus(c, previous);
        }

        m.PhaseStatus = phase.Status;

        if (phase.Status is { } status)
        {
            brain.Combat.ApplyStatus(c, c, status, permanent: true);
        }

        if (
            phase.Summon is { } summon
            && brain.Content.Get<MonsterDefinition>(summon.Monster) is { Retired: false } summoned
        )
        {
            brain.Spawner.Summon(
                m,
                summoned,
                summon.Count,
                summon.Level > 0 ? summon.Level : c.Level,
                brain.Runtime.NowMs
            );
        }

        if (phase.Yell is { } yell)
        {
            brain.Combat.Broadcast(
                new RpgMonsterYellMessageComposer { ObjectId = m.ObjectId, TextKey = yell }
            );
        }

        brain.Metrics.BossPhase(m.Definition.Id, number);
    }
}
