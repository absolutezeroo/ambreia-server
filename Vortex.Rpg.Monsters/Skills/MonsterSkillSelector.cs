using System.Linq;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Spawns;

namespace Vortex.Rpg.Monsters.Skills;

/// <summary>
/// A monster's choice of skill (spec M1c §6): its rules by priority, the file's order on a tie; a
/// rule passes on its target, its least range, its health condition and its chance, then the zone's
/// casting system is asked. Cooldowns, the global cooldown, range and sight are the casting system's
/// to refuse (plan M1c decision 3): a refusal hands over to the next rule.
/// </summary>
public static class MonsterSkillSelector
{
    /// <summary>True when a cast started.</summary>
    public static bool TryCast(MonsterBrain brain, int enemyObjectId)
    {
        LiveMonster m = brain.Monster;

        // OrderByDescending is stable: on a tie, the order of the file.
        foreach (MonsterSkillRule rule in m.Skills.OrderByDescending(r => r.Priority))
        {
            if (
                brain.Content.Get<SkillDefinition>(rule.Skill) is not { Retired: false } skill
                || Aim(brain, skill, rule, enemyObjectId) is not { } aim
                || aim.Distance < rule.MinRange
                || rule.Chance < 100 && brain.Runtime.Random.Next(0, 100) >= rule.Chance
            )
            {
                continue;
            }

            if (
                brain.Combat.Cast(
                    new CastRequest(brain.Combatant.ObjectId, skill.Id, aim.ObjectId, aim.X, aim.Y)
                )
                is null
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whom or where the rule aims (spec M1c §6.2), its distance, and its health
    /// condition met; null when nothing fits.</summary>
    private static (int ObjectId, int X, int Y, int Distance)? Aim(
        MonsterBrain brain,
        SkillDefinition skill,
        MonsterSkillRule rule,
        int enemyObjectId
    )
    {
        Combatant me = brain.Combatant;

        if (brain.Runtime.PositionOf(me.ObjectId) is not { } at)
        {
            return null;
        }

        if (skill.Target == SkillTarget.Ally)
        {
            return
                MostWoundedAlly(brain, skill, at) is { } ally
                && Percent(ally.Fighter) <= rule.BelowHealthPercent
                ? (ally.Fighter.ObjectId, -1, -1, ally.Distance)
                : null;
        }

        // 100 (the default) always passes, a full health included.
        if (Percent(me) > rule.BelowHealthPercent)
        {
            return null;
        }

        if (skill.Target is SkillTarget.Self or SkillTarget.AroundCaster)
        {
            return (me.ObjectId, -1, -1, 0);
        }

        if (brain.Runtime.PositionOf(enemyObjectId) is not { } to)
        {
            return null;
        }

        int distance = CombatMath.Chebyshev(at.X, at.Y, to.X, to.Y);

        return skill.Target == SkillTarget.Ground
            ? (0, to.X, to.Y, distance)
            : (enemyObjectId, -1, -1, distance);
    }

    /// <summary>The monster in reach, alive and not walking home, with the least share of health —
    /// itself included.</summary>
    private static (Combatant Fighter, int Distance)? MostWoundedAlly(
        MonsterBrain brain,
        SkillDefinition skill,
        (int X, int Y) at
    ) =>
        brain
            .Combat.Combatants.Values.Where(c => !c.IsPlayer && !c.IsDead && !c.Invulnerable)
            .Select(c => (Fighter: c, At: brain.Runtime.PositionOf(c.ObjectId)))
            .Where(x => x.At is not null)
            .Select(x =>
                (x.Fighter, Distance: CombatMath.Chebyshev(at.X, at.Y, x.At!.Value.X, x.At.Value.Y))
            )
            .Where(x => x.Distance <= skill.Range)
            .OrderBy(x => Percent(x.Fighter))
            .ThenBy(x => x.Fighter.ObjectId)
            .Cast<(Combatant, int)?>()
            .FirstOrDefault();

    private static double Percent(Combatant c) =>
        c.MaxHealth > 0 ? 100.0 * c.Health / c.MaxHealth : 0;
}
