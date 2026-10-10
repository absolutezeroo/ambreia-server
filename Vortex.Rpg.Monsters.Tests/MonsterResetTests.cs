using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §7.4: a monster that goes home starts over — its casting reset; a boss's phases,
/// phase status, skills and summons (gone without XP) too, and its phases play again on the next pull.</summary>
public sealed class MonsterResetTests
{
    private readonly BossWorld _boss = new();

    private async Task LeashAsync()
    {
        _boss.World.Zone.Positions[_boss.BossId] = (12, 1);
        _boss.World.Zone.NowMs += 250;
        await _boss.World.TickAsync();
    }

    [Fact]
    public async Task PastItsLeashInPhaseTwo_TheBossStartsOver()
    {
        await _boss.StartAsync();
        await _boss.HurtToAsync(60);
        int[] summons = _boss.SummonIds;
        _boss.World.Calls.Clear();

        await LeashAsync();

        _boss.Statuses.Log.Should().EndWith($"remove status.enraged {_boss.BossId}");
        _boss.BossMonster.Phase.Should().Be(0);
        _boss.BossMonster.PhaseStatus.Should().BeNull();
        _boss.BossMonster.Skills.Should().BeEmpty();
        _boss.BossMonster.Summons.Should().BeEmpty();
        _boss.World.Zone.Removed.Should().Contain(summons);
        _boss.World.Simulation.Spawner.Live.Keys.Should().NotContain(summons);
        _boss.World.Calls.Should().NotContain(c => c.StartsWith("GainXpAsync"));
        _boss.Casting.Resets.Should().Contain(_boss.BossId);
        _boss.World.Counted.Should().Contain("BossReset monster.test.boss");
    }

    [Fact]
    public async Task AReset_LetsThePhasesPlayAgain()
    {
        await _boss.StartAsync();
        await _boss.HurtToAsync(60);
        await LeashAsync();
        _boss.World.Zone.Positions[_boss.BossId] = (1, 1);
        _boss.World.Zone.Walking.Clear();
        _boss.World.Zone.NowMs += 250;
        await _boss.World.TickAsync();

        await _boss.HurtToAsync(60);

        _boss.World.Zone.Broadcasts.OfType<RpgMonsterYellMessageComposer>().Should().HaveCount(2);
        _boss.SummonIds.Should().HaveCount(4);
    }

    [Fact]
    public async Task AMonsterWithoutPhases_GoesHomeAsBefore()
    {
        await _boss.StartAsync(MonsterWorld.Wolf);

        await LeashAsync();

        _boss.Casting.Resets.Should().Contain(_boss.BossId);
        _boss.World.Counted.Should().NotContain(c => c.StartsWith("BossReset"));
        _boss.BossCombatant.Invulnerable.Should().BeTrue();
    }
}
