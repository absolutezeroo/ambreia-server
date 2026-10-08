using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Mapping;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains.Modules;

public sealed partial class RoomAvatarModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    private int _nextObjectId = 0;

    /// <summary>
    /// What to run when an avatar's current walk ends, by room object id. One entry per avatar: a
    /// new walk replaces the old promise, which is what "walk somewhere else" means.
    /// <para>
    /// An avatar that leaves the room mid-walk leaves its entry behind; object ids are never reused
    /// within a room, so it can never fire on somebody else, and the grain takes it with it.
    /// </para>
    /// </summary>
    private readonly Dictionary<int, Func<CancellationToken, Task>> _onWalkEnded = [];

    public async Task<IRoomAvatar> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        int objectId = _nextObjectId += 1;
        int startX = _roomGrain._state.Model?.DoorX ?? 0;
        int startY = _roomGrain._state.Model?.DoorY ?? 0;
        Rotation startRot = _roomGrain._state.Model?.DoorRotation ?? Rotation.North;

        if (_roomGrain.ZoneArrival.TakeArrival(snapshot.PlayerId) is { } arrival)
        {
            (startX, startY, startRot) = arrival;
        }
        else if (!_roomGrain.MapModule.InBounds(startX, startY))
        {
            // A door outside the map means broken model data: spawning blindly on (0, 0) can drop
            // the avatar on a closed/blocked tile it can never walk off, so pick a real one.
            (startX, startY) = _roomGrain.MapModule.FindFallbackSpawnTile();
            startRot = Rotation.North;

            _roomGrain._logger.LogWarning(
                "Room {RoomId} model {ModelName} has an out-of-bounds door ({DoorX}, {DoorY}); spawning at ({StartX}, {StartY}).",
                _roomGrain.RoomId,
                _roomGrain._state.Model?.Name ?? string.Empty,
                _roomGrain._state.Model?.DoorX ?? 0,
                _roomGrain._state.Model?.DoorY ?? 0,
                startX,
                startY
            );
        }

        IRoomPlayer avatar = _roomGrain._avatarProvider.CreateAvatarFromPlayerSnapshot(
            objectId,
            snapshot
        );

        RoomControllerType controllerLevel =
            await _roomGrain.SecurityModule.GetControllerLevelAsync(ctx);

        avatar.AddStatus(AvatarStatusType.FlatControl, ((int)controllerLevel).ToString());

        avatar.NextTileId = _roomGrain.MapModule.ToIdx(startX, startY);

        await _roomGrain.ObjectModule.AttatchObjectAsync(avatar, ct);

        _roomGrain._state.AvatarsByPlayerId[snapshot.PlayerId] = avatar.ObjectId;

        avatar.SetRotation(startRot);

        return avatar;
    }

    public async Task RemoveAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_roomGrain._state.AvatarsByPlayerId.TryGetValue(
                    playerId,
                    out RoomObjectId objectId
                )
                || !_roomGrain._state.AvatarsByObjectId.TryGetValue(
                    objectId,
                    out IRoomAvatar? avatar
                )
            )
            {
                return;
            }

            try
            {
                await _roomGrain.ObjectModule.RemoveObjectAsync(ctx, avatar, ct, -1);
            }
            finally
            {
                _roomGrain._state.AvatarsByPlayerId.Remove(playerId);
                _roomGrain._state.ZoneRuntime?.Visitors.Remove(playerId);
            }
        }
        catch (Exception ex)
        {
            _roomGrain._logger.LogWarning(
                ex,
                "Failed to remove avatar for player {PlayerId} in room {RoomId}.",
                playerId,
                _roomGrain.RoomId
            );
        }
    }

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

                //throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
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
                            Math.Abs(_roomGrain._roomConfig.MaxStepHeight)
                        )
                        ?.Height
                );

                return false;

                // throw new VortexException(VortexErrorCodeEnum.InvalidMoveTarget);
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

    public Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    ) =>
        Task.FromResult(
            _roomGrain
                ._state.AvatarsByObjectId.Values.Select(x => x.GetSnapshot())
                .ToImmutableArray()
        );

    /// <summary>
    /// Cancels an avatar's walk outright: clears its queued path, goal and move status without taking
    /// the pending step <see cref="StopWalkingAsync"/> would apply first.
    /// <para>
    /// For something that relocates an avatar instantly (a teleport); nothing calls it yet. The walk tick
    /// consumes <c>TilePath[0]</c> each step, and that path was computed from the tile the avatar just
    /// left — so leaving it in place makes the avatar walk straight back towards its old goal, which
    /// looks exactly like the teleport being undone.
    /// </para>
    /// </summary>
    public void CancelWalk(IRoomAvatar avatar)
    {
        // A teleport is not an arrival: whatever was promised for the end of this walk is dropped
        // with the walk itself.
        _onWalkEnded.Remove(avatar.ObjectId.Value);

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

    public Task<bool> UpdateAvatarWithPlayerAsync(
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        if (
            snapshot.PlayerId <= 0
            || !_roomGrain._state.AvatarsByPlayerId.TryGetValue(
                snapshot.PlayerId,
                out RoomObjectId objectId
            )
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(objectId, out IRoomAvatar? avatar)
            || avatar is not IRoomPlayer avatarPlayer
            || !avatarPlayer.UpdateWithPlayer(snapshot)
        )
        {
            return Task.FromResult(false);
        }

        _roomGrain
            .SendComposerToRoomAsync(
                new UserChangeMessageComposer
                {
                    ObjectId = avatarPlayer.ObjectId,
                    Figure = avatarPlayer.Figure,
                    Gender = avatarPlayer.Gender,
                    CustomInfo = avatarPlayer.Motto,
                    AchievementScore = snapshot.AchievementScore,
                }
            )
            .LogAndForget(
                _roomGrain._logger,
                "Failed to publish avatar update for room {RoomId}",
                _roomGrain._state.RoomId
            );

        return Task.FromResult(true);
    }

    /// <summary>
    /// Sitting or laying cancels a dance on the avatar itself and standing up resumes it, but the
    /// room only ever learns about a dance through <see cref="DanceMessageComposer"/> — without
    /// this the other clients keep rendering the dance of someone who has sat down, and see
    /// nothing when they stand back up. Call it after any posture change with the dance the avatar
    /// had beforehand. No packet starts a dance yet; the state is kept for when one does.
    /// </summary>
    /// <remarks>
    /// This was <c>BroadcastDanceIfCleared</c> and returned early when the previous dance was
    /// <see cref="AvatarDanceType.None"/>, which made it deaf to exactly one direction: the
    /// resume. Standing up goes None → dancing, so the guard that read as "nothing was cancelled,
    /// nothing to announce" swallowed every restored dance.
    /// </remarks>
    public void BroadcastDanceIfChanged(IRoomAvatar avatar, AvatarDanceType previousDanceType)
    {
        if (avatar is not IRoomPlayer player || player.DanceType == previousDanceType)
        {
            return;
        }

        _roomGrain
            .SendComposerToRoomAsync(
                new DanceMessageComposer
                {
                    ObjectId = avatar.ObjectId,
                    DanceType = player.DanceType,
                }
            )
            .LogAndForget(
                _roomGrain._logger,
                "Failed to publish avatar update for room {RoomId}",
                _roomGrain._state.RoomId
            );
    }

    public Task<bool> LookToAvatarAsync(
        RoomObjectId objectId,
        int targetX,
        int targetY,
        CancellationToken ct
    )
    {
        if (
            objectId <= 0
            || !_roomGrain._state.AvatarsByObjectId.TryGetValue(
                objectId.Value,
                out IRoomAvatar? avatar
            )
        )
        {
            return Task.FromResult(false);
        }

        int dx = targetX - avatar.X;
        int dy = targetY - avatar.Y;

        if (dx == 0 && dy == 0)
        {
            return Task.FromResult(false);
        }

        Rotation rotation = RotationExtensions.FromDelta(dx, dy);
        avatar.SetHeadRotation(rotation);

        if (!avatar.HasStatus(AvatarStatusType.Sit, AvatarStatusType.Lay))
        {
            avatar.SetBodyRotation(rotation);
        }

        _roomGrain
            .SendComposerToRoomAsync(
                new UserUpdateMessageComposer { Avatars = [avatar.GetSnapshot()] }
            )
            .LogAndForget(
                _roomGrain._logger,
                "Failed to publish avatar update for room {RoomId}",
                _roomGrain._state.RoomId
            );

        return Task.FromResult(true);
    }
}
