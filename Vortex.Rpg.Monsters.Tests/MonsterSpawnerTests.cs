using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1a §5.2: spawns born when a player is there, at a level in range, back after
/// their delay, never twice.</summary>
public sealed class MonsterSpawnerTests
{
    private readonly MonsterWorld _world = new();

    [Fact]
    public async Task NoPlayer_NoSpawn()
    {
        _world.Start([MonsterWorld.WolvesAt(2, (1, 1), (2, 1), (3, 1))]);

        await _world.TickAsync();

        _world.Zone.Spawned.Should().BeEmpty();
    }

    [Fact]
    public async Task AFirstPlayer_SpawnsMaxPerPoint_InTheArea_AtALevelInRange()
    {
        _world.Start([
            MonsterWorld.WolvesAt(2, (1, 1), (2, 1), (3, 1)) with
            {
                LevelMin = 2,
                LevelMax = 3,
            },
        ]);
        await _world.EnterAsync(7, 1, 20, 20);

        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
        _world
            .Zone.Spawned.Select(s => _world.Zone.Positions[s.ObjectId])
            .Should()
            .OnlyContain(at => at.Y == 1 && at.X >= 1 && at.X <= 3)
            .And.OnlyHaveUniqueItems();
        _world
            .Zone.Spawned.Should()
            .OnlyContain(s => s.Appearance.Level >= 2 && s.Appearance.Level <= 3);
        _world.Simulation.Combat.Combatants.Values.Count(c => !c.IsPlayer).Should().Be(2);
    }

    [Fact]
    public async Task ADeadMonster_RespawnsAfterItsDelay()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1))]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        int wolf = _world.Zone.Spawned.Single().ObjectId;

        _world.Simulation.Combat.Combatants[wolf].Health = 0;
        await _world.TickAsync();

        _world.Zone.Postures[wolf].Should().Be("ded");

        _world.Zone.NowMs += 29_000;
        await _world.TickAsync();
        _world.Zone.Removed.Should().Equal(wolf);
        _world.Zone.Spawned.Should().HaveCount(1, "30 s have not passed");

        _world.Zone.NowMs += 1_000;
        await _world.TickAsync();
        _world.Zone.Spawned.Should().HaveCount(2);
    }

    /// <summary>A send that fails (a player's presence just went away) must not cost the zone its
    /// monster: the death is still counted and it comes back.</summary>
    [Fact]
    public async Task AFailingSend_DoesNotLoseARespawn()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1))]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        int wolf = _world.Zone.Spawned.Single().ObjectId;

        // The player kills it: his own vitals (now in combat) go out, and fail.
        _world.Zone.Positions[1] = (2, 1);
        _world.Simulation.Combat.Combatants[wolf].Health = 1;
        _world.Simulation.Combat.StartAttack(new PlayerId(7), wolf);
        _world.Zone.FailSends = true;
        await _world.TickAsync();
        _world.Zone.FailSends = false;

        _world.Zone.Postures[wolf].Should().Be("ded");

        // Its body leaves 2 s later, then its spawn's delay runs out.
        _world.Zone.NowMs += 2_000;
        await _world.TickAsync();
        _world.Zone.NowMs += 28_000;
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
    }

    /// <summary>A respawn due while the body still lies on the only cell waits for it to go, and
    /// comes with a new object id.</summary>
    [Fact]
    public async Task ARespawn_NeverTakesTheCorpsesCell_NorItsId()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1)) with { RespawnSeconds = 0 }]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        int wolf = _world.Zone.Spawned.Single().ObjectId;

        _world.Simulation.Combat.Combatants[wolf].Health = 0;
        await _world.TickAsync();
        _world.Zone.NowMs += 1_000;
        await _world.TickAsync();
        _world.Zone.Spawned.Should().HaveCount(1, "its body still lies on the cell");

        _world.Zone.NowMs += 1_000;
        await _world.TickAsync();
        _world.Zone.NowMs += 1_000;
        await _world.TickAsync();

        _world.Zone.Removed.Should().Equal(wolf);
        _world.Zone.Spawned.Should().HaveCount(2);
        _world.Zone.Spawned[1].ObjectId.Should().NotBe(wolf);
    }

    [Fact]
    public async Task AnUnknownMonster_SpawnsNothing()
    {
        _world.Start([MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = "monster.none" }]);
        await _world.EnterAsync(7, 1, 20, 20);

        await _world.TickAsync();

        _world.Zone.Spawned.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUndeclaredBehavior_SpawnsNothing()
    {
        _world.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = "monster.forest.archer" }],
            content: MonsterWorld.Wolf with
            {
                Id = "monster.forest.archer",
                Behavior = "ranged",
            }
        );
        await _world.EnterAsync(7, 1, 20, 20);

        await _world.TickAsync();

        _world.Zone.Spawned.Should().BeEmpty();
    }

    [Fact]
    public async Task ReEntering_DoesNotSpawnAgain()
    {
        _world.Start([MonsterWorld.WolvesAt(2, (1, 1), (2, 1), (3, 1))]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();

        await _world.Simulation.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        await _world.TickAsync();
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
    }
}
