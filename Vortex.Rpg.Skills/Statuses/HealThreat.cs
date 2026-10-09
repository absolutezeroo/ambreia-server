using System.Linq;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Skills.Configuration;

namespace Vortex.Rpg.Skills.Statuses;

/// <summary>A heal angers whoever fights the healed (spec M1b §8): a share of what was really given,
/// to each monster that already threatens him.</summary>
public static class HealThreat
{
    public static void Spread(
        CombatZone zone,
        Combatant healer,
        Combatant healed,
        int amount,
        SkillsConfig config
    )
    {
        long threat = (long)amount * config.HealThreatPercent / 100;

        if (threat <= 0)
        {
            return;
        }

        foreach (
            Combatant monster in zone.Combatants.Values.Where(m =>
                !m.IsPlayer && m.Threat.Contains(healed.ObjectId)
            )
        )
        {
            zone.AddThreat(monster, healer.ObjectId, threat);
        }
    }
}
