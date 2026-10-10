using Vortex.Primitives.Observability;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>What a behavior reads and acts on for one monster (spec M1a §5.3, M1c §6, §7).</summary>
public sealed class MonsterBrain
{
    public required LiveMonster Monster { get; init; }

    public required Combatant Combatant { get; init; }

    public required CombatZone Combat { get; init; }

    public required IZoneRuntime Runtime { get; init; }

    /// <summary>Its zone's monsters: a boss's summons come and go through it.</summary>
    public required MonsterSpawner Spawner { get; init; }

    public required IRpgContent Content { get; init; }

    public required IVortexMetrics Metrics { get; init; }

    public required MonstersConfig Config { get; init; }
}
