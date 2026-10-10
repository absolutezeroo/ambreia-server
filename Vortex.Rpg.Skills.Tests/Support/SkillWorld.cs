using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Outgoing.Rpg.Skills;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Skills.Casting;
using Vortex.Rpg.Skills.Configuration;
using Vortex.Rpg.Skills.Effects;
using Vortex.Rpg.Skills.Hits;
using Vortex.Rpg.Skills.Statuses;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support;

namespace Vortex.Rpg.Skills.Tests.Support;

/// <summary>
/// A zone on paper with its fights and the skills module's statuses: players with Power 25 and 100
/// health (player 7 is avatar 1 at (5, 5)), wolves of 40 health and 0 armor. Every random draw is
/// 0.5 unless the test sets <see cref="Zone"/>'s: no dodge, no crit.
/// </summary>
internal sealed class SkillWorld
{
    public static readonly MonsterDefinition WolfDefinition = new()
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
        CorpseSeconds = 0,
    };

    private readonly Dictionary<int, CharacterCombatProfile> _profiles = [];

    public SkillWorld(IEnumerable<ICombatSystem>? extra = null, params ContentDefinition[] content)
        : this(extra, content, casting: false) { }

    private SkillWorld(IEnumerable<ICombatSystem>? extra, ContentDefinition[] content, bool casting)
    {
        Content = new TestContent(content);
        Statuses = new StatusApplier(Options.Create(Config), Metrics);
        Effects = new SkillEffects(
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
        Hits = new HitResolver([
            new DodgeStep(),
            new BaseDamageStep(Options.Create(CombatConfig)),
            new CritStep(Options.Create(CombatConfig)),
            new DamageModifierStep(),
            new ArmorStep(),
            new MinimumStep(),
            new ShieldStep(),
        ]);
        StatusSystem = new StatusSystem(Hits, Options.Create(Config), Content, Statuses);
        Casting = casting
            ? new CastSystem(Effects, Hits, Statuses, Options.Create(Config), Content, Metrics)
            : null;
        Combat = new CombatZone(
            Zone,
            Hits,
            CombatConfig,
            Characters(),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            Metrics,
            NullLogger.Instance,
            [
                StatusSystem,
                .. Casting is null ? [] : new ICombatSystem[] { Casting },
                .. extra ?? [],
            ],
            [StatusSystem]
        );
    }

    /// <summary>A world that casts: the skills module's <see cref="CastSystem"/> too.</summary>
    public static SkillWorld WithCasting(params ContentDefinition[] content) =>
        new(null, content, casting: true);

    public SkillEffects Effects { get; }

    public CastSystem? Casting { get; }

    /// <summary>What was counted: "cast skill.x resolved", "status status.y".</summary>
    public List<string> Counted { get; } = [];

    private IVortexMetrics Metrics =>
        FakeProxy.Create<IVortexMetrics>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IVortexMetrics.SkillCast):
                    Counted.Add($"cast {call.Args![0]} {call.Args[1]}");
                    break;
                case nameof(IVortexMetrics.StatusApplied):
                    Counted.Add($"status {call.Args![0]}");
                    break;
            }

            return null;
        });

    /// <summary>The player's cast, as the room hands it over.</summary>
    public string? Cast(string skillId, int target = 0, int x = -1, int y = -1, int player = 7) =>
        Combat.PlayerCombatant(new PlayerId(player)) is { } me
            ? Combat.Cast(new CastRequest(me.ObjectId, skillId, target, x, y))
            : RpgSkillResultMessageComposer.Dead;

    public FakeZoneRuntime Zone { get; } = new();

    public SkillsConfig Config { get; } = new();

    public CombatConfig CombatConfig { get; } = new();

    public TestContent Content { get; }

    public StatusApplier Statuses { get; }

    public StatusSystem StatusSystem { get; }

    public HitResolver Hits { get; }

    public CombatZone Combat { get; }

    /// <summary>What the characters were told: "vitals 7: 80/50 status.a,status.b".</summary>
    public List<string> Calls { get; } = [];

    public long Now => Zone.NowMs;

    public async Task<Combatant> PlayerAsync(
        int player = 7,
        int objectId = 1,
        int x = 5,
        int y = 5,
        string classId = "class.mage",
        int power = 25,
        double critPercent = 0,
        ImmutableArray<CarriedStatus> carried = default
    )
    {
        Zone.Positions[objectId] = (x, y);
        _profiles[player] = new CharacterCombatProfile
        {
            Stats = new CharacterStatsSnapshot
            {
                MaxHealth = 100,
                MaxMana = 100,
                SpeedPercent = 100,
                Power = power,
                CritPercent = critPercent,
            },
            Health = 100,
            Mana = 100,
            Level = 6,
            Range = 1,
            ThreatMultiplier = 1,
            ClassId = classId,
            SpecializationId = "spec.any",
            Statuses = carried.IsDefault ? [] : carried,
        };
        await Combat.PlayerEnteredAsync(new PlayerId(player), objectId, CancellationToken.None);

        return Combat.Combatants[objectId];
    }

    public Combatant Wolf(int x = 6, int y = 5, MonsterDefinition? definition = null)
    {
        MonsterDefinition d = definition ?? WolfDefinition;
        int id = Zone.SpawnMonster(new MonsterAppearance(d.Id, d.Figure, 1), x, y);

        return Combat.AddMonster(id, 1, d);
    }

    /// <summary>Moves the clock by <paramref name="ms"/> and runs one combat tick.</summary>
    public async Task TickAsync(long ms = 250)
    {
        Zone.NowMs += ms;
        await Combat.TickAsync(CancellationToken.None);
    }

    /// <summary>Ticks of 250 ms until <paramref name="ms"/> have passed.</summary>
    public async Task RunAsync(long ms)
    {
        for (long spent = 0; spent < ms; spent += 250)
        {
            await TickAsync();
        }
    }

    public IEnumerable<T> Broadcast<T>() => Zone.Broadcasts.OfType<T>();

    public IEnumerable<T> SentTo<T>(int player) =>
        Zone.Sent.Where(s => s.Player == new PlayerId(player)).Select(s => s.Composer).OfType<T>();

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
                        $"vitals {player}: {inner.Args![0]}/{inner.Args[1]} {string.Join(',', ((ImmutableArray<CarriedStatus>)inner.Args[2]!).Select(s => $"{s.StatusId}@{s.RemainingMs}"))}",
                        Task.CompletedTask
                    ),
                    nameof(IPlayerCharacterGrain.GainXpAsync) => Record(
                        $"xp {player}: {inner.Args![0]}",
                        Task.FromResult(XpGrantOutcome.Granted)
                    ),
                    _ => null,
                }
            );
        });

    private object Record(string call, object answer)
    {
        Calls.Add(call);

        return answer;
    }
}
