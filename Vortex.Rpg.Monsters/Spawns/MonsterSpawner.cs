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
/// A zone's monsters coming and going (spec M1a §5.2, Z1 §6): a spawn wakes when its conditions
/// hold and — by nearness — a player comes within reach; it then brings between its <c>Min</c> and
/// <c>Max</c>, each on a free walkable non-safe cell of its area at a level in its range, and a dead
/// one comes back after <c>RespawnSeconds</c>, one per spawn and per tick. It sleeps when its
/// conditions stop holding or nobody has been near for a while: its idle monsters go, those in a
/// fight stay. A dormant spawn brings nothing until a trigger wakes it, then one wave that never
/// comes back. A new zone activation starts from the spawns again: nothing is kept.
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

    public IReadOnlyDictionary<int, LiveMonster> Live => _live;

    /// <summary>Called on each tick a player is in the zone.</summary>
    public void Tick(long now)
    {
        foreach (SpawnPoint point in points)
        {
            if (now >= point.NextCheckAtMs)
            {
                point.NextCheckAtMs = now + config.SpawnConditionCheckMs;
                Check(point, now);
            }

            if (!point.Active)
            {
                continue;
            }

            if (point.WaveDue)
            {
                SpawnWave(point, now);

                continue;
            }

            if (point.Due.Count == 0 || point.Due.Peek() > now)
            {
                continue;
            }

            point.Due.Dequeue();

            if (Spawn(point, now) is null)
            {
                // No free cell yet: it tries again in a second.
                point.Due.Enqueue(now + 1000);
            }
        }
    }

    /// <summary>
    /// Spec Z1 §6.3–6.4: the spawn should run while its conditions hold and — by nearness — a player
    /// is near, or was within <c>AoiSleepSeconds</c>. Switched on, it fills; off, it empties.
    /// </summary>
    private void Check(SpawnPoint point, long now)
    {
        bool held =
            point.Source.Conditions.Count == 0 || zone.ConditionsHold(point.Source.Conditions);
        bool near = !point.Source.AoiActivation || AnyPlayerNear(point);

        if (near)
        {
            point.LastNearAtMs = now;
        }

        bool wanted =
            held
            && (near || point.Active && now - point.LastNearAtMs < config.AoiSleepSeconds * 1000L);

        if (wanted == point.Active)
        {
            return;
        }

        point.Active = wanted;

        if (wanted)
        {
            Fill(point, now);
        }
        else
        {
            Empty(point);
        }
    }

    /// <summary>A player within <c>AoiRadius</c> cells (Chebyshev) of a cell of the spawn's area.</summary>
    private bool AnyPlayerNear(SpawnPoint point)
    {
        int radius = config.AoiRadius;

        foreach (Combatant player in combat.Players)
        {
            if (zone.PositionOf(player.ObjectId) is not { } at)
            {
                continue;
            }

            foreach (ZoneCell cell in point.Source.Area)
            {
                if (Math.Max(Math.Abs(cell.X - at.X), Math.Abs(cell.Y - at.Y)) <= radius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Back to between Min and Max, counting the monsters it kept in a fight; a dormant
    /// spawn only waits for its wave.</summary>
    private void Fill(SpawnPoint point, long now)
    {
        point.Due.Clear();

        if (point.Dormant)
        {
            return;
        }

        int missing = Count(point) - _live.Values.Count(m => m.Spawn == point);

        for (int i = 0; i < missing; i++)
        {
            if (Spawn(point, now) is null)
            {
                point.Due.Enqueue(now + 1000);
            }
        }
    }

    /// <summary>Its monsters that are not fighting go, with their summons; none is due back.</summary>
    private void Empty(SpawnPoint point)
    {
        point.Due.Clear();

        foreach (LiveMonster m in _live.Values.Where(m => m.Spawn == point).ToArray())
        {
            if (!combat.Combatants.TryGetValue(m.ObjectId, out Combatant? c) || c.InCombat)
            {
                continue;
            }

            Dismiss(m);
            combat.RemoveMonster(m.ObjectId);
            _live.Remove(m.ObjectId);
            point.Wave.Remove(m.ObjectId);
        }
    }

    /// <summary>
    /// A SpawnGroup action (spec Z1 §6.1): every dormant spawn of that id or group whose last wave is
    /// dead brings a new one at the next tick a player is there. How many were woken.
    /// </summary>
    public int WakeGroup(string name)
    {
        int woken = 0;

        foreach (SpawnPoint point in points.Where(p => p.Dormant && p.Answers(name)))
        {
            if (point.Wave.Count > 0 || point.WaveDue)
            {
                continue;
            }

            point.WaveDue = true;
            woken++;
        }

        return woken;
    }

    private void SpawnWave(SpawnPoint point, long now)
    {
        point.WaveDue = false;
        int count = Count(point);

        for (int i = 0; i < count; i++)
        {
            if (Spawn(point, now) is not { } id)
            {
                logger.LogDebug(
                    "Spawn {SpawnId}: its wave found room for {Born} of {Count}.",
                    point.Source.Id,
                    i,
                    count
                );

                break;
            }

            point.Wave.Add(id);
        }
    }

    /// <summary>Between Min and Max, drawn on the zone's random source.</summary>
    private int Count(SpawnPoint point)
    {
        int min = Math.Max(0, point.Source.Min);
        int max = Math.Max(min, point.Source.Max);

        return zone.Random.Next(min, max + 1);
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

        // A wave's monster does not come back (spec Z1 §6.1).
        if (m.Spawn.Dormant)
        {
            m.Spawn.Wave.Remove(objectId);

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

    /// <summary>One monster of the spawn on a free cell of its area; its object id, null without room.</summary>
    private int? Spawn(SpawnPoint point, long now)
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

            return null;
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

        return id;
    }
}
