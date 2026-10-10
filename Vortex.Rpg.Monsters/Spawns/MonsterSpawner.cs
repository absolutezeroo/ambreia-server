using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.World.Layers;
using Vortex.Rpg.Combat.Combatants;
using Vortex.Rpg.Combat.Zones;
using Vortex.Rpg.Content.Definitions;
using Vortex.Rpg.Monsters.Behaviors;
using Vortex.Rpg.Monsters.Configuration;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Monsters.Spawns;

/// <summary>
/// A zone's monsters coming and going (spec M1a §5.2): every spawn brings its <c>Max</c> the first
/// tick a player is there, each on a free walkable non-safe cell of its area at a level in its range;
/// a dead one comes back after <c>RespawnSeconds</c>, one per spawn and per tick. A new zone
/// activation starts from the spawns again: nothing is kept.
/// </summary>
public sealed class MonsterSpawner(
    IZoneRuntime zone,
    CombatZone combat,
    IReadOnlyList<SpawnPoint> points,
    IReadOnlyDictionary<string, IMonsterBehavior> behaviors,
    MonstersConfig config,
    ILogger logger
)
{
    private readonly Dictionary<int, LiveMonster> _live = [];
    private bool _awake;

    public IReadOnlyDictionary<int, LiveMonster> Live => _live;

    public void Tick(long now)
    {
        if (!_awake)
        {
            _awake = true;

            foreach (SpawnPoint point in points)
            {
                for (int i = 0; i < Math.Max(0, point.Source.Max); i++)
                {
                    if (!Spawn(point, now))
                    {
                        point.Due.Enqueue(now + 1000);
                    }
                }
            }

            return;
        }

        foreach (SpawnPoint point in points)
        {
            if (point.Due.Count == 0 || point.Due.Peek() > now)
            {
                continue;
            }

            point.Due.Dequeue();

            if (!Spawn(point, now))
            {
                // No free cell yet: it tries again in a second.
                point.Due.Enqueue(now + 1000);
            }
        }
    }

    /// <summary>The spawn a live monster belongs to — its summoner's for a summon — or null.</summary>
    public string? SpawnOf(int objectId) =>
        _live.TryGetValue(objectId, out LiveMonster? m) ? m.Spawn.Source.Id : null;

    /// <summary>A monster died: it comes back after its spawn's delay; a summon never does, and frees
    /// its place by its summoner (spec M1c §7.3).</summary>
    public void Died(int objectId, long now)
    {
        if (!_live.Remove(objectId, out LiveMonster? m))
        {
            return;
        }

        if (m.Parent is { } parent)
        {
            if (_live.TryGetValue(parent, out LiveMonster? summoner))
            {
                summoner.Summons.Remove(objectId);
            }

            return;
        }

        m.Spawn.Due.Enqueue(now + Math.Max(0, m.Spawn.Source.RespawnSeconds) * 1000L);
    }

    /// <summary>
    /// A phase brings monsters around its summoner (spec M1c §7.3): on free cells that are not safe,
    /// one then two steps away, each taking on its summoner's threat in its order. Past
    /// <see cref="MonstersConfig.MaxSummonsPerMonster"/> alive, or with no cell left, the rest stay out.
    /// </summary>
    public void Summon(
        LiveMonster summoner,
        MonsterDefinition definition,
        int count,
        int level,
        long now
    )
    {
        if (!behaviors.TryGetValue(definition.Behavior, out IMonsterBehavior? behavior))
        {
            logger.LogWarning(
                "{MonsterId} summons {SummonId}, whose behavior {Behavior} is not declared.",
                summoner.Definition.Id,
                definition.Id,
                definition.Behavior
            );

            return;
        }

        int room = Math.Min(count, config.MaxSummonsPerMonster - summoner.Summons.Count);
        (int X, int Y)[] cells = room > 0 ? Around(summoner) : [];

        for (int i = 0; i < room && i < cells.Length; i++)
        {
            (int x, int y) = cells[i];
            int id = zone.SpawnMonster(
                new MonsterAppearance(definition.Id, definition.Figure, level),
                x,
                y
            );
            Combatant summoned = combat.AddMonster(
                id,
                level,
                definition,
                config.ScaleFor(definition.Rank)
            );

            if (combat.Combatants.TryGetValue(summoner.ObjectId, out Combatant? boss))
            {
                foreach ((int target, long threat) in boss.Threat.Entries)
                {
                    summoned.Threat.Add(target, threat);
                }
            }

            _live[id] = new LiveMonster
            {
                ObjectId = id,
                Definition = definition,
                Spawn = new SpawnPoint
                {
                    Source = summoner.Spawn.Source,
                    Definition = definition,
                    Behavior = behavior,
                },
                Home = (x, y),
                NextWanderAtMs = now + 4000,
                Skills = definition.Skills,
                Parent = summoner.ObjectId,
                State = MonsterState.Chase,
            };
            summoner.Summons.Add(id);
        }

        if (Math.Min(room, cells.Length) < count)
        {
            logger.LogWarning(
                "{MonsterId} summoned {Brought} of {Count} {SummonId}: the cap or the free cells ran out.",
                summoner.Definition.Id,
                Math.Max(0, Math.Min(room, cells.Length)),
                count,
                definition.Id
            );
        }
    }

    /// <summary>A reset sends its summons away: no death, no XP (spec M1c §7.4).</summary>
    public void Dismiss(LiveMonster summoner)
    {
        foreach (int id in summoner.Summons)
        {
            combat.RemoveMonster(id);
            _live.Remove(id);
        }

        summoner.Summons.Clear();
    }

    /// <summary>Free cells that are not safe, one step away then two, in a shuffled order.</summary>
    private (int X, int Y)[] Around(LiveMonster summoner)
    {
        if (zone.PositionOf(summoner.ObjectId) is not { } at)
        {
            return [];
        }

        List<(int X, int Y)> found = [];

        for (int ring = 1; ring <= 2; ring++)
        {
            List<(int X, int Y)> cells = [];

            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    (int X, int Y) c = (at.X + dx, at.Y + dy);

                    if (
                        Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring
                        && zone.IsFree(c.X, c.Y)
                        && !zone.RulesAt(c.X, c.Y).Safe
                    )
                    {
                        cells.Add(c);
                    }
                }
            }

            Shuffle(cells);
            found.AddRange(cells);
        }

        return [.. found];
    }

    /// <summary>Fisher–Yates on the zone's random source.</summary>
    private void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = zone.Random.Next(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private bool Spawn(SpawnPoint point, long now)
    {
        ZoneCell[] cells = [.. point.Source.Area];

        // Fisher–Yates on the zone's random source: a different cell each time, the tests' fixed one.
        for (int i = cells.Length - 1; i > 0; i--)
        {
            int j = zone.Random.Next(0, i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        ZoneCell? free = cells
            .Where(c => zone.IsFree(c.X, c.Y) && !zone.RulesAt(c.X, c.Y).Safe)
            .Cast<ZoneCell?>()
            .FirstOrDefault();

        if (free is not { } cell)
        {
            logger.LogDebug("Spawn {SpawnId}: no free cell for its monster.", point.Source.Id);

            return false;
        }

        int min = Math.Max(1, Math.Min(point.Source.LevelMin, point.Source.LevelMax));
        int max = Math.Max(min, point.Source.LevelMax);
        int level = zone.Random.Next(min, max + 1);

        int id = zone.SpawnMonster(
            new MonsterAppearance(point.Definition.Id, point.Definition.Figure, level),
            cell.X,
            cell.Y
        );
        combat.AddMonster(id, level, point.Definition, config.ScaleFor(point.Definition.Rank));
        _live[id] = new LiveMonster
        {
            ObjectId = id,
            Definition = point.Definition,
            Spawn = point,
            Home = (cell.X, cell.Y),
            NextWanderAtMs = now + 4000,
            Skills = point.Definition.Skills,
        };

        return true;
    }
}
