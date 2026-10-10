using System;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// The caster (spec M1c §4.3): it stands at the least range of the offensive skills its health lets
/// it use now — from there it can cast them all (plan M1c decision 5) — else at its attack's reach,
/// and casts before it strikes. Its content gives it an attack that reaches that post (a weak shot),
/// or it waits there for its cooldowns.
/// </summary>
public sealed class CasterBehavior : MonsterBehavior
{
    public override string Key => "caster";

    protected override int DesiredRange(MonsterBrain brain)
    {
        double health =
            brain.Combatant.MaxHealth > 0
                ? 100.0 * brain.Combatant.Health / brain.Combatant.MaxHealth
                : 0;
        int[] ranges =
        [
            .. brain
                .Monster.Skills.Where(r => health <= r.BelowHealthPercent)
                .Select(r => brain.Content.Get<SkillDefinition>(r.Skill))
                .Where(s =>
                    s
                        is {
                            Retired: false,
                            Target: SkillTarget.Enemy
                                or SkillTarget.Ground
                                or SkillTarget.AroundTarget,
                        }
                )
                .Select(s => s!.Range),
        ];

        return ranges.Length > 0 ? Math.Max(1, ranges.Min()) : brain.Combatant.Range;
    }
}
