using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Skills.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Skills.Tests;

/// <summary>
/// Spec M1b §7: statuses stack by their rule, pulse on their period, change stats while they last,
/// absorb, stun and slow; they end on expiry, on death and on leaving, the persistent ones carried
/// out and back. Player 7 (avatar 1, Power 25) next to a wolf.
/// </summary>
public sealed class StatusTests
{
    private static StatusDefinition Status(
        string id,
        StatusType type,
        StatusStacking stacking = StatusStacking.Refresh,
        int durationMs = 6000,
        int periodMs = 0,
        int maxStacks = 0,
        double baseAmount = 0,
        double coefficient = 0,
        int slowPercent = 0,
        bool persistent = false,
        params StatModifierDefinition[] modifiers
    ) =>
        new()
        {
            Id = id,
            Kind = "status",
            Type = type,
            Stacking = stacking,
            DurationMs = durationMs,
            PeriodMs = periodMs,
            MaxStacks = maxStacks,
            Base = baseAmount,
            Coefficient = coefficient,
            SlowPercent = slowPercent,
            Persistent = persistent,
            Modifiers = [.. modifiers],
        };

    private static readonly StatusDefinition Burning = Status(
        "status.burning",
        StatusType.Dot,
        StatusStacking.Stack,
        periodMs: 2000,
        maxStacks: 3,
        coefficient: 0.2
    );

    private static readonly StatusDefinition Renewed = Status(
        "status.renewed",
        StatusType.Hot,
        durationMs: 12000,
        periodMs: 3000,
        coefficient: 0.4
    );

    private static readonly StatusDefinition Stunned = Status(
        "status.stunned",
        StatusType.Stun,
        StatusStacking.Ignore,
        durationMs: 2000
    );

    private readonly SkillWorld _world = new();

    private bool Apply(Combatant caster, Combatant target, StatusDefinition status) =>
        _world.Statuses.Apply(_world.Combat, caster, target, status, _world.Now);

    private static IReadOnlyList<ActiveStatus> On(Combatant c) => c.Get<StatusSet>()?.All ?? [];

    private IEnumerable<RpgSkillOutcome> Outcomes() =>
        _world.Broadcast<RpgSkillEventsMessageComposer>().SelectMany(e => e.Outcomes);

    [Fact]
    public async Task Replace_PutsTheNewOne_WithItsOwnAmount()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        StatusDefinition burn = Burning with { Stacking = StatusStacking.Replace };

        Apply(me, wolf, burn);
        me.Power = 50;
        Apply(me, wolf, burn);

        On(wolf).Should().ContainSingle().Which.Amount.Should().Be(10, "0.2 × 50");
    }

    [Fact]
    public async Task Refresh_RestartsTheDuration_KeepsTheAmount()
    {
        Combatant me = await _world.PlayerAsync();
        Apply(me, me, Renewed);
        await _world.RunAsync(5000);
        me.Power = 50;

        Apply(me, me, Renewed);

        ActiveStatus renewed = On(me).Single();
        (renewed.EndsAtMs, renewed.Amount).Should().Be((_world.Now + 12000, 10));
    }

    [Fact]
    public async Task Stack_AddsUpToMax_AndMultipliesThePulse()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();

        for (int i = 0; i < 4; i++)
        {
            Apply(me, wolf, Burning);
        }

        On(wolf).Single().Stacks.Should().Be(3);

        await _world.RunAsync(2000);

        Outcomes()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    SourceId = "status.burning",
                    Amount = 15,
                    Outcome = RpgSkillOutcome.Hit,
                },
                "0.2 × 25 × 3 stacks"
            );
    }

    [Fact]
    public async Task Ignore_KeepsTheFirst()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Stunned);
        long ends = On(wolf).Single().EndsAtMs;
        await _world.TickAsync(1000);

        Apply(me, wolf, Stunned).Should().BeFalse();

        On(wolf).Single().EndsAtMs.Should().Be(ends);
    }

    [Fact]
    public async Task PastTheCap_TheOldestGoes()
    {
        SkillWorld world = new();
        Combatant me = await world.PlayerAsync();
        int cap = world.Config.MaxStatusesPerCombatant;

        for (int i = 0; i <= cap; i++)
        {
            world.Statuses.Apply(
                world.Combat,
                me,
                me,
                Status($"status.s{i}", StatusType.Buff, modifiers: Crit(1)),
                world.Now
            );
        }

        On(me).Should().HaveCount(cap);
        On(me).Select(s => s.Definition.Id).Should().NotContain("status.s0");
    }

    [Fact]
    public async Task ADot_PulsesOnItsPeriod_AndGivesThreatAndParticipation()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Burning);

        await _world.RunAsync(6000);

        Outcomes().Select(o => o.Amount).Should().Equal(5, 5, 5);
        wolf.Participants.Should().Contain(new PlayerId(7));
        wolf.Threat.ThreatOf(1).Should().BeGreaterThan(0);
        On(wolf).Should().BeEmpty("6 s: it ran out");
    }

    [Fact]
    public async Task AHot_HealsOnItsPeriod_AndSpreadsHealThreat()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        wolf.Threat.Add(1, 4);
        // In combat: out of it he would also regenerate.
        _world.Combat.Hostile(me, _world.Now);
        me.Health = 50;
        Apply(me, me, Renewed);

        await _world.RunAsync(3000);

        me.Health.Should().Be(60, "0.4 × 25");
        Outcomes().Single().Outcome.Should().Be(RpgSkillOutcome.Heal);
        wolf.Threat.ThreatOf(1).Should().Be(4 + 5, "50 % of the 10 healed");
    }

    [Fact]
    public async Task ABuff_RaisesCrit_UntilItExpires()
    {
        Combatant me = await _world.PlayerAsync(critPercent: 5);

        Apply(
            me,
            me,
            Status("status.focused", StatusType.Buff, durationMs: 1000, modifiers: Crit(20))
        );

        me.CritPercent.Should().Be(25);

        await _world.RunAsync(1000);

        me.CritPercent.Should().Be(5);
    }

    [Fact]
    public async Task ADebuff_LowersTheDamageDealt()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(
            me,
            wolf,
            Status(
                "status.weakened",
                StatusType.Debuff,
                modifiers: new StatModifierDefinition
                {
                    Stat = ModifierStat.DamageDealtPercent,
                    Flat = -50,
                }
            )
        );

        _world.Combat.SetMonsterTarget(wolf.ObjectId, 1);
        await _world.TickAsync();

        _world
            .Broadcast<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Single(h => h.AttackerId == wolf.ObjectId)
            .Amount.Should()
            .Be(2, "a roll of 4, halved");
    }

    [Fact]
    public async Task ASlow_UsesTheStrongest()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();

        Apply(me, wolf, Status("status.chilled", StatusType.Slow, slowPercent: 30));
        Apply(me, wolf, Status("status.frozen", StatusType.Slow, slowPercent: 60));

        (wolf.SlowPercent, _world.Zone.MoveSpeeds[wolf.ObjectId]).Should().Be((60, 40));
    }

    /// <summary>Review Focus 3: stunned mid-fight, nothing swings or walks; when it ends, the fight
    /// goes on.</summary>
    [Fact]
    public async Task AStun_StopsSwingsAndWalk_ThenEverythingResumes()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        _world.Combat.StartAttack(new PlayerId(7), wolf.ObjectId);
        Apply(wolf, me, Stunned);

        await _world.RunAsync(1750);

        _world.Zone.Rooted.Should().Contain(1);
        _world
            .Broadcast<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Should()
            .BeEmpty();

        await _world.RunAsync(500);

        _world.Zone.Rooted.Should().NotContain(1);
        _world
            .Broadcast<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Should()
            .Contain(h => h.AttackerId == 1);
    }

    /// <summary>Review Focus 5: damage taken +50 % and a crit come first, then the shield takes what
    /// it can and breaks; the next hit is not absorbed.</summary>
    [Fact]
    public async Task AShield_AbsorbsAfterModifiersAndCrit_ThenBreaks()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(
            definition: SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with
                {
                    DamageMin = 20,
                    DamageMax = 20,
                    CritPercent = 100,
                },
            }
        );
        Apply(
            me,
            me,
            Status("status.aegis", StatusType.Shield, StatusStacking.Ignore, 10000, baseAmount: 20)
        );
        Apply(
            wolf,
            me,
            Status(
                "status.exposed",
                StatusType.Debuff,
                modifiers: new StatModifierDefinition
                {
                    Stat = ModifierStat.DamageTakenPercent,
                    Flat = 50,
                }
            )
        );
        _world.Combat.SetMonsterTarget(wolf.ObjectId, 1);

        await _world.TickAsync();

        RpgCombatHit first = _world
            .Broadcast<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Single();
        first.Amount.Should().Be(25, "20 × 1.5 crit × 1.5 taken = 45, less a shield of 20");
        On(me).Select(s => s.Definition.Id).Should().Equal("status.exposed");

        await _world.RunAsync(2000);

        _world
            .Broadcast<RpgCombatEventsMessageComposer>()
            .SelectMany(e => e.Hits)
            .Last()
            .Amount.Should()
            .Be(45);
    }

    [Fact]
    public async Task Death_DropsEveryStatus()
    {
        Combatant me = await _world.PlayerAsync();
        Apply(me, me, Renewed);
        me.Health = 0;

        await _world.TickAsync();

        On(me).Should().BeEmpty();
        _world
            .Broadcast<RpgStatusesMessageComposer>()
            .Last()
            .Combatants.Single(c => c.ObjectId == 1)
            .Statuses.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task Leaving_DropsTheRest_AndCarriesThePersistent()
    {
        Combatant me = await _world.PlayerAsync();
        Apply(me, me, Renewed);
        Apply(
            me,
            me,
            Status(
                "status.wellfed",
                StatusType.Buff,
                durationMs: 60_000,
                persistent: true,
                modifiers: Crit(1)
            )
        );
        await _world.TickAsync(1000);

        await _world.Combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _world.Calls.Should().Contain("vitals 7: 100/100 status.wellfed@59000");
    }

    [Fact]
    public async Task ACarriedStatus_IsReappliedOnEntry_WithItsTimeLeft()
    {
        StatusDefinition wellFed = Status(
            "status.wellfed",
            StatusType.Buff,
            durationMs: 60_000,
            persistent: true,
            modifiers: Crit(10)
        );
        SkillWorld world = new(null, wellFed);

        Combatant me = await world.PlayerAsync(
            carried:
            [
                new CarriedStatus
                {
                    StatusId = "status.wellfed",
                    RemainingMs = 5000,
                    Stacks = 1,
                    Amount = 0,
                },
            ]
        );

        ActiveStatus back = On(me).Single();
        (back.EndsAtMs, me.CritPercent).Should().Be((world.Now + 5000, 10));
    }

    [Fact]
    public async Task OneStatusesMessagePerTick_ForTheChangedOnly()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Burning);
        Apply(me, wolf, Stunned);
        Apply(me, me, Renewed);

        await _world.TickAsync();

        _world
            .Broadcast<RpgStatusesMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Combatants.Select(c => (c.ObjectId, c.Statuses.Length))
            .Should()
            .BeEquivalentTo([(wolf.ObjectId, 2), (1, 1)]);

        await _world.TickAsync();

        _world
            .Broadcast<RpgStatusesMessageComposer>()
            .Should()
            .ContainSingle("nothing changed since");
    }

    /// <summary>The wolf killed its burner: the dot stops, the ghost is not put back on its table.</summary>
    [Fact]
    public async Task ADot_StopsWhenItsCasterDies()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf(
            definition: SkillWorld.WolfDefinition with
            {
                Base = SkillWorld.WolfDefinition.Base with { Health = 500 },
            }
        );
        Apply(me, wolf, Burning);
        me.Health = 0;
        await _world.TickAsync();

        await _world.RunAsync(2000);

        (wolf.Health, wolf.Threat.Contains(1), On(wolf).Count).Should().Be((500, false, 0));
    }

    /// <summary>A killed monster's body shows no status left.</summary>
    [Fact]
    public async Task AKilledMonster_IsShownWithoutStatuses()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Burning);
        await _world.TickAsync();
        wolf.Health = 0;

        await _world.TickAsync();

        _world
            .Broadcast<RpgStatusesMessageComposer>()
            .Last()
            .Combatants.Should()
            .ContainSingle(c => c.ObjectId == wolf.ObjectId && c.Statuses.IsEmpty);
    }

    /// <summary>A monster walking home takes nothing, and nothing is shown as taken.</summary>
    [Fact]
    public async Task ADot_OnAMonsterGoingHome_LandsNothing()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Burning);
        wolf.Invulnerable = true;

        await _world.RunAsync(2000);

        (wolf.Health, Outcomes().Count()).Should().Be((40, 0));
    }

    [Fact]
    public async Task TheEntryBurst_CarriesStatuses()
    {
        Combatant me = await _world.PlayerAsync();
        Combatant wolf = _world.Wolf();
        Apply(me, wolf, Burning);

        _world
            .Combat.EntryVitals(new PlayerId(7))
            .OfType<RpgStatusesMessageComposer>()
            .Single()
            .Combatants.Single()
            .ObjectId.Should()
            .Be(wolf.ObjectId);
    }

    private static StatModifierDefinition Crit(double flat) =>
        new() { Stat = ModifierStat.CritPercent, Flat = flat };
}
