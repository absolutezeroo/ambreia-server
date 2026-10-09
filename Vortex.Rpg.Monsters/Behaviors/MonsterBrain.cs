using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Behaviors;

/// <summary>What a behavior reads and acts on for one monster (spec M1a §5.3).</summary>
public sealed class MonsterBrain
{
    public required LiveMonster Monster { get; init; }

    public required Combatant Combatant { get; init; }

    public required CombatZone Combat { get; init; }

    public required IZoneRuntime Runtime { get; init; }
}
