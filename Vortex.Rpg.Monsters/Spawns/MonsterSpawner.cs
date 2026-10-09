using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.World.Layers;
using Vortex.Rpg.Combat.Zones;
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

    /// <summary>A monster died: it comes back after its spawn's delay.</summary>
    public void Died(int objectId, long now)
    {
        if (_live.Remove(objectId, out LiveMonster? m))
        {
            m.Spawn.Due.Enqueue(now + Math.Max(0, m.Spawn.Source.RespawnSeconds) * 1000L);
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
        combat.AddMonster(id, level, point.Definition);
        _live[id] = new LiveMonster
        {
            ObjectId = id,
            Definition = point.Definition,
            Spawn = point,
            Home = (cell.X, cell.Y),
            NextWanderAtMs = now + 4000,
        };

        return true;
    }
}
