using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Monsters.Tests;

/// <summary>
/// Spec M1c §4.3: a ranged monster stops at its attack's reach with a line of sight, and after a move
/// stands still <c>RangedAimMs</c> (250) before it shoots or casts. An archer of reach 5 at (1, 1),
/// player 7 (avatar 1).
/// </summary>
public sealed class RangedBehaviorTests
{
    private static readonly SkillDefinition Arrow = new()
    {
        Id = "skill.monster.arrow",
        Kind = "skill",
        Classes = [],
        Level = 1,
        Type = SkillType.Instant,
        Range = 5,
        Target = SkillTarget.Enemy,
        Effects = [new SkillEffectDefinition { Kind = "damage", Base = 1 }],
    };

    private readonly MonsterWorld _world = new();
    private readonly RecordingCasting _casting = new();

    private int Archer => _world.Zone.Spawned[0].ObjectId;

    private Combatant ArcherCombatant => _world.Simulation.Combat.Combatants[Archer];

    private async Task StartAsync(int playerX, bool skill = false)
    {
        MonsterDefinition archer = MonsterWorld.Wolf with
        {
            Id = "monster.test.archer",
            Behavior = "ranged",
            AttackRange = 5,
            Skills = skill ? [new MonsterSkillRule { Skill = Arrow.Id }] : [],
        };
        _world.Start(
            [MonsterWorld.WolvesAt(1, (1, 1)) with { Monster = archer.Id, AggroRadius = 9 }],
            [new MeleeBehavior(), new RangedBehavior(Options.Create(_world.Config))],
            [_casting],
            archer,
            Arrow
        );
        await _world.EnterAsync(7, 1, playerX, 1);
    }

    [Fact]
    public async Task OutOfReach_ItClosesToItsReach()
    {
        await StartAsync(playerX: 9);
        await _world.TickAsync();

        _world.Zone.WalkRanges.Last().Should().Be(5);
    }

    [Fact]
    public async Task OutOfSight_ItKeepsApproaching()
    {
        await StartAsync(playerX: 5);
        await _world.TickAsync();
        _world.Zone.Walks.Should().BeEmpty();

        _world.Zone.Blind.Add(((1, 1), (5, 1)));
        _world.Zone.NowMs += 250;
        await _world.TickAsync();

        _world.Zone.Walks.Should().Contain(w => w.ObjectId == Archer && w.X == 5);
    }

    [Fact]
    public async Task JustStopped_ItAimsBeforeShooting()
    {
        await StartAsync(playerX: 5, skill: true);
        await _world.TickAsync();
        _casting.Casts.Clear();
        _world.Zone.Walking.Add(Archer);
        long now = _world.Zone.NowMs;

        await _world.TickAsync();

        ArcherCombatant.NextSwingAtMs.Should().BeGreaterThanOrEqualTo(now + 250);
        _casting.Casts.Should().BeEmpty();
    }

    [Fact]
    public async Task OnceAimed_ItCasts()
    {
        await StartAsync(playerX: 5, skill: true);
        await _world.TickAsync();
        _world.Zone.Walking.Add(Archer);
        await _world.TickAsync();
        _casting.Casts.Clear();

        _world.Zone.NowMs += 250;
        await _world.TickAsync();

        _casting.Casts.Select(c => c.SkillId).Should().Equal(Arrow.Id);
    }

    [Fact]
    public async Task StandingStill_ItShootsFromAfar()
    {
        await StartAsync(playerX: 5);
        Combatant me = _world.Simulation.Combat.Combatants[1];

        for (int i = 0; i < 12; i++)
        {
            _world.Zone.NowMs += 250;
            await _world.TickAsync();
        }

        me.Health.Should().BeLessThan(me.MaxHealth);
        _world.Zone.Walks.Should().NotContain(w => w.ObjectId == Archer);
    }
}
