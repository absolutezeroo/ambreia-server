using System;
using System.Linq;
using Vortex.Rpg.Content.Definitions;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// The caster (spec M1c §4.3): it stands at the least range of its offensive skills — from there it
/// can cast them all (plan M1c decision 5) — else at its attack's reach, and casts before it strikes.
/// </summary>
public sealed class CasterBehavior : MonsterBehavior
{
    public override string Key => "caster";

    protected override int DesiredRange(MonsterBrain brain)
    {
        int[] ranges =
        [
            .. brain
                .Monster.Skills.Select(r => brain.Content.Get<SkillDefinition>(r.Skill))
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
