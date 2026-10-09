using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Events;
using Vortex.Primitives.Events.Combat;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Tests.Support;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rpg.Combat.Tests;

/// <summary>
/// Spec M1a §6–§8: the zone's combat on a zone on paper. Every random draw is 0.5: no dodge, no crit,
/// a player hits for his Power (20), a wolf for 4. A player is avatar 1, 2 or 3 (players 7, 8, 9).
/// </summary>
public sealed class CombatZoneTests
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
    private readonly List<string> _calls = [];
    private readonly List<IEvent> _events = [];
    private readonly Dictionary<int, CharacterCombatProfile> _profiles = [];

    private CombatZone _combat = null!;

    [Fact]
    public async Task Entering_ReadsTheProfile_AndSendsSelfVitals()
    {
        await OnePlayerAsync();

        (PlayerId to, IComposer composer) = _zone.Sent.Should().ContainSingle().Which;

        to.Should().Be(new PlayerId(7));
        composer
            .Should()
            .Be(
                new RpgSelfVitalsMessageComposer
                {
                    Health = 100,
                    MaxHealth = 100,
                    Mana = 50,
                    MaxMana = 50,
                    InCombat = false,
                }
            );
    }

    [Fact]
    public async Task APlayerInRange_HitsAtHisCadence()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);

        _combat.StartAttack(new PlayerId(7), wolf).Should().BeNull();
        await _combat.TickAsync(CancellationToken.None);
        await _combat.TickAsync(CancellationToken.None);

        Hits()
            .Should()
            .ContainSingle()
            .Which.Health.Should()
            .Be(20, "40 − 20, once in the cadence");

        _zone.NowMs += 2000;
        await _combat.TickAsync(CancellationToken.None);

        Hits().Last().Killed.Should().BeTrue();
    }

    [Fact]
    public async Task APlayerOutOfRange_WalksToTheTarget()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(9, 5);

        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);

        _zone.Walks.Should().Equal((1, 9, 5));
        Hits().Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreachableTarget_StopsTheAttack_WithUnreachable()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(9, 5);
        _zone.PathsExist = false;

        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);
        await _combat.TickAsync(CancellationToken.None);

        Results().Should().Equal("unreachable");
        _zone.Walks.Should().HaveCount(1, "the attack stopped");
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public async Task AnAttackFromOrOntoASafeCell_IsRefused(int safeX, int safeY)
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _zone.SafeCells.Add((safeX, safeY));

        _combat.StartAttack(new PlayerId(7), wolf).Should().Be("safe_zone");
    }

    [Fact]
    public async Task AHitOnAMonster_AddsThreatTimesTheMultiplier()
    {
        await OnePlayerAsync(threatMultiplier: 3);
        int wolf = AddWolf(6, 5);

        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);

        _combat.Combatants[wolf].Threat.ThreatOf(1).Should().Be(60);
        _combat.Combatants[wolf].Threat.Top.Should().Be(1);
    }

    [Fact]
    public async Task AMonsterKilled_GivesXpOnceToEachParticipantStillHere()
    {
        _combat = NewCombat();
        await EnterAsync(9, 3, (5, 4));
        await EnterAsync(7, 1, (5, 5));
        await EnterAsync(8, 2, (5, 6));
        int wolf = AddWolf(6, 5);
        _combat.Combatants[wolf].Health = 100;

        foreach (int player in new[] { 7, 8, 9 })
        {
            _combat.StartAttack(new PlayerId(player), wolf);
        }

        await _combat.TickAsync(CancellationToken.None);
        await _combat.PlayerLeftAsync(new PlayerId(9), CancellationToken.None);
        _zone.NowMs += 2000;
        await _combat.TickAsync(CancellationToken.None);

        _calls.Where(c => c.StartsWith("xp")).Should().BeEquivalentTo("xp 7: 20", "xp 8: 20");
        _combat.DiedMonsters.Should().Equal(wolf);
        _zone.Removed.Should().Equal(wolf);
        _events.OfType<MonsterKilledEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task APlayerKilled_BecomesAGhost_ThenRespawnsAtHalfHealth_AndIsWorn()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 4;
        _combat.SetMonsterTarget(wolf, 1);

        await _combat.TickAsync(CancellationToken.None);

        _zone.Postures[1].Should().Be("lay");
        Sent<RpgDeathMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.RespawnSeconds.Should()
            .Be(5);
        _calls.Should().Contain("wear 7: 10");
        _combat.Combatants[wolf].TargetId.Should().BeNull("a ghost is no target");
        _events.OfType<PlayerDiedEvent>().Should().ContainSingle();

        _zone.NowMs += 5000;
        await _combat.TickAsync(CancellationToken.None);

        _zone.Teleports.Should().Equal((1, 0, 0));
        _zone.Postures[1].Should().BeNull();
        _combat.Combatants[1].Health.Should().Be(50);
    }

    [Fact]
    public async Task ARespawn_EndsTheCombat_WithCombatLeft()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 4;
        _combat.SetMonsterTarget(wolf, 1);
        await _combat.TickAsync(CancellationToken.None);

        _zone.NowMs += 5000;
        await _combat.TickAsync(CancellationToken.None);

        _combat.Combatants[1].InCombat.Should().BeFalse();
        _events.OfType<CombatLeftEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task LeavingInCombat_EndsTheCombat_WithCombatLeft()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);

        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _events.OfType<CombatLeftEvent>().Should().ContainSingle();
    }

    /// <summary>Spec §8.2: XP goes to who touched it — a swing that missed gives threat only.</summary>
    [Fact]
    public async Task AMiss_MakesNoParticipant()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[wolf].DodgePercent = 100;
        _combat.StartAttack(new PlayerId(7), wolf);

        await _combat.TickAsync(CancellationToken.None);

        _combat.Combatants[wolf].Participants.Should().BeEmpty();
        _combat.Combatants[wolf].Threat.Top.Should().Be(1);
    }

    [Fact]
    public async Task NoHitForFiveSeconds_LeavesCombat_AndRegenerates()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 50;
        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);
        _combat.Combatants[1].InCombat.Should().BeTrue();

        _combat.RemoveMonster(wolf);
        _zone.NowMs += 5000;
        await _combat.TickAsync(CancellationToken.None);

        _combat.Combatants[1].InCombat.Should().BeFalse();
        Sent<RpgSelfVitalsMessageComposer>().Last().InCombat.Should().BeFalse();
        _events.OfType<CombatLeftEvent>().Should().ContainSingle();

        int before = _combat.Combatants[1].Health;
        _zone.NowMs += 1000;
        await _combat.TickAsync(CancellationToken.None);

        _combat.Combatants[1].Health.Should().Be(before + 2, "2 % of 100 per second");
    }

    /// <summary>Spec M1a §4.4: an item taken off in the zone lowers the maximum; health follows.</summary>
    [Fact]
    public async Task AStatsChange_BoundsHealthToTheNewMaximum()
    {
        await OnePlayerAsync();

        _combat.PlayerStatsChanged(
            new PlayerId(7),
            Profile(
                new CharacterStatsSnapshot
                {
                    MaxHealth = 60,
                    MaxMana = 50,
                    SpeedPercent = 100,
                    Power = 25,
                },
                health: 5
            )
        );
        await _combat.TickAsync(CancellationToken.None);

        (_combat.Combatants[1].Health, _combat.Combatants[1].MaxHealth, _combat.Combatants[1].Power)
            .Should()
            .Be((60, 60, 25), "the zone's health is the truth: the profile's 5 is not read");
        Sent<RpgSelfVitalsMessageComposer>().Last().MaxHealth.Should().Be(60);
    }

    /// <summary>A player back within the grace period keeps his combatant and gets no second
    /// entry: the burst carries his own vitals, or his HUD stays empty (spec §9.2).</summary>
    [Fact]
    public async Task TheEntryBurst_CarriesTheEnteringPlayersOwnVitals()
    {
        await OnePlayerAsync();
        _combat.Combatants[1].Health = 70;

        IComposer[] own = [.. _combat.EntryVitals(new PlayerId(7))];
        IComposer[] stranger = [.. _combat.EntryVitals(new PlayerId(8))];

        own.OfType<RpgSelfVitalsMessageComposer>().Single().Health.Should().Be(70);
        own.OfType<RpgVitalsMessageComposer>().Should().ContainSingle();
        stranger.OfType<RpgSelfVitalsMessageComposer>().Should().BeEmpty();
        stranger.OfType<RpgVitalsMessageComposer>().Should().ContainSingle();
    }

    /// <summary>A level gained in the zone counts at once for XP (spec §8.2), and a class chosen
    /// there brings its range and threat.</summary>
    [Fact]
    public async Task ALevelOrClassGainedInZone_ScoresTheNextKill()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);

        _combat.PlayerStatsChanged(
            new PlayerId(7),
            Profile(_profiles[7].Stats, level: 20, range: 3, threatMultiplier: 4)
        );
        _combat.Combatants[wolf].Health = 1;
        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);

        (_combat.Combatants[1].Range, _combat.Combatants[1].ThreatMultiplier).Should().Be((3, 4));
        _combat.DiedMonsters.Should().Equal(wolf);
        _calls
            .Should()
            .NotContain(c => c.StartsWith("xp"), "a level 1 wolf gives a level 20 nothing");
    }

    private static CharacterCombatProfile Profile(
        CharacterStatsSnapshot stats,
        int health = 100,
        int level = 1,
        int range = 1,
        int threatMultiplier = 1
    ) =>
        new()
        {
            Stats = stats,
            Health = health,
            Mana = 50,
            Level = level,
            Range = range,
            ThreatMultiplier = threatMultiplier,
            ClassId = "class.mage",
            SpecializationId = "spec.mage.fire",
        };

    [Fact]
    public async Task InCombat_NothingRegenerates()
    {
        await OnePlayerAsync();
        Combatant player = _combat.Combatants[1];
        player.Health = 50;
        player.InCombat = true;
        player.LastHostileAtMs = _zone.NowMs;

        _zone.NowMs += 1000;
        await _combat.TickAsync(CancellationToken.None);

        player.Health.Should().Be(50);
    }

    [Fact]
    public async Task APlayerWhoLeaves_IsWrittenBackOnce_AndForgotten()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 30;
        _combat.Combatants[1].Mana = 10;
        _combat.StartAttack(new PlayerId(7), wolf);
        await _combat.TickAsync(CancellationToken.None);
        _combat.SetMonsterTarget(wolf, 1);

        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);
        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _calls.Where(c => c.StartsWith("vitals")).Should().Equal("vitals 7: 30/10");
        _combat.Combatants.Should().NotContainKey(1);
        _combat.Combatants[wolf].Threat.Contains(1).Should().BeFalse();
        _combat.Combatants[wolf].TargetId.Should().BeNull();
    }

    [Fact]
    public async Task AGhostWhoLeaves_IsWrittenBackAtItsRespawnHealth()
    {
        await OnePlayerAsync();
        int wolf = AddWolf(6, 5);
        _combat.Combatants[1].Health = 4;
        _combat.SetMonsterTarget(wolf, 1);
        await _combat.TickAsync(CancellationToken.None);

        await _combat.PlayerLeftAsync(new PlayerId(7), CancellationToken.None);

        _calls.Where(c => c.StartsWith("vitals")).Should().Equal("vitals 7: 50/50");
    }

    [Fact]
    public async Task APlayerEnteringAtZeroHealth_IsBackAtHisRespawnHealth()
    {
        _combat = NewCombat();

        await EnterAsync(7, 1, (5, 5), health: 0);

        _combat.Combatants[1].Health.Should().Be(50);
    }

    [Fact]
    public async Task OneTick_SendsOneCombatEventsMessage()
    {
        _combat = NewCombat();
        await EnterAsync(7, 1, (5, 5));
        await EnterAsync(8, 2, (5, 6));
        int wolf = AddWolf(6, 5);
        _combat.Combatants[wolf].Health = 100;
        _combat.StartAttack(new PlayerId(7), wolf);
        _combat.StartAttack(new PlayerId(8), wolf);

        await _combat.TickAsync(CancellationToken.None);

        _zone
            .Broadcasts.OfType<RpgCombatEventsMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.Hits.Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task NoPlayer_TheTickDoesNothing()
    {
        _combat = NewCombat();
        AddWolf(6, 5);

        await _combat.TickAsync(CancellationToken.None);

        _zone.Broadcasts.Should().BeEmpty();
    }

    private IEnumerable<RpgCombatHit> Hits() =>
        _zone.Broadcasts.OfType<RpgCombatEventsMessageComposer>().SelectMany(e => e.Hits);

    private IEnumerable<string> Results() =>
        Sent<RpgCombatResultMessageComposer>().Select(r => r.Code);

    private IEnumerable<T> Sent<T>() => _zone.Sent.Select(s => s.Composer).OfType<T>();

    private async Task OnePlayerAsync(int threatMultiplier = 1)
    {
        _combat = NewCombat();
        await EnterAsync(7, 1, (5, 5), threatMultiplier: threatMultiplier);
    }

    private async Task EnterAsync(
        int player,
        int objectId,
        (int X, int Y) at,
        int threatMultiplier = 1,
        int health = 100
    )
    {
        _zone.Positions[objectId] = at;
        _profiles[player] = new CharacterCombatProfile
        {
            Stats = new CharacterStatsSnapshot
            {
                MaxHealth = 100,
                MaxMana = 50,
                SpeedPercent = 100,
                Power = 20,
            },
            Health = health,
            Mana = 50,
            Level = 1,
            Range = 1,
            ThreatMultiplier = threatMultiplier,
            ClassId = "class.mage",
            SpecializationId = "spec.mage.fire",
        };

        await _combat.PlayerEnteredAsync(new PlayerId(player), objectId, CancellationToken.None);
    }

    private int AddWolf(int x, int y)
    {
        int id = _zone.SpawnMonster(new MonsterAppearance(Wolf.Id, Wolf.Figure, 1), x, y);
        _combat.AddMonster(id, 1, Wolf);

        return id;
    }

    private CombatZone NewCombat() =>
        new(
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
            FakeProxy.Create<IEventPublisher>(call =>
            {
                _events.Add((IEvent)call.Args![0]!);

                return Task.CompletedTask;
            }),
            FakeProxy.Create<IVortexMetrics>(_ => null),
            NullLogger.Instance
        );

    private IGrainFactory Characters() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            int player = (int)(long)call.Args![0]!;

            return FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                inner.Method.Name switch
                {
                    nameof(IPlayerCharacterGrain.GetCombatProfileAsync) => Task.FromResult(
                        _profiles.GetValueOrDefault(player)
                    ),
                    nameof(IPlayerCharacterGrain.SetVitalsAsync) => Record(
                        $"vitals {player}: {inner.Args![0]}/{inner.Args[1]}",
                        Task.CompletedTask
                    ),
                    nameof(IPlayerCharacterGrain.GainXpAsync) => Record(
                        $"xp {player}: {inner.Args![0]}",
                        Task.FromResult(XpGrantOutcome.Granted)
                    ),
                    nameof(IPlayerCharacterGrain.WearEquipmentAsync) => Record(
                        $"wear {player}: {inner.Args![0]}",
                        Task.CompletedTask
                    ),
                    _ => null,
                }
            );
        });

    private object Record(string call, object answer)
    {
        _calls.Add(call);

        return answer;
    }
}
