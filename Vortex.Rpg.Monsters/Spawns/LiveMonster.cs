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
}
