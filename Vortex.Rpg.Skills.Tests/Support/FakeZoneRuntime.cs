using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Rpg.Zones;

namespace Vortex.Rpg.Skills.Tests.Support;

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

    /// <summary>Lines of sight cut, both ways.</summary>
    public HashSet<((int X, int Y) From, (int X, int Y) To)> Blind { get; } = [];

    public HashSet<int> Walking { get; } = [];

    /// <summary>Every walk asked for: object id, goal and the range to stop within.</summary>
    public List<(int ObjectId, int X, int Y, int Within)> Walks { get; } = [];

    /// <summary>False: no path anywhere.</summary>
    public bool PathsExist { get; set; } = true;

    public Dictionary<int, string?> Postures { get; } = [];

    public List<int> Removed { get; } = [];

    public List<IComposer> Broadcasts { get; } = [];

    public List<(PlayerId Player, IComposer Composer)> Sent { get; } = [];

    public Dictionary<int, int> MoveSpeeds { get; } = [];

    public HashSet<int> Rooted { get; } = [];

    public bool IsWalkable(int x, int y) => !Walls.Contains((x, y)) && x >= 0 && y >= 0;

    public bool IsFree(int x, int y) => IsWalkable(x, y) && !Positions.ContainsValue((x, y));

    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY) =>
        !Blind.Contains(((fromX, fromY), (toX, toY)))
        && !Blind.Contains(((toX, toY), (fromX, fromY)));

    public ZoneCellRules RulesAt(int x, int y) =>
        new()
        {
            Safe = SafeCells.Contains((x, y)),
            Pvp = false,
            Hostile = false,
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

        return id;
    }

    public void RemoveMonster(int objectId)
    {
        Positions.Remove(objectId);
        Removed.Add(objectId);
    }

    public WalkResult WalkNear(int objectId, int x, int y, int within)
    {
        Walks.Add((objectId, x, y, within));

        if (!PathsExist || Rooted.Contains(objectId))
        {
            return WalkResult.NoPath;
        }

        Walking.Add(objectId);

        return WalkResult.Started;
    }

    public void StopWalking(int objectId) => Walking.Remove(objectId);

    public void Teleport(int objectId, int x, int y) => Positions[objectId] = (x, y);

    public void SetPosture(int objectId, string? posture) => Postures[objectId] = posture;

    public void Face(int objectId, int x, int y) { }

    public void SetMoveSpeed(int objectId, int percent) => MoveSpeeds[objectId] = percent;

    public void SetRooted(int objectId, bool rooted)
    {
        if (rooted)
        {
            Rooted.Add(objectId);
            Walking.Remove(objectId);
        }
        else
        {
            Rooted.Remove(objectId);
        }
    }

    public (int X, int Y)? RespawnCell() => (0, 0);

    /// <summary>The triggers the zone was told to run (spec Z1 §5).</summary>
    public List<(PlayerId Player, ZoneTriggerType Type, string Target)> Raised { get; } = [];

    public void RaiseTrigger(PlayerId player, ZoneTriggerType type, string target) =>
        Raised.Add((player, type, target));

    public bool ConditionsHold(IReadOnlyList<ZoneCondition> conditions) => true;

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
