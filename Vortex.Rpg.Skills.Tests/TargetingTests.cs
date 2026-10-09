using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Skills.Targeting;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>Spec M1b §6.1, §6.6: what each target mode accepts and touches — an area's camp, radius,
/// cap, order and sight; no offensive skill from or onto a safe cell. Player 7 (avatar 1) at (5, 5).</summary>
public sealed class TargetingTests
{
    private readonly SkillWorld _world = new();

    private SkillEffects Effects { get; } =
        new(
            [
                new DamageEffect(),
                new HealEffect(),
                new ApplyStatusEffect(),
                new InterruptEffect(),
                new DispelEffect(),
                new ThreatEffect(),
            ],
            NullLogger<SkillEffects>.Instance
        );

    private static SkillDefinition Skill(
        SkillTarget target,
        string effect = "damage",
        int radius = 0,
        int maxTargets = 10
    ) =>
        new()
        {
            Id = "skill.test",
            Kind = "skill",
            Classes = ["class.mage"],
            Level = 1,
            Type = SkillType.Instant,
            Range = 5,
            Target = target,
            Radius = radius,
            MaxTargets = maxTargets,
            Effects = [new SkillEffectDefinition { Kind = effect, Base = 1 }],
        };

    private SkillContext Context(Combatant caster, SkillDefinition skill) =>
        new()
        {
            Zone = _world.Combat,
            Caster = caster,
            Skill = skill,
            Now = _world.Now,
            Hits = _world.Hits,
            Statuses = _world.Statuses,
            Config = _world.Config,
            Content = _world.Content,
            Effects = Effects,
        };

    private int[] Targets(
        Combatant caster,
        SkillDefinition skill,
        int target = 0,
        (int X, int Y)? cell = null
    ) => [.. SkillTargets.Resolve(Context(caster, skill), target, cell).Select(c => c.ObjectId)];

    private string? Check(
        Combatant caster,
        SkillDefinition skill,
        int target = 0,
        (int X, int Y)? cell = null
    ) =>
        TargetCheck.Validate(
            _world.Combat,
            skill,
            Effects.IsOffensive(skill, _world.Content),
            caster,
            target,
            cell
        );

    [Fact]
    public async Task Self_IsTheCaster()
    {
        Combatant me = await _world.PlayerAsync();

        Targets(me, Skill(SkillTarget.Self, "heal")).Should().Equal(1);
    }

    [Fact]
    public async Task AllyZero_IsTheCaster_AndAnyOtherPlayerIsAnAlly()
    {
        Combatant me = await _world.PlayerAsync();
        await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);

        Targets(me, Skill(SkillTarget.Ally, "heal")).Should().Equal(1);
        Targets(me, Skill(SkillTarget.Ally, "heal"), target: 2).Should().Equal(2);
    }

    [Fact]
    public async Task AnAlly_IsNeverAMonster_AndAnEnemyNeverAPlayer()
    {
        Combatant me = await _world.PlayerAsync();
        await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);
        Combatant wolf = _world.Wolf();

        Check(me, Skill(SkillTarget.Ally, "heal"), target: wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
        Check(me, Skill(SkillTarget.Enemy), target: 2)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
        Check(me, Skill(SkillTarget.Enemy), target: wolf.ObjectId).Should().BeNull();
    }

    [Fact]
    public async Task AnInvulnerableOrDeadEnemy_IsInvalid()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        wolf.Invulnerable = true;

        Check(me, Skill(SkillTarget.Enemy), target: wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);

        wolf.Invulnerable = false;
        wolf.Health = 0;

        Check(me, Skill(SkillTarget.Enemy), target: wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
    }

    [Fact]
    public async Task TheGround_MustBeAWalkableCell()
    {
        Combatant me = await _world.PlayerAsync();
        _world.Zone.Walls.Add((7, 7));

        Check(me, Skill(SkillTarget.Ground, radius: 1), cell: (7, 7))
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
        Check(me, Skill(SkillTarget.Ground, radius: 1), cell: (-1, -1))
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
        Check(me, Skill(SkillTarget.Ground, radius: 1), cell: (7, 6)).Should().BeNull();
    }

    [Fact]
    public async Task AnArea_HitsTheNearestFirst_UpToMaxTargets()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant far = _world.Wolf(8, 5);
        Combatant near = _world.Wolf(6, 5);
        Combatant middle = _world.Wolf(7, 5);

        Targets(me, Skill(SkillTarget.Ground, radius: 2, maxTargets: 2), cell: (6, 5))
            .Should()
            .Equal(near.ObjectId, middle.ObjectId);
        Targets(me, Skill(SkillTarget.AroundCaster, radius: 3))
            .Should()
            .Equal(near.ObjectId, middle.ObjectId, far.ObjectId);
    }

    [Fact]
    public async Task AnArea_SkipsTheDead_AndThoseOutOfSight()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant dead = _world.Wolf(6, 5);
        dead.Health = 0;
        Combatant hidden = _world.Wolf(6, 6);
        _world.Zone.Blind.Add(((5, 5), (6, 6)));
        Combatant seen = _world.Wolf(4, 4);

        Targets(me, Skill(SkillTarget.AroundCaster, radius: 2)).Should().Equal(seen.ObjectId);
    }

    [Fact]
    public async Task AnOffensiveArea_NeverHitsPlayers_AndAHealingAreaNeverMonsters()
    {
        Combatant me = await _world.PlayerAsync();
        await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);
        Combatant wolf = _world.Wolf();

        Targets(me, Skill(SkillTarget.AroundCaster, radius: 2)).Should().Equal(wolf.ObjectId);
        Targets(me, Skill(SkillTarget.AroundCaster, "heal", radius: 2))
            .Should()
            .BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task AroundTheTarget_IsCentredOnIt()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant target = _world.Wolf(8, 5);
        Combatant beside = _world.Wolf(9, 5);
        _world.Wolf(6, 5);

        Targets(me, Skill(SkillTarget.AroundTarget, radius: 1), target: target.ObjectId)
            .Should()
            .Equal(target.ObjectId, beside.ObjectId);
    }

    [Fact]
    public async Task AnOffensiveSkill_FromOrOntoASafeCell_IsRefused()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();

        _world.Zone.SafeCells.Add((5, 5));
        Check(me, Skill(SkillTarget.Enemy), target: wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.SafeZone);

        _world.Zone.SafeCells.Clear();
        _world.Zone.SafeCells.Add((6, 5));
        Check(me, Skill(SkillTarget.Enemy), target: wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.SafeZone);
        Check(me, Skill(SkillTarget.Ground, radius: 1), cell: (6, 5))
            .Should()
            .Be(RpgSkillResultMessageComposer.SafeZone);
    }

    [Fact]
    public async Task AHeal_IsAllowedOnASafeCell()
    {
        Combatant me = await _world.PlayerAsync();
        _world.Zone.SafeCells.Add((5, 5));

        Check(me, Skill(SkillTarget.Ally, "heal")).Should().BeNull();
    }
}
