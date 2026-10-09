using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1b §7.2: a stunned monster does not think — no chase, no target — and thinks again
/// once the stun is gone. A wolf is born at (1, 1), aggro radius 4.</summary>
public sealed class StunnedMonsterTests
{
    private readonly MonsterWorld _world = new();

    private int Wolf => _world.Zone.Spawned.Single().ObjectId;

    private Combatant WolfCombatant => _world.Simulation.Combat.Combatants[Wolf];

    [Fact]
    public async Task AStunnedMonster_DoesNotThink_ThenThinksAgain()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1))]);
        await _world.EnterAsync(7, 1, 9, 1);
        await _world.TickAsync();

        WolfCombatant.Stunned = true;
        _world.Zone.Positions[1] = (4, 1);
        await _world.TickAsync();

        _world.Zone.Walks.Should().NotContain(w => w.ObjectId == Wolf);
        (WolfCombatant.TargetId, WolfCombatant.Threat.Top).Should().Be((null, null));

        WolfCombatant.Stunned = false;
        await _world.TickAsync();

        _world.Zone.Walks.Should().Contain((Wolf, 4, 1));
    }
}
