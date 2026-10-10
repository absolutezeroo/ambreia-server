using System.Collections.Generic;

namespace Vortex.Rpg.Content.Definitions;

/// <summary>
/// A monster (spec M1a §5.1): how it looks (a Habbo pet), how it behaves, its stats at level 1 and
/// growth per level, its attack and the XP it gives. Its level comes from the spawn that places it.
/// </summary>
public sealed record MonsterDefinition : ContentDefinition
{
    /// <summary>A Habbo pet figure: "&lt;type&gt; &lt;race&gt; &lt;colour&gt;".</summary>
    public required string Figure { get; init; }

    /// <summary>A behaviour key declared by a module (<c>melee</c>…).</summary>
    public string Behavior { get; init; } = "melee";

    /// <summary>Attacks on sight within its aggro radius; otherwise only when hit.</summary>
    public bool Aggressive { get; init; }

    public required MonsterStatBlock Base { get; init; }

    public MonsterStatBlock PerLevel { get; init; } = new();

    /// <summary>In tiles (Chebyshev).</summary>
    public int AttackRange { get; init; } = 1;

    public int AttackIntervalMs { get; init; } = 2000;

    /// <summary>How far from its spawn it follows a player; 0: twice the spawn's aggro radius.</summary>
    public int LeashRadius { get; init; }

    public int Xp { get; init; }

    public int XpPerLevel { get; init; }

    /// <summary>How long its body stays in the room once killed, lying dead (status <c>ded</c>):
    /// 0 to 10 seconds; 0 removes it at once.</summary>
    public int CorpseSeconds { get; init; } = 2;

    /// <summary>Scales its stats by configuration (spec M1c §7.1).</summary>
    public MonsterRank Rank { get; init; } = MonsterRank.Normal;

    /// <summary>When it uses which skill, outside its phases (spec M1c §5.2).</summary>
    public List<MonsterSkillRule> Skills { get; init; } = [];

    /// <summary>What changes as its health falls (spec M1c §7.2).</summary>
    public List<MonsterPhase> Phases { get; init; } = [];
}
