using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Primitives.World.Rules;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Object.Avatars.Monster;
using Vortex.Rpg.Zones;
using Vortex.Rooms.Grains.State;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;

namespace Vortex.Rooms.Grains.Zones.Runtime;

/// <summary>
/// The room as a zone for combat and monsters (spec M1a §4.2): its grid, its avatars, walking,
/// sending, the clock of the combat tick. Everything runs inside the room's turn. A monster's walk
/// never crosses a safe cell and counts against the tick's path budget.
/// </summary>
public sealed class RoomZoneRuntime(RoomGrain room) : IZoneRuntime
{
    private readonly List<IComposer> _pending = [];
    private readonly List<(PlayerId Player, IComposer Composer)> _pendingToPlayers = [];

    /// <summary>Avatars the zone laid down (a ghost, a body) and those it rooted (a stun): either one
    /// keeps the avatar from walking.</summary>
    private readonly HashSet<int> _downed = [];
    private readonly HashSet<int> _rooted = [];
    private int _pathSearches;

    public RoomId ZoneId => room._state.RoomId;

    public long NowMs { get; private set; }

    public Random Random { get; set; } = Random.Shared;

    /// <summary>A combat tick starts: its instant, a fresh path budget.</summary>
    public void BeginTick(long nowMs)
    {
        NowMs = nowMs;
        _pathSearches = 0;
    }

    /// <summary>
    /// What the tick put on the room (monsters born or gone), sent in order; then the triggers it
    /// raised run, for the players still here (spec Z1 §5.1). Whatever fails, both are emptied.
    /// </summary>
    public async Task FlushAsync()
    {
        // Taken first: a send that throws below must not leave them to grow until the next tick.
        (PlayerId Player, ZoneTriggerType Type, string Target)[] raised = [.. _raised];
        _raised.Clear();

        try
        {
            foreach (IComposer composer in _pending.ToArray())
            {
                await room.SendComposerToRoomAsync(composer);
            }

            foreach ((PlayerId player, IComposer composer) in _pendingToPlayers.ToArray())
            {
                await SendToPlayerAsync(player, composer);
            }
        }
        finally
        {
            _pending.Clear();
            _pendingToPlayers.Clear();
        }

        foreach ((PlayerId player, ZoneTriggerType type, string target) in raised)
        {
            if (!room._state.AvatarsByPlayerId.ContainsKey(player))
            {
                continue;
            }

            try
            {
                await room.RaiseZoneTriggerAsync(player, type, target);
            }
            catch (Exception ex)
            {
                room._logger.LogWarning(
                    ex,
                    "Zone trigger {Type} {Target} for player {PlayerId} failed in room {RoomId}.",
                    type,
                    target,
                    player.Value,
                    room.RoomId.Value
                );
            }
        }
    }

    private readonly List<(PlayerId Player, ZoneTriggerType Type, string Target)> _raised = [];

    /// <summary>Held until the tick is over: no trigger runs in the middle of the simulation.</summary>
    public void RaiseTrigger(PlayerId player, ZoneTriggerType type, string target) =>
        _raised.Add((player, type, target));

    public bool ConditionsHold(IReadOnlyList<ZoneCondition> conditions) =>
        room.ZoneTriggers.ConditionsHold(conditions, PlayerId.Invalid);

    public bool IsWalkable(int x, int y) =>
        room.MapModule.InBounds(x, y) && room.MapModule.IsSpawnable(room.MapModule.ToIdx(x, y));

    public bool IsFree(int x, int y) =>
        IsWalkable(x, y) && room._state.TileAvatarStacks[room.MapModule.ToIdx(x, y)].Count == 0;

    /// <summary>A ray over the tiles (Bresenham): any tile on the way, the ends excluded, that no
    /// avatar could stand on — a wall, a closed tile, a blocking furniture — cuts it.</summary>
    public bool HasLineOfSight(int fromX, int fromY, int toX, int toY)
    {
        int dx = Math.Abs(toX - fromX);
        int dy = -Math.Abs(toY - fromY);
        int sx = fromX < toX ? 1 : -1;
        int sy = fromY < toY ? 1 : -1;
        int error = dx + dy;
        (int x, int y) = (fromX, fromY);

        while (true)
        {
            if ((x, y) == (toX, toY))
            {
                return true;
            }

            if ((x, y) != (fromX, fromY) && !IsWalkable(x, y))
            {
                return false;
            }

            int twice = 2 * error;

            if (twice >= dy)
            {
                error += dy;
                x += sx;
            }

            if (twice <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    public ZoneCellRules RulesAt(int x, int y) => room._state.ZoneRuntime?.RulesAt(x, y) ?? Open;

    /// <summary>A room without a zone: no rule.</summary>
    private static readonly ZoneCellRules Open = new()
    {
        Safe = false,
        Pvp = false,
        Hostile = false,
        NoMount = false,
        Restricted = false,
        RecommendedLevel = 1,
        Music = string.Empty,
        Type = default,
    };

    public (int X, int Y)? PositionOf(int objectId) =>
        room._state.AvatarsByObjectId.TryGetValue(new RoomObjectId(objectId), out IRoomAvatar? a)
            ? (a.X, a.Y)
            : null;

    public bool IsWalking(int objectId) =>
        room._state.AvatarsByObjectId.TryGetValue(new RoomObjectId(objectId), out IRoomAvatar? a)
        && a.IsWalking;

    public int SpawnMonster(MonsterAppearance appearance, int x, int y)
    {
        int id = room.AvatarModule.NextObjectId();
        RoomMonsterAvatar monster = new()
        {
            ObjectId = new RoomObjectId(id),
            DefinitionId = appearance.DefinitionId,
            Level = appearance.Level,
            PetFigure = appearance.Figure,
        };

        room._state.AvatarsByObjectId[monster.ObjectId] = monster;
        int idx = room.MapModule.ToIdx(x, y);
        room.MapModule.RollAvatar(monster, idx, room.MapModule.GetTileHeightForAvatar(idx));
        _pending.Add(new UsersMessageComposer { Avatars = [monster.GetSnapshot()] });

        return id;
    }

    public void RemoveMonster(int objectId)
    {
        RoomObjectId id = new(objectId);

        if (room._state.AvatarsByObjectId.TryGetValue(id, out IRoomAvatar? a) && a is IRoomMonster)
        {
            room.AvatarModule.CancelWalk(a);
            room.MapModule.RemoveAvatar(a, false);
            room._state.AvatarsByObjectId.Remove(id);
            _downed.Remove(objectId);
            _rooted.Remove(objectId);
            _pending.Add(new UserRemoveMessageComposer { ObjectId = id });
        }
    }

    public WalkResult WalkNear(int objectId, int x, int y, int within)
    {
        if (
            !room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? avatar
            )
            // Stunned, a ghost, a body: it walks nowhere.
            || avatar.IsMovementLocked
        )
        {
            return WalkResult.NoPath;
        }

        bool monster = avatar is IRoomMonster;
        (int X, int Y) from =
            avatar.NextTileId >= 0
                ? room.MapModule.GetTileXY(avatar.NextTileId)
                : (avatar.X, avatar.Y);

        List<(int X, int Y)> goals = Goals(avatar, monster, from, x, y, within);

        // Already in reach and in sight: nothing to walk. Mid-step, the step lands and the walk ends
        // there, rather than a search from a cell to itself.
        if (goals.Count > 0 && goals[0] == from)
        {
            if (avatar.NextTileId >= 0)
            {
                avatar.TilePath.Clear();
            }
            else
            {
                room.AvatarModule.CancelWalk(avatar);
            }

            return WalkResult.Started;
        }

        if (monster)
        {
            bool refused = _pathSearches >= room._roomConfig.MaxMonsterPathSearchesPerTick;
            room._metrics.MonsterPathSearch(refused);

            if (refused)
            {
                return WalkResult.BudgetSpent;
            }

            _pathSearches++;
        }

        foreach ((int X, int Y) goal in goals.Take(4))
        {
            IReadOnlyList<(int X, int Y)> path = monster
                ? MonsterPath(avatar, from, goal)
                : room.PathingSystem.FindPath(avatar, from, goal);

            if (path.Count > 0)
            {
                avatar.SetGoalTileId(-1);
                avatar.SetGoalTileId(room.MapModule.ToIdx(goal.X, goal.Y));
                avatar.TilePath.Clear();
                avatar.TilePath.AddRange(path.Skip(1).Select(p => room.MapModule.ToIdx(p.X, p.Y)));
                avatar.IsWalking = avatar.TilePath.Count > 0;

                return WalkResult.Started;
            }
        }

        return WalkResult.NoPath;
    }

    public void StopWalking(int objectId)
    {
        if (
            room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            ) && a.IsWalking
        )
        {
            room.AvatarModule.CancelWalk(a);
        }
    }

    public void Teleport(int objectId, int x, int y)
    {
        if (
            room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            )
        )
        {
            room.AvatarModule.Teleport(a, x, y);
        }
    }

    /// <summary>A ghost lies down and a killed monster lies dead: neither walks; standing again
    /// frees it.</summary>
    public void SetPosture(int objectId, string? posture)
    {
        if (
            !room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            )
        )
        {
            return;
        }

        a.Lay(posture == "lay");

        if (posture == "ded")
        {
            a.AddStatus(AvatarStatusType.Dead, string.Empty);
        }
        else
        {
            a.RemoveStatus(AvatarStatusType.Dead);
        }

        if (posture is "lay" or "ded")
        {
            _downed.Add(objectId);
        }
        else
        {
            _downed.Remove(objectId);
        }

        // Standing up does not free a stunned avatar.
        a.SetMovementLocked(_downed.Contains(objectId) || _rooted.Contains(objectId));
    }

    public void SetMoveSpeed(int objectId, int percent)
    {
        if (
            room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            )
        )
        {
            a.MoveSpeedPercent = Math.Clamp(percent, 1, 100);
        }
    }

    public void SetRooted(int objectId, bool rooted)
    {
        if (
            !room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            )
        )
        {
            return;
        }

        if (rooted)
        {
            _rooted.Add(objectId);
            room.AvatarModule.CancelWalk(a);
        }
        else
        {
            _rooted.Remove(objectId);
        }

        // A ghost or a body lies: freeing it from a stun does not stand it up. Lying on a bed is
        // not that: only the zone's own postures count.
        a.SetMovementLocked(rooted || _downed.Contains(objectId));
    }

    /// <summary>Through the avatar's dirty flag: the next avatar tick sends it in a UserUpdate.</summary>
    public void Face(int objectId, int x, int y)
    {
        if (
            !room._state.AvatarsByObjectId.TryGetValue(
                new RoomObjectId(objectId),
                out IRoomAvatar? a
            )
            || (a.X, a.Y) == (x, y)
        )
        {
            return;
        }

        // Each one does nothing when already facing that way: no update for nothing.
        Rotation rotation = RotationExtensions.FromPoints(a.X, a.Y, x, y);
        a.SetBodyRotation(rotation);
        a.SetHeadRotation(rotation);
    }

    public (int X, int Y)? RespawnCell()
    {
        if (room._state.ZoneRuntime?.Compiled is not { } compiled)
        {
            return null;
        }

        foreach (ZoneEntry entry in compiled.Document.Entries)
        {
            if (entry.Kind == ZoneEntryKinds.Respawn && IsWalkable(entry.Cell.X, entry.Cell.Y))
            {
                return (entry.Cell.X, entry.Cell.Y);
            }
        }

        return compiled.DefaultArrival is { } arrival ? (arrival.Cell.X, arrival.Cell.Y) : null;
    }

    public void TargetDied(int objectId)
    {
        RoomObjectId id = new(objectId);

        foreach ((PlayerId player, RoomTarget target) in room._state.Targets.ToArray())
        {
            if (target.ObjectId == id)
            {
                room._state.Targets.Remove(player);
                _pendingToPlayers.Add((player, RpgTargetMessageComposer.None));
            }
        }
    }

    public Task BroadcastAsync(IComposer composer) => room.SendComposerToRoomAsync(composer);

    public Task SendToPlayerAsync(PlayerId player, IComposer composer) =>
        room._grainFactory.GetPlayerPresenceGrain(player).SendComposerAsync(composer);

    /// <summary>Cells within <paramref name="within"/> of (x, y) the avatar may end on: walkable,
    /// free (or its own), in sight of (x, y), never safe for a monster; nearest to it first.</summary>
    private List<(int X, int Y)> Goals(
        IRoomAvatar avatar,
        bool monster,
        (int X, int Y) from,
        int x,
        int y,
        int within
    )
    {
        List<(int X, int Y)> goals = [];

        for (int gx = x - within; gx <= x + within; gx++)
        {
            for (int gy = y - within; gy <= y + within; gy++)
            {
                // Its own cell (the one it is stepping onto, mid-step) counts as free; with a range,
                // the cell aimed at (the target's) is not a place to stand.
                bool own = (gx, gy) == from;
                bool standable = own ? IsWalkable(gx, gy) : IsFree(gx, gy);
                bool inSight = within == 0 || HasLineOfSight(gx, gy, x, y);
                bool allowed = !monster || !RulesAt(gx, gy).Safe;

                if (standable && inSight && allowed)
                {
                    goals.Add((gx, gy));
                }
            }
        }

        return [.. goals.OrderBy(g => Math.Max(Math.Abs(g.X - from.X), Math.Abs(g.Y - from.Y)))];
    }

    /// <summary>A monster's path on the top surfaces, never through a safe cell.</summary>
    private IReadOnlyList<(int X, int Y)> MonsterPath(
        IRoomAvatar avatar,
        (int X, int Y) from,
        (int X, int Y) goal
    ) =>
        // The flat search asks canOccupy of the two ends only: every step in between goes through
        // canMove, so the safe cells are refused there too. It keeps no height per node: each step
        // is measured from the top of the cell it leaves, as the walk itself will.
        room.PathingSystem.FindPath(
            from,
            goal,
            idx => room.MapModule.CanAvatarWalk(avatar, idx) && !IsSafe(idx),
            (current, next, isGoal) =>
                !IsSafe(next)
                && room.MapModule.CanAvatarWalkBetween(
                    avatar,
                    current,
                    next,
                    room.MapModule.GetTopSection(current).Height,
                    out _,
                    isGoal
                )
        );

    private bool IsSafe(int idx) =>
        RulesAt(room.MapModule.GetX(idx), room.MapModule.GetY(idx)).Safe;
}
