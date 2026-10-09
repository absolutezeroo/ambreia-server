using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Events;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Zones;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;

namespace Vortex.Rpg.Monsters.Tests.Support;

/// <summary>A zone on paper with its simulation, wolves to spawn, and players who fight with Power
/// 20 and 100 health. Every random draw is 0.5 unless the test sets <see cref="Zone"/>'s.</summary>
internal sealed class MonsterWorld
{
    public static readonly MonsterDefinition Wolf = new()
    {
        Id = "monster.forest.wolf",
        Kind = "monster",
        Figure = "0 3 C0A060",
        Aggressive = true,
        Base = new MonsterStatBlock
        {
            Health = 40,
            DamageMin = 3,
            DamageMax = 5,
        },
        Xp = 20,
    };

    public FakeZoneRuntime Zone { get; } = new();

    public List<string> Calls { get; } = [];

    public RpgZoneSimulation Simulation { get; private set; } = null!;

    /// <summary>The simulation for a zone holding these spawns; the wolf, and any extra
    /// definition, as its content.</summary>
    public RpgZoneSimulation Start(
        IEnumerable<ZoneSpawn> spawns,
        IMonsterBehavior[]? behaviors = null,
        params ContentDefinition[] content
    )
    {
        RpgZoneSimulationFactory factory = new(
            new TestContent([Wolf, .. content]),
            behaviors ?? [new MeleeBehavior()],
            new HitResolver([
                new DodgeStep(),
                new BaseDamageStep(Options.Create(new CombatConfig())),
                new CritStep(Options.Create(new CombatConfig())),
                new ArmorStep(),
                new MinimumStep(),
            ]),
            Options.Create(new CombatConfig()),
            Characters(),
            FakeProxy.Create<IEventPublisher>(_ => Task.CompletedTask),
            NullLoggerFactory.Instance
        );

        Simulation = (RpgZoneSimulation)
            factory.Create(Zone, ZoneSamples.Minimal() with { Spawns = [.. spawns] });

        return Simulation;
    }

    public async Task EnterAsync(int player, int objectId, int x, int y)
    {
        Zone.Positions[objectId] = (x, y);
        await Simulation.PlayerEnteredAsync(new PlayerId(player), objectId, CancellationToken.None);
    }

    public Task TickAsync() => Simulation.TickAsync(CancellationToken.None);

    public static ZoneSpawn WolvesAt(int count, params (int X, int Y)[] area) =>
        new()
        {
            Id = "wolves",
            Area = [.. System.Linq.Enumerable.Select(area, c => new ZoneCell(c.X, c.Y))],
            Monster = Wolf.Id,
            Min = count,
            Max = count,
            RespawnSeconds = 30,
            WanderRadius = 2,
            AggroRadius = 4,
        };

    private IGrainFactory Characters() =>
        FakeProxy.Create<IGrainFactory>(call =>
        {
            if (call.Method.Name != nameof(IGrainFactory.GetGrain))
            {
                return null;
            }

            int player = (int)(long)call.Args![0]!;

            return FakeProxy.Create<IPlayerCharacterGrain>(inner =>
            {
                Calls.Add($"{inner.Method.Name} {player}");

                return inner.Method.Name switch
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
                            }
                        ),
                    nameof(IPlayerCharacterGrain.GainXpAsync) => Task.FromResult(
                        XpGrantOutcome.Granted
                    ),
                    _ => null,
                };
            });
        });
}
