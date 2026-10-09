using System.Collections.Generic;
using Vortex.Primitives.World.Layers;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;

namespace Vortex.Rpg.Monsters.Spawns;

/// <summary>A zone's spawn (spec M1a §5.2) with its monster resolved: what it brings, how it thinks,
/// and when its dead come back.</summary>
public sealed class SpawnPoint
{
    public required ZoneSpawn Source { get; init; }

    public required MonsterDefinition Definition { get; init; }

    public required IMonsterBehavior Behavior { get; init; }

    public int AggroRadius => Source.AggroRadius;

    /// <summary>The monster's leash, else twice the aggro radius.</summary>
    public int LeashRadius =>
        Definition.LeashRadius > 0 ? Definition.LeashRadius : 2 * Source.AggroRadius;

    /// <summary>When each of its dead is due back, in order.</summary>
    public Queue<long> Due { get; } = new();
}
