using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>Spec M1c §7.2: a phase plays once, when health falls below its threshold: its skills,
/// its status for as long as it lasts, its yell; two thresholds crossed at once play both, in order.</summary>
public sealed class BossPhasesTests
{
    private readonly BossWorld _boss = new();

    private string[] Yells =>
        [
            .. _boss
                .World.Zone.Broadcasts.OfType<RpgMonsterYellMessageComposer>()
                .Select(y => y.TextKey),
        ];

    [Fact]
    public async Task BelowItsThreshold_APhasePlaysOnce()
    {
        await _boss.StartAsync();

        await _boss.HurtToAsync(60);
        await _boss.HurtToAsync(55);

        Yells.Should().Equal("monster.test.boss.phase2");
        _boss.Statuses.Log.Should().Equal($"apply status.enraged {_boss.BossId} permanent");
        _boss.BossMonster.Skills.Should().Equal(BossWorld.Bite);
        _boss.World.Counted.Should().Contain("BossPhase monster.test.boss 1");
    }

    [Fact]
    public async Task AboveItsThreshold_NothingPlays()
    {
        await _boss.StartAsync();

        await _boss.HurtToAsync(70);

        Yells.Should().BeEmpty();
        _boss.Statuses.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoThresholdsAtOnce_PlayBoth_InOrder()
    {
        await _boss.StartAsync();

        await _boss.HurtToAsync(20);

        Yells.Should().Equal("monster.test.boss.phase2", "monster.test.boss.phase3");
        _boss
            .Statuses.Log.Should()
            .Equal(
                $"apply status.enraged {_boss.BossId} permanent",
                $"remove status.enraged {_boss.BossId}",
                $"apply status.frenzy {_boss.BossId} permanent"
            );
        // A phase without skills keeps the last ones.
        _boss.BossMonster.Skills.Should().Equal(BossWorld.Bite);
        _boss.BossMonster.Phase.Should().Be(2);
    }
}
