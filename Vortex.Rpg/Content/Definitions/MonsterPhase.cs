using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>What changes when a monster's health falls below a threshold (spec M1c §7.2).</summary>
public sealed record MonsterPhase
{
    /// <summary>1 to 99: played once, when its health falls below it.</summary>
    public required int AtHealthPercent { get; init; }

    /// <summary>Its skills from then on; empty keeps the previous ones.</summary>
    public List<MonsterSkillRule> Skills { get; init; } = [];

    /// <summary>A status put on the monster for as long as the phase lasts.</summary>
    public string? Status { get; init; }

    public MonsterSummon? Summon { get; init; }

    /// <summary>A text key the monster shouts to the zone.</summary>
    public string? Yell { get; init; }
}
