using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1b §6.1–§6.5: a cast's checks in their order, the approach, an instant on the next tick, a
/// cast at its end, a channel by pulses, the automatic attack paused meanwhile, the global cooldown
/// and the cooldowns. Mage 7 (avatar 1, Power 25, mana 100) at (5, 5), a tough wolf at (6, 5).
/// </summary>
public sealed class CastTests
{
    internal static readonly StatusDefinition Stunned = new()
    {
        Id = "status.stunned",
        Kind = "status",
        Type = StatusType.Stun,
        DurationMs = 2000,
        Stacking = StatusStacking.Ignore,
    };

    internal static SkillDefinition Skill(
        string id,
        SkillType type = SkillType.Instant,
        SkillTarget target = SkillTarget.Enemy,
        int range = 5,
        int cost = 5,
        int cooldownMs = 0,
        int castMs = 0,
        int channelMs = 0,
        int pulseMs = 0,
        int radius = 0,
        int level = 1,
        string cls = "class.mage",
        bool globalCooldown = true,
        bool moveAllowed = false,
        string effect = "damage"
    ) =>
        new()
        {
            Id = id,
            Kind = "skill",
            Classes = [cls],
            Level = level,
            Type = type,
            Target = target,
            Range = range,
            Cost = cost,
            CooldownMs = cooldownMs,
            CastMs = castMs,
            ChannelMs = channelMs,
            PulseMs = pulseMs,
            Radius = radius,
            GlobalCooldown = globalCooldown,
            MoveAllowed = moveAllowed,
            Effects = [new SkillEffectDefinition { Kind = effect, Base = 10 }],
        };

    internal static readonly SkillDefinition Zap = Skill("skill.mage.zap", cooldownMs: 4000);
    internal static readonly SkillDefinition Spark = Skill("skill.mage.spark");
    internal static readonly SkillDefinition Bolt = Skill(
        "skill.mage.bolt",
        SkillType.Cast,
        cost: 10,
        cooldownMs: 3000,
        castMs: 2000
    );
    internal static readonly SkillDefinition Rain = Skill(
        "skill.mage.rain",
        SkillType.Channel,
        SkillTarget.Ground,
        cost: 15,
        channelMs: 3000,
        pulseMs: 1000,
        radius: 1
    );
    internal static readonly SkillDefinition Focus = Skill(
        "skill.mage.focus",
        target: SkillTarget.Self,
        range: 0,
        globalCooldown: false,
        effect: "heal"
    );
    internal static readonly SkillDefinition Strike = Skill(
        "skill.warrior.strike",
        cls: "class.warrior"
    );
    internal static readonly SkillDefinition Meteor = Skill("skill.mage.meteor", level: 10);

    private readonly SkillWorld _world = SkillWorld.WithCasting(
        Zap,
        Spark,
        Bolt,
        Rain,
        Focus,
        Strike,
        Meteor,
        Stunned
    );

    private Combatant ToughWolf(int x = 6, int y = 5) =>
        _world.Wolf(
            x,
            y,
            SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with { Health = 500 },
            }
        );

    private IEnumerable<RpgSkillOutcome> Outcomes() =>
        _world.Broadcast<RpgSkillEventsMessageComposer>().SelectMany(e => e.Outcomes);

    private IEnumerable<RpgCastEnd> Ends() =>
        _world.Broadcast<RpgCastEndMessageComposer>().SelectMany(e => e.Ends);

    [Fact]
    public async Task ADeadCaster_IsRefusedFirst()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        me.Health = 0;
        me.Mana = 0;

        _world.Cast("skill.nope", wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.Dead);
    }

    [Fact]
    public async Task AStunnedCaster_IsRefused_BeforeTheSkillIsEvenLookedAt()
    {
        Combatant me = await _world.PlayerAsync();
        _world.Statuses.Apply(_world.Combat, me, me, Stunned, _world.Now);

        _world.Cast("skill.nope").Should().Be(RpgSkillResultMessageComposer.Stunned);
    }

    [Fact]
    public async Task ACasterCasting_IsRefused()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId).Should().BeNull();

        _world.Cast(Spark.Id, wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.Casting);
    }

    [Fact]
    public async Task AnUnknownSkill_ThenOneNotLearned_AreRefused()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        _world
            .Cast("skill.nope", wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.UnknownSkill);
        _world.Cast(Strike.Id, wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.NotLearned);
        _world
            .Cast(Meteor.Id, wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.NotLearned, "level 10, he is 6");
    }

    [Fact]
    public async Task AWrongTarget_ThenASafeCell_AreRefused()
    {
        await _world.PlayerAsync();
        await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);
        Combatant wolf = ToughWolf();

        _world.Cast(Spark.Id, 2).Should().Be(RpgSkillResultMessageComposer.InvalidTarget);

        _world.Zone.SafeCells.Add((5, 5));
        _world.Cast(Spark.Id, wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.SafeZone);
    }

    [Fact]
    public async Task ACooldown_ThenTheGlobalCooldown_ThenMana_AreChecked()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Zap.Id, wolf.ObjectId).Should().BeNull();

        _world
            .Cast(Zap.Id, wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.Cooldown, "before the global cooldown");
        _world
            .Cast(Spark.Id, wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.GlobalCooldown);

        await _world.RunAsync(1000);
        me.Mana = 2;

        _world.Cast(Spark.Id, wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.NoMana);
    }

    [Fact]
    public async Task AnInstant_LandsOnTheNextTick()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        _world.Cast(Spark.Id, wolf.ObjectId);

        (wolf.Health, me.Mana).Should().Be((500, 95), "paid at once, landed at the tick");

        await _world.TickAsync();

        wolf.Health.Should().Be(490);
        _world.SentTo<RpgCooldownsMessageComposer>(7).Should().ContainSingle();
    }

    [Fact]
    public async Task ACast_LandsAfterItsTime_ThenTakesManaAndCooldown()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        _world.Cast(Bolt.Id, wolf.ObjectId);
        await _world.TickAsync();

        _world
            .Broadcast<RpgCastStartMessageComposer>()
            .Single()
            .Casts.Single()
            .Should()
            .BeEquivalentTo(
                new
                {
                    CasterObjectId = 1,
                    SkillId = Bolt.Id,
                    DurationMs = 2000,
                    Channel = false,
                    TargetObjectId = wolf.ObjectId,
                }
            );

        await _world.RunAsync(1500);
        (wolf.Health, me.Mana).Should().Be((500, 100));

        await _world.RunAsync(250);

        (wolf.Health, me.Mana).Should().Be((490, 90));
        Ends().Single().Reason.Should().Be(RpgCastEnd.Done);
        _world
            .SentTo<RpgCooldownsMessageComposer>(7)
            .Last()
            .Skills.Single()
            .Should()
            .BeEquivalentTo(new { SkillId = Bolt.Id, RemainingMs = 3000 });
    }

    [Fact]
    public async Task ACastEndingOutOfRange_Fails_WithoutCost()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        _world.Zone.Positions[wolf.ObjectId] = (20, 5);

        await _world.RunAsync(2000);

        Ends().Single().Reason.Should().Be(RpgCastEnd.Failed);
        _world
            .SentTo<RpgSkillResultMessageComposer>(7)
            .Single()
            .Code.Should()
            .Be(RpgSkillResultMessageComposer.Unreachable);
        (wolf.Health, me.Mana).Should().Be((500, 100));
    }

    [Fact]
    public async Task AChannel_PulsesThenEnds_AndIsPaidUpFront()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        _world.Cast(Rain.Id, 0, 6, 5).Should().BeNull();
        me.Mana.Should().Be(85);

        await _world.RunAsync(3000);

        Outcomes().Should().HaveCount(3);
        wolf.Health.Should().Be(470);
        Ends().Single().Reason.Should().Be(RpgCastEnd.Done);
        _world
            .Broadcast<RpgCastStartMessageComposer>()
            .Single()
            .Casts.Single()
            .Channel.Should()
            .BeTrue();
    }

    [Fact]
    public async Task TheAutoAttack_PausesDuringACast_ThenResumes()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Bolt.Id, wolf.ObjectId);
        _world.Combat.StartAttack(new PlayerId(7), wolf.ObjectId);

        await _world.RunAsync(1750);

        Hits().Should().BeEmpty();

        await _world.RunAsync(500);

        Hits().Should().NotBeEmpty();

        IEnumerable<RpgCombatHit> Hits() =>
            _world
                .Broadcast<RpgCombatEventsMessageComposer>()
                .SelectMany(e => e.Hits)
                .Where(h => h.AttackerId == 1);
    }

    [Fact]
    public async Task OutOfRange_ApproachesThenCasts()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf(12, 5);

        _world.Cast(Bolt.Id, wolf.ObjectId).Should().BeNull();

        _world.Zone.Walks.Should().ContainSingle().Which.Should().Be((1, 12, 5, 5));
        _world.Broadcast<RpgCastStartMessageComposer>().Should().BeEmpty();

        _world.Zone.Positions[1] = (8, 5);
        await _world.TickAsync();

        _world.Zone.Walking.Should().NotContain(1);
        _world.Broadcast<RpgCastStartMessageComposer>().Should().ContainSingle();
    }

    [Fact]
    public async Task NoPath_IsUnreachable()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf(12, 5);
        _world.Zone.PathsExist = false;

        _world.Cast(Bolt.Id, wolf.ObjectId).Should().Be(RpgSkillResultMessageComposer.Unreachable);
    }

    [Fact]
    public async Task TheGlobalCooldown_IsShared_ButNotByAnOffGcdSkill()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Spark.Id, wolf.ObjectId);

        _world
            .Cast(Zap.Id, wolf.ObjectId)
            .Should()
            .Be(RpgSkillResultMessageComposer.GlobalCooldown);
        // The spark lands; the global cooldown still runs, and focus is not subject to it.
        await _world.TickAsync();
        _world.Cast(Focus.Id).Should().BeNull();
    }

    /// <summary>Review Focus 4: five casts in one tick — one lands, one is paid, the rest are told.</summary>
    [Fact]
    public async Task ACastSpammedDuringItsGlobalCooldown_ResolvesOnce()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        string?[] answers =
        [
            .. Enumerable.Range(0, 5).Select(_ => _world.Cast(Spark.Id, wolf.ObjectId)),
        ];
        await _world.TickAsync();

        answers
            .Should()
            .Equal(
                null,
                "global_cooldown",
                "global_cooldown",
                "global_cooldown",
                "global_cooldown"
            );
        Outcomes().Should().ContainSingle();
        me.Mana.Should().Be(95);
    }

    /// <summary>An instant paid and waiting for its tick is not replaced by the next packet.</summary>
    [Fact]
    public async Task APaidInstant_IsNotReplaced_BeforeItLands()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Spark.Id, wolf.ObjectId).Should().BeNull();

        _world.Cast(Focus.Id).Should().Be(RpgSkillResultMessageComposer.Casting);
        await _world.TickAsync();

        (wolf.Health, me.Mana).Should().Be((490, 95));
    }

    /// <summary>A cast that does not allow moving stops the walk under way.</summary>
    [Fact]
    public async Task ACastStartedWhileWalking_StopsTheWalk()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Zone.Walking.Add(1);

        _world.Cast(Bolt.Id, wolf.ObjectId).Should().BeNull();

        _world.Zone.Walking.Should().NotContain(1);
    }

    /// <summary>While a cast walks him to its target, the attack does not drag him back to its own.</summary>
    [Fact]
    public async Task ACastApproach_IsNotUndoneByTheAttacksApproach()
    {
        await _world.PlayerAsync();
        Combatant near = ToughWolf();
        Combatant far = ToughWolf(12, 5);
        _world.Combat.StartAttack(new PlayerId(7), near.ObjectId);
        await _world.TickAsync();
        _world.Cast(Spark.Id, far.ObjectId).Should().BeNull();

        _world.Zone.Positions[1] = (5, 7);
        await _world.TickAsync();

        _world.Zone.Walks.Should().NotContain(w => w.X == 6 && w.Y == 5);
    }

    /// <summary>A refusal of an id nobody declared is counted under "unknown": a client string never
    /// becomes a metric label.</summary>
    [Fact]
    public async Task AnUnknownSkillId_IsCountedAsUnknown()
    {
        await _world.PlayerAsync();

        _world.Cast("skill.whatever.1234");

        _world.Counted.Should().Equal("cast unknown unknown_skill");
    }

    [Fact]
    public async Task EachResolvedCast_IsCounted()
    {
        await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        _world.Cast(Spark.Id, wolf.ObjectId);
        _world.Cast(Spark.Id, wolf.ObjectId);

        await _world.TickAsync();

        _world
            .Counted.Should()
            .Contain("cast skill.mage.spark resolved")
            .And.Contain("cast skill.mage.spark global_cooldown");
    }
}
