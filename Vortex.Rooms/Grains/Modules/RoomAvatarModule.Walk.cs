using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>Moving avatars: a walk asked for, each step taken, a walk stopped or cut short, and a
/// teleport.</summary>
public sealed partial class RoomAvatarModule
{
    /// <summary>
    /// What to run when an avatar's current walk ends, by room object id. One entry per avatar: a
    /// new walk replaces the old promise, which is what "walk somewhere else" means.
    /// <para>
    /// An avatar that leaves the room mid-walk leaves its entry behind; object ids are never reused
    /// within a room, so it can never fire on somebody else, and the grain takes it with it.
    /// </para>
    /// </summary>
    private readonly Dictionary<int, Func<CancellationToken, Task>> _onWalkEnded = [];

    /// <summary>
    /// Whether this tile has somewhere to stand other than <paramref name="standingAt" />.
    ///
    /// The question only exists because a click carries no height: it is how "the tile I am on" is
    /// told apart from "the other level of the tile I am on". Asked with an unbounded step, because
    /// what is being decided is whether a second surface exists at all -- whether it can be reached
    /// is the search's business, and it may well need the stairs to get there.
    /// </summary>
    private bool HasAnotherSurface(int tileId, Altitude standingAt)
    {
        RoomTileSection? highest = _roomGrain.MapModule.FindSection(
            tileId,
            standingAt,
            Altitude.FromValue(double.MaxValue)
        );

        return highest is not null && highest.Value.Height != standingAt;
    }

    public async Task<bool> WalkAvatarToAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    )
    {
        if (
            ctx.PlayerId <= 0
            || !_roomGrain._state.AvatarsByPlayerId.TryGetValue(
                ctx.PlayerId,
                out RoomObjectId objectIdValue
            )
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(
                objectIdValue,
                out IRoomAvatar? avatar
            )
            || !await WalkAvatarToAsync(avatar, targetX, targetY, ct, targetZKey)
        )
        {
            return false;
        }

        // A player walking on his own gives up his attack (spec M1a §6.1) and a cast that does not
        // allow moving (spec M1b §6.4).
        _roomGrain.Simulation.StopAttack(ctx.PlayerId);
        _roomGrain.Simulation.PlayerWalked(ctx.PlayerId);

        return true;
    }

    public async Task<bool> WalkAvatarToAsync(
        RoomObjectId objectId,
        int targetX,
        int targetY,
        CancellationToken ct
    )
    {
        if (
            !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
            || !await WalkAvatarToAsync(avatar, targetX, targetY, ct)
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Walks an avatar to a tile and runs <paramref name="onEnded" /> once that walk ends — on
    /// arrival, or wherever the avatar was left if the path was cut short.
    /// <para>
    /// Both cases on purpose, as the reference emulator does it: the caller asked for something to
    /// happen <em>after</em> the approach, and an action dropped in silence because a chair
    /// appeared mid-path is worse than one taken a tile too far away.
    /// </para>
    /// </summary>
    public async Task<bool> WalkAvatarToThenAsync(
        IRoomAvatar avatar,
        int targetX,
        int targetY,
        Func<CancellationToken, Task> onEnded,
        CancellationToken ct
    )
    {
        if (!await WalkAvatarToAsync(avatar, targetX, targetY, ct))
        {
            return false;
        }

        _onWalkEnded[avatar.ObjectId.Value] = onEnded;

        return true;
    }

    public async Task<bool> WalkAvatarToAsync(
        IRoomAvatar avatar,
        int targetX,
        int targetY,
        CancellationToken ct,
        int? targetZKey = null
    )
    {
        // A movement-locked avatar starts no walk. Nothing sets the lock yet; whoever does is
        // expected to stop the in-flight walk itself.
        if (avatar.IsMovementLocked)
        {
            return false;
        }

        try
        {
            int goalTileId = _roomGrain.MapModule.ToIdx(targetX, targetY);
            int currentTileId =
                avatar.NextTileId >= 0
                    ? avatar.NextTileId
                    : _roomGrain.MapModule.ToIdx(avatar.X, avatar.Y);
            (int currentX, int currentY) = _roomGrain.MapModule.GetTileXY(currentTileId);

            // Clicking the tile you are already standing on is normally nothing to do -- and used
            // to be exactly that. It stopped being so the moment a tile could hold two surfaces:
            // the floor under a platform and the top of that platform are the *same* (x, y), and
            // the wire carries no height, so "walk up onto the thing above me" and "walk down under
            // the thing below me" both arrive here as the tile the avatar is already on.
            //
            // So a same-tile target is refused only when the tile really has nowhere else to stand.
            // When it has, the walk goes ahead: no step joins two surfaces of one tile directly, so
            // the search leaves by a neighbour and comes back at the other height -- round by the
            // stairs, which is what a player means and what they would do on foot.
            bool sameTile = goalTileId == currentTileId;

            if (sameTile)
            {
                // With a height on the wire the guess above is no longer needed: a same-tile click
                // is a no-op exactly when the surface asked for is the one already stood on, and
                // anything else is a real request to change height on this tile. Without a height
                // it falls back to "is there anywhere else to stand here at all".
                bool nothingToDo = targetZKey is not null
                    ? targetZKey == (int)Math.Round(avatar.Z * 100)
                    : !HasAnotherSurface(currentTileId, avatar.Z);

                if (nothingToDo)
                {
                    return false;
                }
            }

            if (!avatar.SetGoalTileId(goalTileId))
            {
                _roomGrain._logger.LogWarning(
                    "Walk refused before pathfinding for avatar {ObjectId} in room {RoomId}: "
                        + "({CurrentX},{CurrentY}) -> ({TargetX},{TargetY})",
                    avatar.ObjectId,
                    _roomGrain.RoomId,
                    currentX,
                    currentY,
                    targetX,
                    targetY
                );

                return false;
            }

            IReadOnlyList<(int X, int Y)> path = _roomGrain.PathingSystem.FindPath(
                avatar,
                (currentX, currentY),
                (targetX, targetY),
                targetZKey
            );

            if (path.Count == 0)
            {
                // A refused walk used to be indistinguishable from a walk nobody asked for: the
                // client sends a tile, the server returns false, and the avatar simply does not
                // move. With a tile now having several surfaces there are two quite different
                // reasons for that -- the goal is not somewhere this avatar may stand at any of its
                // heights, or it is but no route reaches it -- and the two want opposite fixes.
                _roomGrain._logger.LogWarning(
                    "No path for avatar {ObjectId} in room {RoomId}: ({CurrentX},{CurrentY}) z={FromZ} "
                        + "-> ({TargetX},{TargetY}). goalOccupiable={GoalOccupiable}, "
                        + "goalTopHeight={GoalTop}, goalSectionFromHere={GoalSection}",
                    avatar.ObjectId,
                    _roomGrain.RoomId,
                    currentX,
                    currentY,
                    avatar.Z,
                    targetX,
                    targetY,
                    _roomGrain.MapModule.CanAvatarWalk(avatar, goalTileId),
                    _roomGrain.MapModule.GetTopSection(goalTileId).Height,
                    _roomGrain
                        .MapModule.FindSection(
                            goalTileId,
                            avatar.Z,
                            Math.Abs(_roomGrain._roomConfig.MaxStepHeight),
                            Math.Abs(_roomGrain._roomConfig.MaxDropHeight)
                        )
                        ?.Height
                );

                return false;
            }

            avatar.TilePath.Clear();
            avatar.TilePath.AddRange(
                path.Skip(1).Select(pos => _roomGrain.MapModule.ToIdx(pos.X, pos.Y))
            );

            avatar.IsWalking = true;

            return true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to walk avatar {ObjectId} to ({TargetX},{TargetY}) in room {RoomId}.",
                avatar.ObjectId,
                targetX,
                targetY,
                _roomGrain.RoomId
            );

            await StopWalkingAsync(avatar, ct);

            return false;
        }
    }

    /// <summary>
    /// Cancels an avatar's walk outright: clears its queued path, goal and move status without taking
    /// the pending step <see cref="StopWalkingAsync"/> would apply first.
    /// <para>
    /// For something that relocates an avatar instantly (a teleport). The walk tick consumes
    /// <c>TilePath[0]</c> each step, and that path was computed from the tile the avatar just left —
    /// so leaving it in place makes the avatar walk straight back towards its old goal, which looks
    /// exactly like the teleport being undone.
    /// </para>
    /// </summary>
    public void CancelWalk(IRoomAvatar avatar)
    {
        // A teleport is not an arrival: whatever was promised for the end of this walk is dropped
        // with the walk itself.
        _onWalkEnded.Remove(avatar.ObjectId.Value);

        // Mid-step the avatar already stands in the next tile's stack while X, Y still name the
        // tile it left: put it back there, or whoever removes or moves it by X, Y leaves its id
        // behind on the next tile for good. Back, not forward: the step's passage never ran.
        // A spawn's first step is in no stack yet, and is left alone.
        int next = avatar.NextTileId;

        if (
            next >= 0
            && next != _roomGrain.MapModule.ToIdx(avatar.X, avatar.Y)
            && _roomGrain._state.TileAvatarStacks[next].Contains(avatar.ObjectId)
        )
        {
            _roomGrain.MapModule.RemoveAvatarAtIdx(avatar, next, false);
            _roomGrain.MapModule.AddAvatar(avatar, false);
        }

        avatar.IsWalking = false;
        avatar.NextMoveStepAtMs = 0;
        avatar.NextMoveUpdateAtMs = 0;
        avatar.PendingStopAtMs = 0;

        avatar.TilePath.Clear();
        avatar.NextTileId = -1;
        avatar.SetGoalTileId(-1);
        avatar.RemoveStatus(AvatarStatusType.Move);
        avatar.NeedsInvoke = true;
    }

    /// <summary>Stops a player's avatar where it stands; false when the player has none here.</summary>
    public async Task<bool> StopAvatarOfPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        if (
            !_roomGrain._state.AvatarsByPlayerId.TryGetValue(playerId, out RoomObjectId objectId)
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
        )
        {
            return false;
        }

        await StopWalkingAsync(avatar, ct);

        return true;
    }

    public async Task StopWalkingAsync(IRoomAvatar avatar, CancellationToken ct)
    {
        try
        {
            if (!avatar.IsWalking)
            {
                return;
            }

            avatar.IsWalking = false;
            avatar.NextMoveStepAtMs = 0;
            avatar.NextMoveUpdateAtMs = 0;
            avatar.PendingStopAtMs = 0;

            await ProcessNextAvatarStepAsync(avatar, ct);

            avatar.TilePath.Clear();
            avatar.NextTileId = -1;
            avatar.SetGoalTileId(-1);
            avatar.RemoveStatus(AvatarStatusType.Move);
            avatar.NeedsInvoke = true;
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to stop walking for avatar {ObjectId} in room {RoomId}.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );
        }

        // Outside the block above, and with its own log: the walk is over either way, and an action
        // that throws is not a walk that failed to stop.
        if (!_onWalkEnded.Remove(avatar.ObjectId.Value, out Func<CancellationToken, Task>? onEnded))
        {
            return;
        }

        try
        {
            await onEnded(ct);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to run the end-of-walk action for avatar {ObjectId} in room {RoomId}.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );
        }
    }

    public async Task ProcessNextAvatarStepAsync(IRoomAvatar avatar, CancellationToken ct)
    {
        try
        {
            int nextTileId = avatar.NextTileId;

            if (nextTileId < 0)
            {
                return;
            }

            avatar.NextTileId = -1;

            (int nextX, int nextY) = _roomGrain.MapModule.GetTileXY(nextTileId);

            // No "already there" shortcut: a spawn is a first step onto the door, and an avatar not
            // yet placed reads as (0, 0) -- with a door on (0, 0) the shortcut skipped the spawn and
            // left the avatar off every tile stack. Removing then adding is harmless when it was on.
            _roomGrain.MapModule.RemoveAvatar(avatar, false);

            avatar.SetPosition(nextX, nextY);

            _roomGrain.MapModule.AddAvatar(avatar, false);
            _roomGrain.MapModule.UpdateHeightForAvatar(avatar);

            await OnZoneStepAsync(avatar, nextX, nextY, ct);
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to process next avatar step for avatar {ObjectId} in room {RoomId}.",
                avatar.ObjectId,
                _roomGrain.RoomId
            );

            await StopWalkingAsync(avatar, ct);
        }
    }

    /// <summary>A player's step in a zone: a passage under them, else the areas they now stand in.</summary>
    private async Task OnZoneStepAsync(IRoomAvatar avatar, int x, int y, CancellationToken ct)
    {
        if (
            _roomGrain._state.ZoneRuntime is null
            || avatar is not IRoomPlayer player
            || player.PlayerId.Value <= 0
        )
        {
            return;
        }

        if (await _roomGrain.ZonePassages.TryTakeAsync(player, x, y, ct))
        {
            return;
        }

        await _roomGrain.ZoneAreas.UpdateAsync(player.PlayerId, x, y, ct);
    }

    /// <summary>Puts a standing avatar on (x, y) at once, walk dropped.</summary>
    public void Teleport(IRoomAvatar avatar, int x, int y)
    {
        CancelWalk(avatar);

        int idx = _roomGrain.MapModule.ToIdx(x, y);

        _roomGrain.MapModule.RollAvatar(
            avatar,
            idx,
            _roomGrain.MapModule.GetTileHeightForAvatar(idx)
        );
    }
}
