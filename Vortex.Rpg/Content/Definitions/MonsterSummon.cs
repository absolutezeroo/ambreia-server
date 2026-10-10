namespace Vortex.Rpg.Content.Definitions;

/// <summary>Monsters a phase brings around its monster (spec M1c §7.3).</summary>
public sealed record MonsterSummon
{
    /// <summary>A monster id; never a boss.</summary>
    public required string Monster { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>0: the level of the monster that summons.</summary>
    public int Level { get; init; }
}
