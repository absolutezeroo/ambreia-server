using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Logging.Extensions;
using Vortex.Primitives.Action;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;

namespace Vortex.Rooms.Grains.Modules;

/// <summary>
/// The avatars of a room: a player's avatar appearing and leaving, its look kept up to date, where
/// it looks. Moving them is <c>RoomAvatarModule.Walk.cs</c>.
/// </summary>
public sealed partial class RoomAvatarModule(RoomGrain roomGrain)
{
    private readonly RoomGrain _roomGrain = roomGrain;

    private int _nextObjectId = 0;

    /// <summary>A fresh avatar object id, never reused within the activation: a monster takes its
    /// own from the same counter as the players.</summary>
    public int NextObjectId() => _nextObjectId += 1;

    public async Task<IRoomAvatar> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        int objectId = NextObjectId();
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

        await JoinFightsAsync(snapshot.PlayerId, objectId, ct);

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

            // Health and mana back to the character first, awaited: the next zone reads them.
            await LeaveFightsAsync(playerId, ct);

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

    public Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    ) =>
        Task.FromResult(
            _roomGrain
                ._state.AvatarsByObjectId.Values.Select(x => x.GetSnapshot())
                .ToImmutableArray()
        );

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
