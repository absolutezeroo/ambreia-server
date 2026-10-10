using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec Z1 §6.3–6.4: a spawn brings nothing while its conditions do not hold, and sends its idle
/// monsters away when they stop holding; with activation by nearness it wakes when a player comes
/// within <c>AoiRadius</c> and sleeps after <c>AoiSleepSeconds</c> with nobody near. A monster in a
/// fight always stays.
/// </summary>
public sealed class SpawnActivationTests
{
    private readonly MonsterWorld _world = new();

    private static ZoneSpawn AtNight() =>
        MonsterWorld.WolvesAt(2, (1, 1), (2, 1), (3, 1)) with
        {
            Conditions = [new TimeOfDayCondition(22, 6)],
        };

    private static ZoneSpawn Near() =>
        MonsterWorld.WolvesAt(2, (1, 1), (2, 1), (3, 1)) with
        {
            AoiActivation = true,
        };

    private async Task TickAfterAsync(int ms)
    {
        _world.Zone.NowMs += ms;
        await _world.TickAsync();
    }

    [Fact]
    public async Task FalseConditions_SpawnNothing()
    {
        _world.Zone.ConditionsAnswer = _ => false;
        _world.Start([AtNight()]);
        await _world.EnterAsync(7, 1, 20, 20);

        await _world.TickAsync();
        await TickAfterAsync(5000);

        _world.Zone.Spawned.Should().BeEmpty();
    }

    [Fact]
    public async Task TurningFalse_SendsTheIdleAway_KeepsTheOneFighting()
    {
        _world.Start([AtNight()]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        int[] wolves = [.. _world.Simulation.Spawner.Live.Keys];
        _world.Simulation.Combat.Combatants[wolves[0]].InCombat = true;

        _world.Zone.ConditionsAnswer = _ => false;
        await TickAfterAsync(1000);

        _world.Zone.Removed.Should().Equal(wolves[1]);
        _world.Simulation.Spawner.Live.Keys.Should().Equal(wolves[0]);
    }

    [Fact]
    public async Task TurningTrueAgain_Refills()
    {
        _world.Start([AtNight()]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        _world.Zone.ConditionsAnswer = _ => false;
        await TickAfterAsync(1000);
        _world.Simulation.Spawner.Live.Should().BeEmpty();

        _world.Zone.ConditionsAnswer = _ => true;
        await TickAfterAsync(1000);

        _world.Simulation.Spawner.Live.Should().HaveCount(2);
    }

    [Fact]
    public async Task WithNearness_APointWakesOnlyWhenAPlayerComesClose()
    {
        _world.Start([Near()]);
        await _world.EnterAsync(7, 1, 20, 20);
        await _world.TickAsync();
        _world.Zone.Spawned.Should().BeEmpty("19 cells away, the radius is 12");

        _world.Zone.Positions[1] = (14, 1);
        await TickAfterAsync(1000);

        _world.Zone.Spawned.Should().HaveCount(2, "11 cells from the nearest cell of its area");
    }

    [Fact]
    public async Task NobodyNearForTheDelay_ItSleeps_KeepingTheOneFighting()
    {
        _world.Start([Near()]);
        // Near enough to wake it (7 cells), too far for the wolves to come at him (aggro 4).
        await _world.EnterAsync(7, 1, 10, 1);
        await _world.TickAsync();
        int[] wolves = [.. _world.Simulation.Spawner.Live.Keys];

        _world.Zone.Positions[1] = (20, 20);
        await TickAfterAsync(1000);
        _world.Zone.Removed.Should().BeEmpty("30 s have not passed");

        // Fighting when the spawn falls asleep (the combat clears it on its own, out of a fight).
        _world.Simulation.Combat.Combatants[wolves[0]].InCombat = true;
        await TickAfterAsync(30_000);

        _world.Zone.Removed.Should().Equal(wolves[1]);
        _world.Simulation.Spawner.Live.Keys.Should().Equal(wolves[0]);
    }

    [Fact]
    public async Task AsleepAndThenNear_ItRefills()
    {
        _world.Start([Near()]);
        await _world.EnterAsync(7, 1, 10, 1);
        await _world.TickAsync();
        _world.Zone.Positions[1] = (20, 20);
        await TickAfterAsync(1000);
        await TickAfterAsync(30_000);
        _world.Simulation.Spawner.Live.Should().BeEmpty();

        _world.Zone.Positions[1] = (10, 1);
        await TickAfterAsync(1000);

        _world.Simulation.Spawner.Live.Should().HaveCount(2);
    }
}
