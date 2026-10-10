using System.Collections.Generic;

namespace Vortex.Rpg.Monsters.Configuration;

/// <summary>What a rank does to a monster (spec M1c §7.1, §10): multipliers, and the status types
/// (<c>stun</c>, <c>slow</c>…) it ignores.</summary>
public sealed class MonsterRankConfig
{
    public double HealthMultiplier { get; set; } = 1;

    public double DamageMultiplier { get; set; } = 1;

    public double XpMultiplier { get; set; } = 1;

    public List<string> Immunities { get; set; } = [];
}
