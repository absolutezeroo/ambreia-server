using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Players;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Monsters.Tests.Support;
using Vortex.Rpg.Monsters.Zones;
using Vortex.Rpg.Zones.Rules;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec Z1 §6.1–6.2: a point holds min..max monsters; a point a trigger wakes sleeps until then and
/// brings one wave, never respawned, again only once that wave is dead.
/// </summary>
public sealed class SpawnWaveTests
{
    private readonly MonsterWorld _world = new();

    private static readonly (int X, int Y)[] Clearing = [(1, 1), (2, 1), (3, 1), (4, 1), (5, 1)];

    private static ZoneTrigger Wakes(string spawn) =>
        new()
        {
            Id = "t_" + spawn,
            Type = ZoneTriggerType.MonsterKilled,
            Target = "elsewhere",
            Actions = [new SpawnGroupAction(spawn)],
        };

    private void StartWith(IEnumerable<ZoneSpawn> spawns, params ZoneTrigger[] triggers) =>
        _world.StartZone(
            ZoneSamples.Minimal() with
            {
                Spawns = [.. spawns],
                Triggers = [.. triggers],
            }
        );

    private static ZoneSpawn Ambush(string id = "ambush", string? group = null) =>
        MonsterWorld.WolvesAt(2, Clearing) with
        {
            Id = id,
            Group = group,
        };

    private async Task KillAllAsync()
    {
        foreach (int wolf in _world.Simulation.Spawner.Live.Keys.ToArray())
        {
            _world.Simulation.Combat.Combatants[wolf].Health = 0;
        }

        await _world.TickAsync();
    }

    [Fact]
    public async Task APoint_HoldsBetweenMinAndMax()
    {
        // Every draw is 0.5: Next(2, 5) gives 3.
        StartWith([MonsterWorld.WolvesAt(4, Clearing) with { Min = 2, Max = 4 }]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        _world.Simulation.Spawner.Live.Should().HaveCount(3);

        _world.Simulation.Combat.Combatants[_world.Simulation.Spawner.Live.Keys.First()].Health = 0;
        await _world.TickAsync();
        _world.Zone.NowMs += 31_000;
        await _world.TickAsync();

        _world.Simulation.Spawner.Live.Should().HaveCount(3, "the dead one came back");
    }

    [Fact]
    public async Task ADormantPoint_BringsNothingAlone()
    {
        StartWith([Ambush()], Wakes("ambush"));
        await _world.EnterAsync(7, 1, 20, 20);

        await _world.TickAsync();
        _world.Zone.NowMs += 60_000;
        await _world.TickAsync();

        _world.Zone.Spawned.Should().BeEmpty();
    }

    [Fact]
    public async Task WakingIt_BringsOneWave_NeverRespawned()
    {
        StartWith([Ambush()], Wakes("ambush"));
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();

        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(1);
        await _world.TickAsync();
        _world.Zone.Spawned.Should().HaveCount(2);

        await KillAllAsync();
        _world.Zone.NowMs += 120_000;
        await _world.TickAsync();

        _world.Simulation.Spawner.Live.Should().BeEmpty();
        _world.Zone.Spawned.Should().HaveCount(2, "a wave is not respawned");
    }

    [Fact]
    public async Task AWakeWhileTheWaveLives_IsIgnored_AndAfterItDied_BringsAnother()
    {
        StartWith([Ambush()], Wakes("ambush"));
        await _world.EnterAsync(7, 1, 20, 20);
        _world.Simulation.Spawner.WakeGroup("ambush");
        await _world.TickAsync();

        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(0);
        await _world.TickAsync();
        _world.Zone.Spawned.Should().HaveCount(2);

        await KillAllAsync();
        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(1);
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(4);
    }

    [Fact]
    public async Task WakingAGroup_WakesEveryPointOfIt()
    {
        StartWith(
            [Ambush("north", "raid"), Ambush("south", "raid"), Ambush("other")],
            Wakes("raid"),
            Wakes("other")
        );
        await _world.EnterAsync(7, 1, 20, 20);

        _world.Simulation.Spawner.WakeGroup("raid").Should().Be(2);
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(4);
    }

    /// <summary>
    /// No room on its area (corpses of the last wave, players standing there): the wave is owed,
    /// and comes as cells free up; it is not lost, and a second wake meanwhile changes nothing.
    /// </summary>
    [Fact]
    public async Task AWaveWithNoRoom_IsOwed_AndComesWhenThereIsRoom()
    {
        StartWith(
            [MonsterWorld.WolvesAt(2, (1, 1), (2, 1)) with { Id = "ambush" }],
            Wakes("ambush")
        );
        await _world.EnterAsync(7, 1, 20, 20);
        _world.Zone.Walls.Add((1, 1));
        _world.Zone.Walls.Add((2, 1));
        _world.Simulation.Spawner.WakeGroup("ambush");
        await _world.TickAsync();
        _world.Zone.Spawned.Should().BeEmpty();

        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(0, "its wave is still owed");
        _world.Zone.Walls.Clear();
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
    }

    /// <summary>A wave boss's adds are part of its wave: it is over when they are dead too.</summary>
    [Fact]
    public async Task AWave_IncludesItsBossesSummons()
    {
        StartWith([Ambush()], Wakes("ambush"));
        await _world.EnterAsync(7, 1, 20, 20);
        _world.Simulation.Spawner.WakeGroup("ambush");
        await _world.TickAsync();
        LiveMonster leader = _world.Simulation.Spawner.Live.Values.First();
        _world.Simulation.Spawner.Summon(leader, MonsterWorld.Wolf, 1, 1, _world.Zone.NowMs);
        int add = _world
            .Simulation.Spawner.Live.Keys.Except(
                _world.Zone.Spawned.Take(2).Select(s => s.ObjectId)
            )
            .Single();

        foreach (int id in _world.Zone.Spawned.Take(2).Select(s => s.ObjectId))
        {
            _world.Simulation.Combat.Combatants[id].Health = 0;
        }

        await _world.TickAsync();
        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(0, "its add still lives");

        _world.Simulation.Combat.Combatants[add].Health = 0;
        await _world.TickAsync();

        _world.Simulation.Spawner.WakeGroup("ambush").Should().Be(1);
    }

    /// <summary>A clock may wake it when nobody is there: the wave waits for a player.</summary>
    [Fact]
    public async Task AWakeWithNoPlayer_WaitsForOne()
    {
        StartWith([Ambush()], Wakes("ambush"));

        _world.Simulation.Spawner.WakeGroup("ambush");
        await _world.TickAsync();
        _world.Zone.Spawned.Should().BeEmpty();

        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheAction_WakesTheZonesSpawner()
    {
        StartWith([Ambush()], Wakes("ambush"));
        await _world.EnterAsync(7, 1, 20, 20);

        await new SpawnGroupExecutor(NullLogger<SpawnGroupExecutor>.Instance).RunAsync(
            new SpawnGroupAction("ambush"),
            new ZoneRuleContext(
                PlayerId.Invalid,
                new Dictionary<Type, ZoneFacts>(),
                DateTimeOffset.Now,
                _world.Zone,
                _world.Simulation
            ),
            CancellationToken.None
        );
        await _world.TickAsync();

        _world.Zone.Spawned.Should().HaveCount(2);
    }
}
