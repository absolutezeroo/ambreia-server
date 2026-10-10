using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Tests.Support;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>
/// Plan M1b, Task 4: what the zone's combat offers a module without naming it — systems ticked in
/// their order before the hits, parts a module attaches, stats recomputed from a base and every
/// source of modifiers, hit options, shared damage, heal and threat, a queue of messages.
/// Player 7 is avatar 1 at (5, 5), the wolf stands next to him.
/// </summary>
public sealed class CombatExtensionTests
{
    private static readonly CombatConfig Config = new();

    private static readonly MonsterDefinition Wolf = new()
    {
        Id = "monster.forest.wolf",
        Kind = "monster",
        Figure = "0 3 C0A060",
        Base = new MonsterStatBlock
        {
            Health = 40,
            DamageMin = 3,
            DamageMax = 5,
        },
        Xp = 20,
    };

    private readonly FakeZoneRuntime _zone = new();
    private readonly List<string> _log = [];
    private readonly List<ICombatSystem> _systems = [];
    private readonly StatModifiers _modifiers = new();

    private CombatZone _combat = null!;

    private async Task<Combatant> PlayerNextToAWolfAsync()
    {
        _combat = new CombatZone(
            _zone,
            new HitResolver([
                new DodgeStep(),
                new BaseDamageStep(Options.Create(Config)),
                new CritStep(Options.Create(Config)),
                new ArmorStep(),
                new MinimumStep(),
            ]),
            Config,
            Characters(),
            FakeProxy.Create<IEventPublisher>(_ => null),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger.Instance,
            _systems,
            [new Modifiers(_modifiers)]
        );
        _zone.Positions[1] = (5, 5);
        await _combat.PlayerEnteredAsync(new PlayerId(7), 1, CancellationToken.None);

        return _combat.Combatants[1];
    }

    private int AddWolf()
    {
        int id = _zone.SpawnMonster(new MonsterAppearance(Wolf.Id, Wolf.Figure, 1), 6, 5);
        _combat.AddMonster(id, 1, Wolf);

        return id;
    }

    private IEnumerable<RpgCombatHit> Hits() =>
        _zone.Broadcasts.OfType<RpgCombatEventsMessageComposer>().SelectMany(e => e.Hits);

    [Fact]
    public async Task Systems_TickInOrder()
    {
        _systems.Add(new Recording(200, _log));
        _systems.Add(new Recording(100, _log));
        await PlayerNextToAWolfAsync();

        await _combat.TickAsync(CancellationToken.None);

        _log.Where(l => l.StartsWith("tick")).Should().Equal("tick 100", "tick 200");
    }

    [Fact]
    public async Task ASystem_TicksBeforeTheSwings()
    {
        _systems.Add(new Recording(100, _log) { Busy = true });
        Combatant me = await PlayerNextToAWolfAsync();
        int wolf = AddWolf();
        _combat.StartAttack(new PlayerId(7), wolf);

        await _combat.TickAsync(CancellationToken.None);

        me.Busy.Should().BeTrue();
        Hits().Should().BeEmpty("the system made him busy before the hits ran");
    }

    [Fact]
    public async Task ABusyCombatant_DoesNotSwing()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        int wolf = AddWolf();
        _combat.StartAttack(new PlayerId(7), wolf);
        me.Busy = true;

        await _combat.TickAsync(CancellationToken.None);

        Hits().Should().BeEmpty();
    }

    [Fact]
    public async Task AStun_StopsTheSwings_AndRootsUntilLifted()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        int wolf = AddWolf();
        _combat.StartAttack(new PlayerId(7), wolf);

        _modifiers.Stunned = true;
        _combat.Recompute(me);
        await _combat.TickAsync(CancellationToken.None);

        (me.Stunned, _zone.Rooted.Contains(1)).Should().Be((true, true));
        Hits().Should().BeEmpty();

        _modifiers.Stunned = false;
        _combat.Recompute(me);
        _zone.NowMs += 250;
        await _combat.TickAsync(CancellationToken.None);

        _zone.Rooted.Should().NotContain(1);
        Hits().Should().ContainSingle();
    }

    [Fact]
    public async Task Modifiers_GiveEffectiveStats_FromTheBase()
    {
        Combatant me = await PlayerNextToAWolfAsync();

        _modifiers.AddFlat(CombatStat.Power, 2);
        _modifiers.AddPercent(CombatStat.Power, 10);
        _modifiers.AddFlat(CombatStat.Armor, -50);
        _modifiers.AddFlat(CombatStat.CritPercent, 200);
        _modifiers.AddFlat(CombatStat.DamageTakenPercent, 25);
        _combat.Recompute(me);

        (me.Power, me.Armor, me.CritPercent, me.DamageTakenPercent)
            .Should()
            .Be((24, 0, 100, 25), "(20 + 2) × 1.1 = 24.2; armor never below 0; crit bounded");
        me.Base.Power.Should().Be(20, "the base is untouched");
    }

    [Fact]
    public async Task ASlow_LengthensTheSwing_AndSlowsTheWalk_ThenLeaves()
    {
        Combatant me = await PlayerNextToAWolfAsync();

        _modifiers.Slow(30);
        _modifiers.Slow(60);
        _combat.Recompute(me);

        (me.SlowPercent, me.AttackIntervalMs, _zone.MoveSpeeds[1]).Should().Be((60, 5000, 40));

        _modifiers.Clear();
        _combat.Recompute(me);

        (me.SlowPercent, me.AttackIntervalMs, _zone.MoveSpeeds[1]).Should().Be((0, 2000, 100));
    }

    [Fact]
    public async Task AMonster_TakesItsPowerFromItsDamage_AndASlowOnItsOwnInterval()
    {
        await PlayerNextToAWolfAsync();
        Combatant wolf = _combat.Combatants[AddWolf()];

        wolf.Base.Power.Should().Be(4, "(3 + 5) / 2");

        _modifiers.Slow(50);
        _combat.Recompute(wolf);

        wolf.AttackIntervalMs.Should().Be(4000, "its 2000 ms at half speed");
    }

    [Fact]
    public async Task DamageThroughTheZone_CountsLikeAHit()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        Combatant wolf = _combat.Combatants[AddWolf()];

        _combat.Damage(me, wolf, 10, _zone.NowMs);

        (wolf.Health, wolf.LastHitBy, wolf.Threat.ThreatOf(1)).Should().Be((30, 1, 10));
        wolf.Participants.Should().Contain(new PlayerId(7));
        (me.InCombat, wolf.InCombat).Should().Be((true, true));
    }

    /// <summary>A damage over time on a wolf: everyone's bar over it follows.</summary>
    [Fact]
    public async Task DamageThroughTheZone_UpdatesTheBars()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        Combatant wolf = _combat.Combatants[AddWolf()];
        await _combat.TickAsync(CancellationToken.None);
        _zone.Broadcasts.Clear();

        _combat.Damage(me, wolf, 10, _zone.NowMs);
        await _combat.TickAsync(CancellationToken.None);

        _zone
            .Broadcasts.OfType<RpgVitalsMessageComposer>()
            .SelectMany(v => v.Combatants)
            .Should()
            .Contain(v => v.ObjectId == wolf.ObjectId && v.Health == 30);
    }

    [Fact]
    public async Task DamageOnAnInvulnerableOrDeadTarget_DoesNothing()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        Combatant wolf = _combat.Combatants[AddWolf()];
        wolf.Invulnerable = true;

        _combat.Damage(me, wolf, 10, _zone.NowMs);

        (wolf.Health, wolf.LastHitBy, wolf.Threat.IsEmpty).Should().Be((40, null, true));

        wolf.Invulnerable = false;
        wolf.Health = 0;
        wolf.LastHitBy = 99;

        _combat.Damage(me, wolf, 10, _zone.NowMs);

        wolf.LastHitBy.Should().Be(99, "a dead target's killer stays its killer");
    }

    /// <summary>A system dropping a stun on death without recomputing: the zone recomputes anyway,
    /// or the respawned player never swings again.</summary>
    [Fact]
    public async Task ADeath_RecomputesTheFighter_AfterTheSystemsLetGo()
    {
        _systems.Add(new Recording(100, _log) { OnRemoving = () => _modifiers.Clear() });
        Combatant me = await PlayerNextToAWolfAsync();
        _modifiers.Stunned = true;
        _combat.Recompute(me);
        me.Health = 0;

        await _combat.TickAsync(CancellationToken.None);

        me.Stunned.Should().BeFalse();
    }

    /// <summary>What a removal queued while nobody was there is not sent to the next one in.</summary>
    [Fact]
    public async Task MessagesQueuedInAnEmptyZone_AreDropped()
    {
        IComposer stale = new RpgDeathMessageComposer { RespawnSeconds = 7 };
        _systems.Add(new Recording(100, _log) { OnRemoving = () => _combat.Broadcast(stale) });
        await PlayerNextToAWolfAsync();
        int wolf = AddWolf();
        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        _zone.Broadcasts.Clear();

        _combat.RemoveMonster(wolf);
        await _combat.PlayerEnteredAsync(new PlayerId(7), 1, CancellationToken.None);
        await _combat.TickAsync(CancellationToken.None);

        _zone.Broadcasts.Should().NotContain(stale);
    }

    [Fact]
    public async Task AMonsterGoingHome_TakesNoThreat()
    {
        await PlayerNextToAWolfAsync();
        Combatant wolf = _combat.Combatants[AddWolf()];
        wolf.Invulnerable = true;

        _combat.AddThreat(wolf, 1, 10);

        wolf.Threat.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AHeal_IsBounded_AndSaysWhatItGaveBack()
    {
        Combatant me = await PlayerNextToAWolfAsync();
        me.Health = 90;

        _combat.Heal(me, 50).Should().Be(10);
        me.Health.Should().Be(100);
    }

    [Fact]
    public void HitOptions_SetTheBase_SkipTheDodge_AndTheCrit()
    {
        HitResolver resolver = new([
            new DodgeStep(),
            new BaseDamageStep(Options.Create(Config)),
            new CritStep(Options.Create(Config)),
            new ArmorStep(),
            new MinimumStep(),
        ]);

        (int amount, int absorbed, string outcome) = resolver.Resolve(
            new TestStats { IsPlayer = true, CritPercent = 100 },
            new TestStats { DodgePercent = 100 },
            new FixedRandom(),
            new HitOptions
            {
                BaseDamage = 30,
                Unavoidable = true,
                NoCrit = true,
            }
        );

        (amount, absorbed, outcome).Should().Be((30, 0, HitContext.Hit));
    }

    [Fact]
    public async Task ASystem_SeesTheEntryWithTheProfile_AndTheExit()
    {
        _systems.Add(new Recording(100, _log));
        await PlayerNextToAWolfAsync();

        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _log.Where(l => l.StartsWith("added") || l.StartsWith("removing"))
            .Should()
            .Equal("added 1 class.mage", "removing 1 Left");
    }

    [Fact]
    public async Task CarriedStatuses_AreWrittenBackWithTheVitals()
    {
        _systems.Add(
            new Recording(100, _log)
            {
                Carried =
                [
                    new CarriedStatus
                    {
                        StatusId = "status.food",
                        RemainingMs = 60_000,
                        Stacks = 1,
                        Amount = 0,
                    },
                ],
            }
        );
        await PlayerNextToAWolfAsync();

        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _log.Should().Contain("vitals 7: 100/50 status.food");
    }

    [Fact]
    public async Task QueuedMessages_LeaveWithTheTick()
    {
        await PlayerNextToAWolfAsync();
        IComposer toAll = new RpgDeathMessageComposer { RespawnSeconds = 1 };
        IComposer toMe = new RpgDeathMessageComposer { RespawnSeconds = 2 };

        _combat.Broadcast(toAll);
        _combat.SendTo(new PlayerId(7), toMe);

        (_zone.Broadcasts.Count, _zone.Sent.Count(s => s.Composer == toMe)).Should().Be((0, 0));

        await _combat.TickAsync(CancellationToken.None);

        _zone.Broadcasts.Should().Contain(toAll);
        _zone.Sent.Should().Contain((new PlayerId(7), toMe));
    }

    [Fact]
    public async Task ASystem_AddsToTheEntryBurst()
    {
        IComposer extra = new RpgDeathMessageComposer { RespawnSeconds = 9 };
        _systems.Add(new Recording(100, _log) { EntryComposer = extra });
        await PlayerNextToAWolfAsync();

        _combat.EntryVitals(new PlayerId(7)).Should().Contain(extra);
    }

    [Fact]
    public async Task ACast_WithoutACastingSystem_IsAnUnknownSkill()
    {
        await PlayerNextToAWolfAsync();

        _combat
            .Cast(new CastRequest(1, "skill.mage.firebolt", 2, -1, -1))
            .Should()
            .Be("unknown_skill");
    }

    private IGrainFactory Characters() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name != nameof(IGrainFactory.GetGrain)
                ? null
                : FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                    inner.Method.Name switch
                    {
                        nameof(IPlayerCharacterGrain.GetCombatProfileAsync) =>
                            Task.FromResult<CharacterCombatProfile?>(
                                new CharacterCombatProfile
                                {
                                    Stats = new CharacterStatsSnapshot
                                    {
                                        MaxHealth = 100,
                                        MaxMana = 50,
                                        SpeedPercent = 100,
                                        Power = 20,
                                    },
                                    Health = 100,
                                    Mana = 50,
                                    Level = 1,
                                    Range = 1,
                                    ThreatMultiplier = 1,
                                    ClassId = "class.mage",
                                    SpecializationId = "spec.mage.fire",
                                }
                            ),
                        nameof(IPlayerCharacterGrain.SetVitalsAsync) => LogAsync(
                            $"vitals {(long)call.Args![0]!}: {inner.Args![0]}/{inner.Args[1]} {string.Join(',', ((ImmutableArray<CarriedStatus>)inner.Args[2]!).Select(s => s.StatusId))}"
                        ),
                        _ => null,
                    }
                )
        );

    private Task LogAsync(string line)
    {
        _log.Add(line);

        return Task.CompletedTask;
    }

    /// <summary>Contributes whatever the test put in the shared modifiers.</summary>
    private sealed class Modifiers(StatModifiers shared) : IStatModifierSource
    {
        public void Contribute(Combatant c, StatModifiers into) => into.Merge(shared);
    }

    private sealed class Recording(int order, List<string> log) : ICombatSystem
    {
        public bool Busy { get; init; }

        public ImmutableArray<CarriedStatus> Carried { get; init; } = [];

        public IComposer? EntryComposer { get; init; }

        public Action? OnRemoving { get; init; }

        public int Order => order;

        public void Tick(CombatZone zone, long now)
        {
            log.Add($"tick {order}");

            if (Busy)
            {
                foreach (Combatant c in zone.Players)
                {
                    c.Busy = true;
                }
            }
        }

        public void Added(CombatZone zone, Combatant c, CharacterCombatProfile? profile) =>
            log.Add($"added {c.ObjectId} {profile?.ClassId}");

        public IEnumerable<CarriedStatus> Removing(CombatZone zone, Combatant c, CombatantExit exit)
        {
            log.Add($"removing {c.ObjectId} {exit}");
            OnRemoving?.Invoke();

            return Carried;
        }

        public IEnumerable<IComposer> Entry(CombatZone zone) => EntryComposer is { } e ? [e] : [];
    }
}
