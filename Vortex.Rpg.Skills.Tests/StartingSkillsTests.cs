using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1b §16: the twelve skills of content/ as shipped, each cast for real by a level 6 character
/// of its class with Power 25 at (5, 5); wolves of 500 health and 0 armor. Every random draw is 0.5:
/// no dodge, no crit.
/// </summary>
public sealed class StartingSkillsTests
{
    private readonly SkillWorld _world = SkillWorld.WithCasting(
        RepositoryContent.SkillsAndStatuses()
    );

    private Combatant Wolf(int x = 6, int y = 5) =>
        _world.Wolf(
            x,
            y,
            SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with { Health = 500 },
            }
        );

    private Task<Combatant> HeroAsync(string cls) => _world.PlayerAsync(classId: cls);

    private static string[] StatusesOn(Combatant c) =>
        [.. (c.Get<StatusSet>()?.All ?? []).Select(s => s.Definition.Id)];

    private async Task CastAsync(
        string skill,
        int target = 0,
        int x = -1,
        int y = -1,
        long ms = 250
    )
    {
        _world.Cast(skill, target, x, y).Should().BeNull();
        await _world.RunAsync(ms);
    }

    [Fact]
    public async Task HeroicStrike_HitsAndAngers()
    {
        await HeroAsync("class.warrior");
        Combatant wolf = Wolf();

        await CastAsync("skill.warrior.heroic_strike", wolf.ObjectId);

        (wolf.Health, wolf.Threat.ThreatOf(1)).Should().Be((471, 29 + 14), "4 + 25; +50 % threat");
    }

    [Fact]
    public async Task ChallengingShout_WeakensEveryEnemyAround()
    {
        await HeroAsync("class.warrior");
        Combatant near = Wolf(6, 5);
        Combatant other = Wolf(4, 6);

        await CastAsync("skill.warrior.challenging_shout");

        (StatusesOn(near), StatusesOn(other))
            .Should()
            .BeEquivalentTo((new[] { "status.weakened" }, new[] { "status.weakened" }));
        near.DamageDealtPercent.Should().Be(-20);
    }

    [Fact]
    public async Task ShieldBash_HitsAndStuns()
    {
        await HeroAsync("class.warrior");
        Combatant wolf = Wolf();

        await CastAsync("skill.warrior.shield_bash", wolf.ObjectId);

        (wolf.Health, wolf.Stunned).Should().Be((487, true), "0.5 × 25 = 12.5, rounded up");
    }

    [Fact]
    public async Task AimedShot_LandsAfterItsCast()
    {
        await HeroAsync("class.archer");
        Combatant wolf = Wolf(11, 5);

        await CastAsync("skill.archer.aimed_shot", wolf.ObjectId, ms: 1500);

        wolf.Health.Should().Be(454, "6 + 1.6 × 25");
    }

    [Fact]
    public async Task Focus_RaisesCrit_OffTheGlobalCooldown()
    {
        Combatant me = await HeroAsync("class.archer");
        Combatant wolf = Wolf(11, 5);
        _world.Cast("skill.archer.aimed_shot", wolf.ObjectId).Should().BeNull();
        await _world.RunAsync(1500);
        _world.Cast("skill.archer.volley", 0, 11, 5).Should().BeNull();

        _world.Cast("skill.archer.focus").Should().Be("casting", "a channel runs");

        await _world.RunAsync(3000);
        await CastAsync("skill.archer.focus");

        me.CritPercent.Should().Be(20);
    }

    [Fact]
    public async Task Volley_RainsOnTheGround_ForThreePulses()
    {
        await HeroAsync("class.archer");
        Combatant wolf = Wolf(10, 5);

        await CastAsync("skill.archer.volley", 0, 10, 5, ms: 3000);

        wolf.Health.Should().Be(500 - 3 * 13);
    }

    [Fact]
    public async Task Firebolt_LandsAfterTwoSeconds()
    {
        await HeroAsync("class.mage");
        Combatant wolf = Wolf();

        await CastAsync("skill.mage.firebolt", wolf.ObjectId, ms: 2000);

        wolf.Health.Should().Be(447, "8 + 1.8 × 25");
    }

    [Fact]
    public async Task FlameBurst_BurnsTheTargetAndItsNeighbours()
    {
        await HeroAsync("class.mage");
        Combatant target = Wolf(8, 5);
        Combatant beside = Wolf(9, 5);

        await CastAsync("skill.mage.flame_burst", target.ObjectId, ms: 1500);

        (target.Health, beside.Health).Should().Be((480, 480), "0.8 × 25");
        (StatusesOn(target), StatusesOn(beside))
            .Should()
            .BeEquivalentTo((new[] { "status.burning" }, new[] { "status.burning" }));
    }

    [Fact]
    public async Task FrostNova_HitsAndSlowsAround()
    {
        await HeroAsync("class.mage");
        Combatant wolf = Wolf();

        await CastAsync("skill.mage.frost_nova");

        (wolf.Health, wolf.SlowPercent).Should().Be((490, 60));
    }

    [Fact]
    public async Task Heal_HealsAnAlly()
    {
        Combatant me = await HeroAsync("class.cleric");
        me.Health = 10;

        await CastAsync("skill.cleric.heal", ms: 1500);

        me.Health.Should().BeGreaterThanOrEqualTo(58, "10 + 1.5 × 25 = 47.5, rounded up");
    }

    [Fact]
    public async Task Renew_HealsOverTime()
    {
        Combatant me = await HeroAsync("class.cleric");
        _world.Combat.Hostile(me, _world.Now);
        me.Health = 10;

        // An instant lands at the next tick (250 ms), its first pulse 3 s later.
        await CastAsync("skill.cleric.renew", ms: 3250);

        me.Health.Should().Be(20, "0.4 × 25 after 3 s, in combat");
    }

    [Fact]
    public async Task Aegis_ShieldsAnAlly()
    {
        Combatant me = await HeroAsync("class.cleric");

        await CastAsync("skill.cleric.aegis");

        me.Get<StatusSet>()!.All.Single().Amount.Should().Be(70, "20 + 2 × 25");
    }
}
