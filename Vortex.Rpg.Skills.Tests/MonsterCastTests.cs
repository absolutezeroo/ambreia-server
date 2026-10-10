using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1c §4.2: a monster casts through the same machine as a player — same checks, cast, effects
/// and stops — without mana, approach or action bar. Mage 7 (avatar 1) at (5, 5).
/// </summary>
public sealed class MonsterCastTests
{
    private static readonly SkillDefinition Spit = CastTests.Skill(
        "skill.monster.spit",
        SkillType.Cast,
        range: 4,
        cost: 30,
        castMs: 1000
    ) with
    {
        Classes = [],
    };

    private static readonly SkillDefinition Bite = CastTests.Skill(
        "skill.monster.bite",
        range: 4,
        cost: 0,
        cooldownMs: 5000
    ) with
    {
        Classes = [],
    };

    private static readonly SkillDefinition Snap = CastTests.Skill(
        "skill.monster.snap",
        range: 4,
        cost: 0
    ) with
    {
        Classes = [],
    };

    private static readonly SkillDefinition Mend = CastTests.Skill(
        "skill.monster.mend",
        target: SkillTarget.Ally,
        range: 4,
        cost: 0,
        effect: "heal"
    ) with
    {
        Classes = [],
    };

    private static readonly SkillDefinition Cure = CastTests.Skill(
        "skill.mage.cure",
        target: SkillTarget.Ally,
        effect: "heal"
    );

    private static readonly SkillDefinition Stomp = CastTests.Skill(
        "skill.monster.stomp",
        target: SkillTarget.AroundCaster,
        range: 0,
        cost: 0,
        radius: 2
    ) with
    {
        Classes = [],
    };

    private readonly SkillWorld _world = SkillWorld.WithCasting(
        Spit,
        Bite,
        Snap,
        Mend,
        Cure,
        Stomp,
        CastTests.Stunned
    );

    [Fact]
    public async Task AMonstersArea_SparesAPlayerOnASafeCell()
    {
        Combatant safe = await _world.PlayerAsync(x: 6, y: 5);
        Combatant open = await _world.PlayerAsync(player: 8, objectId: 2, x: 8, y: 5);
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        _world.Zone.SafeCells.Add((6, 5));

        MonsterCast(wolf, Stomp, 0).Should().BeNull();
        await _world.TickAsync();

        _world
            .Broadcast<RpgSkillEventsMessageComposer>()
            .SelectMany(e => e.Outcomes)
            .Select(o => o.TargetObjectId)
            .Should()
            .Equal(open.ObjectId);
        safe.Health.Should().Be(safe.MaxHealth);
    }

    [Fact]
    public async Task AMonster_NeverAimsAtAnotherMonster()
    {
        await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        Combatant other = _world.Wolf(x: 8, y: 5);

        MonsterCast(wolf, Spit, other.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
    }

    private string? MonsterCast(Combatant caster, SkillDefinition skill, int target) =>
        _world.Combat.Cast(new CastRequest(caster.ObjectId, skill.Id, target, -1, -1));

    [Fact]
    public async Task AMonster_CastsWithoutMana_AndHitsThePlayer()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);

        MonsterCast(wolf, Spit, me.ObjectId).Should().BeNull();
        await _world.RunAsync(1250);

        me.Health.Should().BeLessThan(me.MaxHealth);
        wolf.Mana.Should().Be(0);
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.CasterObjectId == wolf.ObjectId && e.Reason == RpgCastEnd.Done);
    }

    [Fact]
    public async Task OutOfRange_AMonsterIsRefused_AndDoesNotWalk()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 15, y: 5);

        MonsterCast(wolf, Spit, me.ObjectId).Should().Be(RpgSkillResultMessageComposer.Unreachable);
        _world.Zone.Walks.Should().BeEmpty();
    }

    [Fact]
    public async Task OutOfSight_AMonsterIsRefused()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        _world.Zone.Blind.Add(((7, 5), (5, 5)));

        MonsterCast(wolf, Spit, me.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.NoLineOfSight);
    }

    [Fact]
    public async Task AMonster_KeepsItsCooldownsAndTheGlobalCooldown()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);

        MonsterCast(wolf, Bite, me.ObjectId).Should().BeNull();
        MonsterCast(wolf, Snap, me.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.GlobalCooldown);
        await _world.RunAsync(1500);

        MonsterCast(wolf, Bite, me.ObjectId).Should().Be(RpgSkillResultMessageComposer.Cooldown);
        MonsterCast(wolf, Snap, me.ObjectId).Should().BeNull();
    }

    [Fact]
    public async Task AStun_InterruptsAMonstersCast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        MonsterCast(wolf, Spit, me.ObjectId);
        await _world.TickAsync();

        _world.Statuses.Apply(_world.Combat, me, wolf, CastTests.Stunned, _world.Now);
        await _world.RunAsync(1250);

        me.Health.Should().Be(me.MaxHealth);
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.Reason == RpgCastEnd.Interrupted);
    }

    [Fact]
    public async Task AHit_DoesNotInterruptAMonstersCast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        MonsterCast(wolf, Spit, me.ObjectId);
        await _world.TickAsync();

        _world.Combat.Damage(me, wolf, 5, _world.Now);
        await _world.RunAsync(1000);

        me.Health.Should().BeLessThan(me.MaxHealth);
    }

    [Fact]
    public async Task AReset_CancelsTheCast_AndClearsTheCooldowns()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        MonsterCast(wolf, Bite, me.ObjectId);
        await _world.RunAsync(1500);
        MonsterCast(wolf, Spit, me.ObjectId).Should().BeNull();

        _world.Combat.ResetCasts(wolf);
        await _world.RunAsync(1250);

        int afterBite = me.Health;
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.SkillId == Spit.Id && e.Reason == RpgCastEnd.Cancelled);
        MonsterCast(wolf, Bite, me.ObjectId).Should().BeNull();
        await _world.TickAsync();
        me.Health.Should().BeLessThan(afterBite);
    }

    [Fact]
    public async Task AMonsterDyingMidCast_CancelsIt()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        MonsterCast(wolf, Spit, me.ObjectId);
        await _world.TickAsync();

        _world.Combat.Damage(me, wolf, 1000, _world.Now);
        await _world.RunAsync(1250);

        me.Health.Should().Be(me.MaxHealth);
        _world
            .Broadcast<RpgCastEndMessageComposer>()
            .SelectMany(e => e.Ends)
            .Should()
            .ContainSingle(e => e.Reason == RpgCastEnd.Cancelled);
    }

    [Fact]
    public async Task AMonstersRefusals_AreNotCounted_ItsCastsAre()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);
        MonsterCast(wolf, Bite, me.ObjectId);
        MonsterCast(wolf, Snap, me.ObjectId);
        await _world.TickAsync();

        _world.Counted.Should().Equal("cast skill.monster.bite resolved");
    }

    [Fact]
    public async Task AMonstersAllySkill_HealsAMonster_NeverAPlayer()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant shaman = _world.Wolf(x: 7, y: 5);
        Combatant hurt = _world.Wolf(x: 8, y: 5);
        hurt.Health = 10;
        me.Health = 50;

        MonsterCast(shaman, Mend, me.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
        MonsterCast(shaman, Mend, hurt.ObjectId).Should().BeNull();
        await _world.TickAsync();

        hurt.Health.Should().BeGreaterThan(10);
        _world
            .Broadcast<RpgSkillEventsMessageComposer>()
            .SelectMany(e => e.Outcomes)
            .Should()
            .OnlyContain(o => o.TargetObjectId == hurt.ObjectId);
    }

    [Fact]
    public async Task APlayersAllySkill_NeverHealsAMonster()
    {
        await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(x: 7, y: 5);

        _world
            .Cast(Cure.Id, wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.InvalidTarget);
    }
}
