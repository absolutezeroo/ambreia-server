using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Rpg.Combat;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §7.3: a phase's summons come in around the boss on free cells that are not safe,
/// take on its threat, never come back once killed, and outlive the boss.</summary>
public sealed class SummonTests
{
    private readonly BossWorld _boss = new();

    [Fact]
    public async Task APhase_BringsItsSummons_NearTheBoss_AtTheirLevel()
    {
        await _boss.StartAsync();

        await _boss.HurtToAsync(60);

        _boss.SummonIds.Should().HaveCount(2);
        (int X, int Y) at = _boss.World.Zone.PositionOf(_boss.BossId)!.Value;

        foreach ((var appearance, int id) in _boss.World.Zone.Spawned.Skip(1))
        {
            appearance.DefinitionId.Should().Be(MonsterWorld.Wolf.Id);
            appearance.Level.Should().Be(3);
            (int X, int Y) cell = _boss.World.Zone.PositionOf(id)!.Value;
            CombatMath.Chebyshev(at.X, at.Y, cell.X, cell.Y).Should().BeInRange(1, 2);
            _boss.World.Zone.SafeCells.Should().NotContain(cell);
        }
    }

    [Fact]
    public async Task Summons_TakeOnTheBossesThreat()
    {
        await _boss.StartAsync();

        await _boss.HurtToAsync(60);

        _boss
            .SummonIds.Select(id => _boss.World.Simulation.Combat.Combatants[id].Threat.Top)
            .Should()
            .OnlyContain(top => top == 1);
    }

    [Fact]
    public async Task PastTheCap_ASummonIsLeftOut()
    {
        _boss.World.Config.MaxSummonsPerMonster = 1;
        await _boss.StartAsync();

        await _boss.HurtToAsync(60);

        _boss.SummonIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task WithoutAFreeCell_NoSummonComes()
    {
        await _boss.StartAsync();

        for (int x = -1; x <= 3; x++)
        {
            for (int y = -1; y <= 3; y++)
            {
                if ((x, y) != (1, 1) && (x, y) != (2, 1))
                {
                    _boss.World.Zone.Walls.Add((x, y));
                }
            }
        }

        await _boss.HurtToAsync(60);

        _boss.SummonIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AKilledSummon_NeverRespawns_AndFreesItsSlot()
    {
        await _boss.StartAsync();
        await _boss.HurtToAsync(60);
        int summon = _boss.SummonIds[0];

        _boss.World.Simulation.Combat.Damage(
            _boss.World.Simulation.Combat.Combatants[1],
            _boss.World.Simulation.Combat.Combatants[summon],
            1000,
            _boss.World.Zone.NowMs
        );

        for (int i = 0; i < 200; i++)
        {
            _boss.World.Zone.NowMs += 250;
            await _boss.World.TickAsync();
        }

        _boss.World.Zone.Spawned.Should().HaveCount(3);
        _boss.World.Simulation.Spawner.Live.Should().NotContainKey(summon);
        _boss.BossMonster.Summons.Should().NotContain(summon);
    }

    [Fact]
    public async Task TheBossDying_LeavesItsSummons()
    {
        await _boss.StartAsync();
        await _boss.HurtToAsync(60);

        _boss.World.Simulation.Combat.Damage(
            _boss.World.Simulation.Combat.Combatants[1],
            _boss.BossCombatant,
            100000,
            _boss.World.Zone.NowMs
        );
        _boss.World.Zone.NowMs += 250;
        await _boss.World.TickAsync();

        _boss
            .SummonIds.Should()
            .OnlyContain(id => _boss.World.Simulation.Spawner.Live.ContainsKey(id));
    }
}
