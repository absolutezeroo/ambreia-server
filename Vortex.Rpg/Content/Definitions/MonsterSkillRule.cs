namespace Vortex.Rpg.Content.Definitions;

/// <summary>When a monster uses one of its skills (spec M1c §5.2, §6).</summary>
public sealed record MonsterSkillRule
{
    /// <summary>A skill id.</summary>
    public required string Skill { get; init; }

    /// <summary>Higher first; on a tie, the order of the file.</summary>
    public int Priority { get; init; }

    /// <summary>The least distance to its target, in tiles.</summary>
    public int MinRange { get; init; }

    /// <summary>Only below this share of health: the caster's, or the chosen ally's for an ally skill.</summary>
    public int BelowHealthPercent { get; init; } = 100;

    /// <summary>The chance, in %, that it tries the skill when the skill is ready.</summary>
    public int Chance { get; init; } = 100;
}
