using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Layers;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec Z1 §5.1: a monster of a spawn dies, and for each player who fought it the zone is told to
/// run its MonsterKilled triggers aimed at the spawn, then at the monster.
/// </summary>
public sealed class MonsterKilledTests
{
    private readonly MonsterWorld _world = new();

    [Fact]
    public async Task ADeath_IsToldWithItsSpawnAndEachParticipant()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (8, 8))]);
        await _world.EnterAsync(7, 1, 1, 1);
        await _world.EnterAsync(8, 2, 1, 2);
        await _world.TickAsync();
        int wolf = _world.Zone.Spawned.Single().ObjectId;
        _world.Simulation.Combat.Combatants[wolf].Participants.Add(new PlayerId(7));
        _world.Simulation.Combat.Combatants[wolf].Participants.Add(new PlayerId(8));
        _world.Simulation.Combat.Combatants[wolf].Health = 0;

        await _world.TickAsync();

        _world
            .Zone.Raised.Should()
            .BeEquivalentTo([
                (new PlayerId(7), ZoneTriggerType.MonsterKilled, "wolves"),
                (new PlayerId(7), ZoneTriggerType.MonsterKilled, MonsterWorld.Wolf.Id),
                (new PlayerId(8), ZoneTriggerType.MonsterKilled, "wolves"),
                (new PlayerId(8), ZoneTriggerType.MonsterKilled, MonsterWorld.Wolf.Id),
            ]);
    }

    [Fact]
    public async Task ADeathNobodyFought_TellsNothing()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (8, 8))]);
        await _world.EnterAsync(7, 1, 1, 1);
        await _world.TickAsync();
        int wolf = _world.Zone.Spawned.Single().ObjectId;
        _world.Simulation.Combat.Combatants[wolf].Health = 0;

        await _world.TickAsync();

        _world.Zone.Raised.Should().BeEmpty();
    }
}
