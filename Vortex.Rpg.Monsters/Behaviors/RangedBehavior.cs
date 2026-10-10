using System;
using Microsoft.Extensions.Options;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Monsters.Skills;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>
/// The ranged monster (spec M1c §4.3): it stops at its attack's reach in sight of its target and,
/// after a move, stands still <see cref="MonstersConfig.RangedAimMs"/> before it shoots or casts — no
/// shooting on the run. The arrow is the client's to draw; the hit lands with the swing.
/// </summary>
public sealed class RangedBehavior(IOptions<MonstersConfig> config) : MonsterBehavior
{
    private readonly MonstersConfig _config = config.Value;

    public override string Key => "ranged";

    protected override void Act(MonsterBrain brain, int targetObjectId, bool justStopped)
    {
        long now = brain.Runtime.NowMs;

        if (justStopped)
        {
            brain.Monster.AimUntilMs = now + _config.RangedAimMs;
            brain.Combatant.NextSwingAtMs = Math.Max(
                brain.Combatant.NextSwingAtMs,
                brain.Monster.AimUntilMs
            );

            return;
        }

        if (now >= brain.Monster.AimUntilMs)
        {
            MonsterSkillSelector.TryCast(brain, targetObjectId);
        }
    }
}
