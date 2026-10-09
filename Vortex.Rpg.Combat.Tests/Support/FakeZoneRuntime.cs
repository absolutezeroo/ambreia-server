using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Combat.Tests.Support;

/// <summary>A zone on paper: avatars at cells, every cell walkable and in sight unless the test says
/// otherwise, a clock the test moves, and everything sent kept for the asserts.</summary>
internal sealed class FakeZoneRuntime : IZoneRuntime
{
    private int _nextObjectId = 100;

    public RoomId ZoneId => new(5);

    public long NowMs { get; set; } = 10_000;

    public Random Random { get; set; } = new FixedRandom();

    public Dictionary<int, (int X, int Y)> Positions { get; } = [];

    public HashSet<(int X, int Y)> SafeCells { get; } = [];

    public HashSet<(int X, int Y)> Walls { get; } = [];

    public HashSet<int> Walking { get; } = [];

    /// <summary>Every walk asked for: object id and goal.</summary>
    public List<(int ObjectId, int X, int Y)> Walks { get; } = [];

    /// <summary>False: no path anywhere.</summary>
    public bool PathsExist { get; set; } = true;

    public Dictionary<int, string?> Postures { get; } = [];

    public List<int> Removed { get; } = [];

    public List<(int ObjectId, int X, int Y)> Teleports { get; } = [];

    public (int X, int Y)? Respawn { get; set; } = (0, 0);

    public List<IComposer> Broadcasts { get; } = [];

    public List<(PlayerId Player, IComposer Composer)> Sent { get; } = [];

    public List<(MonsterAppearance Appearance, int ObjectId)> Spawned { get; } = [];

    public bool IsWalkable(int x, int y) => !Walls.Contains((x, y)) && x >= 0 && y >= 0;

    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY) => true;

    public HashSet<(int X, int Y)> HostileCells { get; } = [];

    public ZoneCellRules RulesAt(int x, int y) =>
        new()
        {
            Safe = SafeCells.Contains((x, y)),
            Pvp = false,
            Hostile = HostileCells.Contains((x, y)),
            NoMount = false,
            Restricted = false,
            RecommendedLevel = 1,
            Music = string.Empty,
            Type = default,
        };

    public (int X, int Y)? PositionOf(int objectId) =>
        Positions.TryGetValue(objectId, out (int X, int Y) at) ? at : null;

    public bool IsWalking(int objectId) => Walking.Contains(objectId);

    public int SpawnMonster(MonsterAppearance appearance, int x, int y)
    {
        int id = ++_nextObjectId;
        Positions[id] = (x, y);
        Spawned.Add((appearance, id));

        return id;
    }

    public void RemoveMonster(int objectId)
    {
        Positions.Remove(objectId);
        Removed.Add(objectId);
    }

    /// <summary>The range each walk asked to stop within, in the order of <see cref="Walks"/>.</summary>
    public List<int> WalkRanges { get; } = [];

    /// <summary>Set: every monster walk answers it (the zone's path budget spent).</summary>
    public WalkResult? Budget { get; set; }

    public bool IsFree(int x, int y) => IsWalkable(x, y) && !Positions.ContainsValue((x, y));

    public WalkResult WalkNear(int objectId, int x, int y, int within)
    {
        Walks.Add((objectId, x, y));
        WalkRanges.Add(within);

        if (Budget is { } spent)
        {
            return spent;
        }

        if (!PathsExist)
        {
            return WalkResult.NoPath;
        }

        Walking.Add(objectId);

        return WalkResult.Started;
    }

    public void StopWalking(int objectId) => Walking.Remove(objectId);

    public void Teleport(int objectId, int x, int y)
    {
        Positions[objectId] = (x, y);
        Teleports.Add((objectId, x, y));
    }

    public void SetPosture(int objectId, string? posture) => Postures[objectId] = posture;

    /// <summary>Every turn asked for: object id and the cell faced.</summary>
    public List<(int ObjectId, int X, int Y)> Faces { get; } = [];

    public void Face(int objectId, int x, int y) => Faces.Add((objectId, x, y));

    public (int X, int Y)? RespawnCell() => Respawn;

    public Task BroadcastAsync(IComposer composer)
    {
        Broadcasts.Add(composer);

        return Task.CompletedTask;
    }

    public Task SendToPlayerAsync(PlayerId player, IComposer composer)
    {
        Sent.Add((player, composer));

        return Task.CompletedTask;
    }
}
