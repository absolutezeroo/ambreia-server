using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Combat.Configuration;
using Vortex.Rpg.Combat.Hits;
using Vortex.Rpg.Combat.Stats;
using Vortex.Rpg.Combat.Systems;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Monsters.Spawns;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Zones;

/// <summary>
/// Makes a zone's simulation (spec M1a §4.1): the combat and the monsters of its spawns, each spawn's
/// monster found in the content and its behavior by key. A spawn whose monster is unknown, retired,
/// or names no declared behavior brings nothing, and says so once.
/// </summary>
public sealed class RpgZoneSimulationFactory : IZoneSimulationFactory
{
    private readonly IRpgContent _content;
    private readonly Dictionary<string, IMonsterBehavior> _behaviors = new(StringComparer.Ordinal);
    private readonly HitResolver _hits;
    private readonly CombatConfig _config;
    private readonly IGrainFactory _grains;
    private readonly IEventPublisher _events;
    private readonly IVortexMetrics _metrics;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;

    /// <summary>What other modules add to the fights (statuses, casts, spec M1b), handed to each zone.</summary>
    private readonly ICombatSystem[] _systems;
    private readonly IStatModifierSource[] _modifiers;
    private readonly MonstersConfig _monsters;

    public RpgZoneSimulationFactory(
        IRpgContent content,
        IEnumerable<IMonsterBehavior> behaviors,
        HitResolver hits,
        IOptions<CombatConfig> config,
        IGrainFactory grains,
        IEventPublisher events,
        IVortexMetrics metrics,
        ILoggerFactory loggers,
        IEnumerable<ICombatSystem> systems,
        IEnumerable<IStatModifierSource> modifiers,
        IOptions<MonstersConfig> monsters
    )
    {
        _monsters = monsters.Value;
        _systems = [.. systems];
        _modifiers = [.. modifiers];
        _metrics = metrics;
        _content = content;
        _hits = hits;
        _config = config.Value;
        _grains = grains;
        _events = events;
        _loggers = loggers;
        _logger = loggers.CreateLogger<RpgZoneSimulationFactory>();

        foreach (IMonsterBehavior behavior in behaviors)
        {
            _behaviors[behavior.Key] = behavior;
        }

        _logger.LogInformation(
            "Monster behaviors {Behaviors}; hit steps in order {Steps}.",
            string.Join(", ", _behaviors.Keys),
            string.Join(", ", hits.Steps.Select(s => s.GetType().Name))
        );
    }

    public IZoneSimulation Create(IZoneRuntime runtime, ZoneDocument zone)
    {
        CombatZone combat = new(
            runtime,
            _hits,
            _config,
            _grains,
            _events,
            _metrics,
            _loggers.CreateLogger<CombatZone>(),
            _systems,
            _modifiers
        );
        List<SpawnPoint> points = [];

        // Spec Z1 §6.1: a spawn a trigger wakes, by its id or its group, sleeps until then.
        HashSet<string> woken =
        [
            .. zone
                .Triggers.SelectMany(t => t.Actions)
                .OfType<SpawnGroupAction>()
                .Select(a => a.Spawn),
        ];

        foreach (ZoneSpawn spawn in zone.Spawns)
        {
            MonsterDefinition? definition = _content.Get<MonsterDefinition>(spawn.Monster);

            if (definition is null || definition.Retired)
            {
                _logger.LogWarning(
                    "Spawn {SpawnId} of zone {ZoneId}: monster {MonsterId} is not in the content; nothing spawns.",
                    spawn.Id,
                    runtime.ZoneId.Value,
                    spawn.Monster
                );

                continue;
            }

            if (!_behaviors.TryGetValue(definition.Behavior, out IMonsterBehavior? behavior))
            {
                _logger.LogWarning(
                    "Spawn {SpawnId} of zone {ZoneId}: behavior {Behavior} of {MonsterId} is not declared; nothing spawns.",
                    spawn.Id,
                    runtime.ZoneId.Value,
                    definition.Behavior,
                    definition.Id
                );

                continue;
            }

            points.Add(
                new SpawnPoint
                {
                    Source = spawn,
                    Definition = definition,
                    Behavior = behavior,
                    Dormant =
                        woken.Contains(spawn.Id)
                        || spawn.Group is { } group && woken.Contains(group),
                }
            );
        }

        return new RpgZoneSimulation(
            runtime,
            combat,
            new MonsterSpawner(
                runtime,
                combat,
                points,
                _behaviors,
                _monsters,
                _loggers.CreateLogger<MonsterSpawner>()
            ),
            _content,
            _metrics,
            _monsters
        );
    }
}
