using System.Collections.Generic;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;

namespace Vortex.Rpg.Monsters.Spawns;

/// <summary>A monster alive in a zone: where it was born, what its AI is doing.</summary>
public sealed class LiveMonster
{
    public required int ObjectId { get; init; }

    public required MonsterDefinition Definition { get; init; }

    public required SpawnPoint Spawn { get; init; }

    /// <summary>Its birth cell: the leash is measured from it, it goes back to it.</summary>
    public required (int X, int Y) Home { get; init; }

    public MonsterState State { get; set; } = MonsterState.Idle;

    public long NextWanderAtMs { get; set; }

    /// <summary>Where its target stood when it last set off after it.</summary>
    public (int X, int Y)? ChaseGoal { get; set; }

    /// <summary>The rules it casts by (spec M1c §6): its own, then its phase's.</summary>
    public IReadOnlyList<MonsterSkillRule> Skills { get; set; } = [];

    /// <summary>How many of its phases have played (spec M1c §7.2).</summary>
    public int Phase { get; set; }

    /// <summary>The status its current phase put on it, taken off at the next one or a reset.</summary>
    public string? PhaseStatus { get; set; }

    /// <summary>The monster that summoned it; null for a monster of a spawn.</summary>
    public int? Parent { get; init; }

    /// <summary>Its summons still alive.</summary>
    public List<int> Summons { get; } = [];

    /// <summary>A ranged monster shoots no earlier (spec M1c §4.3).</summary>
    public long AimUntilMs { get; set; }
}
