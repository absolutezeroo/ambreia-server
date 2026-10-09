using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1b §5.2: each effect type on its own — damage and heal from the caster's power, a status, an
/// interrupt, a dispel, extra threat — in the order of the skill, never on a target already dead.
/// Player 7 (avatar 1, Power 25) next to a wolf of 100 health and 0 armor.
/// </summary>
public sealed class EffectTests
{
    private static readonly StatusDefinition Burning = new()
    {
        Id = "status.burning",
        Kind = "status",
        Type = StatusType.Dot,
        DurationMs = 6000,
        PeriodMs = 2000,
        Coefficient = 0.2,
    };

    private readonly List<string> _interrupts = [];
    private readonly SkillWorld _world;

    public EffectTests() => _world = new SkillWorld([new InterruptRecorder(_interrupts)], Burning);

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

    private Combatant ToughWolf(double dodge = 0) =>
        _world.Wolf(
            definition: SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with { Health = 100, DodgePercent = dodge },
            }
        );

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

    private static SkillDefinition Skill(params SkillEffectDefinition[] effects) =>
        new()
        {
            Id = "skill.test",
            Kind = "skill",
            Classes = ["class.mage"],
            Level = 1,
            Type = SkillType.Instant,
            Range = 5,
            Target = SkillTarget.Enemy,
            Effects = [.. effects],
        };

    private static readonly SkillEffectDefinition Firebolt = new()
    {
        Kind = "damage",
        Base = 8,
        Coefficient = 1.8,
    };

    /// <summary>What the effects reported, sent with the next tick.</summary>
    private async Task<IEnumerable<RpgSkillOutcome>> OutcomesAsync()
    {
        await _world.TickAsync(0);

        return _world.Broadcast<RpgSkillEventsMessageComposer>().SelectMany(e => e.Outcomes);
    }

    [Fact]
    public async Task Damage_IsBasePlusCoefficientTimesPower()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        SkillContext ctx = Context(me, Skill(Firebolt));

        Effects.Apply(ctx, wolf);

        wolf.Health.Should().Be(47, "8 + 1.8 × 25 = 53");
        ctx.DamageDone[wolf.ObjectId].Should().Be(53);
        (await OutcomesAsync())
            .Single()
            .Should()
            .BeEquivalentTo(
                new
                {
                    Amount = 53,
                    Outcome = "hit",
                    SourceId = "skill.test",
                }
            );
    }

    [Fact]
    public async Task Damage_OnAMonsterGoingHome_IsNotReported()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        wolf.Invulnerable = true;

        Effects.Apply(Context(me, Skill(Firebolt)), wolf);

        (wolf.Health, (await OutcomesAsync()).Count()).Should().Be((100, 0));
    }

    [Fact]
    public async Task ADodge_LandsNothing_UnlessTheSkillIsUnavoidable()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf(dodge: 100);

        Effects.Apply(Context(me, Skill(Firebolt)), wolf);
        wolf.Health.Should().Be(100);

        Effects.Apply(Context(me, Skill(Firebolt) with { Unavoidable = true }), wolf);
        wolf.Health.Should().Be(47);

        (await OutcomesAsync()).Select(o => o.Outcome).Should().Equal("dodge", "hit");
    }

    [Fact]
    public async Task AHeal_IsBounded_AndACritHealsOneAndAHalf()
    {
        Combatant me = await _world.PlayerAsync(critPercent: 100);
        me.Health = 10;

        Effects.Apply(
            Context(
                me,
                Skill(
                    new SkillEffectDefinition
                    {
                        Kind = "heal",
                        Base = 10,
                        Coefficient = 1.5,
                    }
                )
            ),
            me
        );

        me.Health.Should().Be(81, "(10 + 1.5 × 25) × 1.5 = 71");
        (await OutcomesAsync())
            .Single()
            .Should()
            .BeEquivalentTo(new { Amount = 71, Outcome = "heal" });
    }

    [Fact]
    public async Task AHeal_SpreadsThreat_ToWhoeverThreatensTheHealed()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant ally = await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);
        Combatant wolf = ToughWolf();
        wolf.Threat.Add(2, 1);
        ally.Health = 50;

        Effects.Apply(
            Context(me, Skill(new SkillEffectDefinition { Kind = "heal", Base = 20 })),
            ally
        );

        wolf.Threat.ThreatOf(1).Should().Be(10, "50 % of the 20 healed");
    }

    [Fact]
    public async Task ApplyStatus_PutsTheStatus()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        Effects.Apply(
            Context(
                me,
                Skill(
                    new SkillEffectDefinition { Kind = "apply_status", Status = "status.burning" }
                )
            ),
            wolf
        );

        wolf.Get<StatusSet>()!.All.Single().Definition.Id.Should().Be("status.burning");
    }

    [Fact]
    public async Task Interrupt_BreaksTheTargetsCast()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant other = await _world.PlayerAsync(player: 8, objectId: 2, x: 4, y: 5);

        Effects.Apply(Context(me, Skill(new SkillEffectDefinition { Kind = "interrupt" })), other);

        _interrupts.Should().Equal("interrupt 2");
    }

    [Fact]
    public async Task Dispel_TakesTheNewestDispellableDebuffsFromAnAlly()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        StatusDefinition Debuff(string id, bool dispellable = true) =>
            new()
            {
                Id = id,
                Kind = "status",
                Type = StatusType.Debuff,
                DurationMs = 9000,
                Dispellable = dispellable,
                Modifiers = [new StatModifierDefinition { Stat = ModifierStat.Armor, Flat = -1 }],
            };
        _world.Statuses.Apply(_world.Combat, wolf, me, Debuff("status.a"), _world.Now);
        _world.Statuses.Apply(_world.Combat, wolf, me, Debuff("status.b"), _world.Now);
        _world.Statuses.Apply(
            _world.Combat,
            wolf,
            me,
            Debuff("status.c", dispellable: false),
            _world.Now
        );

        Effects.Apply(
            Context(me, Skill(new SkillEffectDefinition { Kind = "dispel", Amount = 1 })),
            me
        );

        me.Get<StatusSet>()!
            .All.Select(s => s.Definition.Id)
            .Should()
            .Equal("status.a", "status.c");
    }

    [Fact]
    public async Task Threat_AddsAPercentOfThisSkillsDamage()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();

        Effects.Apply(
            Context(
                me,
                Skill(Firebolt, new SkillEffectDefinition { Kind = "threat", ThreatPercent = 50 })
            ),
            wolf
        );

        wolf.Threat.ThreatOf(1).Should().Be(53 + 26);
    }

    [Fact]
    public async Task ADeadTarget_GetsNoFurtherEffect()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();

        Effects.Apply(
            Context(
                me,
                Skill(
                    Firebolt,
                    new SkillEffectDefinition { Kind = "apply_status", Status = "status.burning" }
                )
            ),
            wolf
        );

        wolf.IsDead.Should().BeTrue();
        wolf.Get<StatusSet>().Should().BeNull();
    }

    [Fact]
    public async Task AnUndeclaredEffect_OrAMissingStatus_IsSkipped_WithOneWarning()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = ToughWolf();
        SkillDefinition skill = Skill(
            new SkillEffectDefinition { Kind = "push" },
            new SkillEffectDefinition { Kind = "apply_status", Status = "status.gone" },
            Firebolt
        );

        Effects.Apply(Context(me, skill), wolf);
        Effects.Apply(Context(me, skill), wolf);

        Effects.Warned.Should().BeEquivalentTo("push", "status.gone");
        wolf.Health.Should().Be(0, "the rest still applies: two firebolts of 53 on 100");
    }

    [Fact]
    public void EachEffect_SaysWhetherItIsOffensive()
    {
        TestContent content = new(Burning);

        Effects.Find("damage")!.IsOffensive(Firebolt, content).Should().BeTrue();
        Effects
            .Find("heal")!
            .IsOffensive(new SkillEffectDefinition { Kind = "heal", Base = 1 }, content)
            .Should()
            .BeFalse();
        Effects
            .Find("apply_status")!
            .IsOffensive(
                new SkillEffectDefinition { Kind = "apply_status", Status = "status.burning" },
                content
            )
            .Should()
            .BeTrue();
    }

    /// <summary>Stands for the casting system: records what an interrupt breaks.</summary>
    private sealed class InterruptRecorder(List<string> log) : ICastingSystem
    {
        public int Order => 200;

        public void Tick(CombatZone zone, long now) { }

        public string? Cast(CombatZone zone, CastRequest request) => null;

        public void Cancel(CombatZone zone, PlayerId player) { }

        public void Interrupt(CombatZone zone, Combatant c) => log.Add($"interrupt {c.ObjectId}");
    }
}
