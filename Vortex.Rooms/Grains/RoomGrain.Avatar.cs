using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Events.Player;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Action;

namespace Vortex.Rooms.Grains;

public sealed partial class RoomGrain
{
    public async Task<bool> CreateAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        try
        {
            // An entry is several calls, and the activation can go between them (a zone republished
            // mid-entry): the avatar must not land in an activation that never built its map.
            await EnsureRoomActiveAsync(ct);

            await AvatarModule.CreateAvatarFromPlayerAsync(ctx, snapshot, ct);

            await PublishRoomEventAsync(
                new PlayerEnterEvent
                {
                    RoomId = _state.RoomId,
                    CausedBy = ctx,
                    PlayerId = snapshot.PlayerId,
                },
                ct
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to create avatar for player {snapshot.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<bool> RemoveAvatarFromPlayerAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        try
        {
            await AvatarModule.RemoveAvatarFromPlayerAsync(ctx, playerId, ct);

            await PublishRoomEventAsync(
                new PlayerLeftEvent
                {
                    RoomId = _state.RoomId,
                    CausedBy = ctx,
                    PlayerId = playerId,
                },
                ct
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to remove avatar for player {playerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<bool> WalkAvatarToAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        int? targetZKey,
        CancellationToken ct
    )
    {
        try
        {
            return await AvatarModule.WalkAvatarToAsync(ctx, targetX, targetY, targetZKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to walk avatar for player {ctx.PlayerId} in room {_state.RoomId} to ({targetX}, {targetY})."
            );

            return false;
        }
    }

    public async Task<bool> StopAvatarAsync(
        ActionContext ctx,
        PlayerId playerId,
        CancellationToken ct
    )
    {
        try
        {
            return await AvatarModule.StopAvatarOfPlayerAsync(playerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to stop the avatar of player {PlayerId} in room {RoomId}.",
                playerId,
                _state.RoomId
            );

            return false;
        }
    }

    public async Task<bool> UpdateAvatarWithPlayerAsync(
        PlayerSummarySnapshot snapshot,
        CancellationToken ct
    )
    {
        try
        {
            return await AvatarModule.UpdateAvatarWithPlayerAsync(snapshot, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to update avatar for player {snapshot.PlayerId} in room {_state.RoomId}"
            );

            return false;
        }
    }

    public async Task<bool> LookToAvatarAsync(
        ActionContext ctx,
        int targetX,
        int targetY,
        CancellationToken ct
    )
    {
        try
        {
            if (
                !_state.AvatarsByPlayerId.TryGetValue(ctx.PlayerId, out RoomObjectId objectId)
                || !await AvatarModule.LookToAvatarAsync(objectId, targetX, targetY, ct)
            )
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to look-to ({targetX},{targetY}) for player {ctx.PlayerId} in room {_state.RoomId}."
            );

            return false;
        }
    }

    public async Task<ImmutableArray<RoomAvatarSnapshot>> GetAllAvatarSnapshotsAsync(
        CancellationToken ct
    )
    {
        return await AvatarModule.GetAllAvatarSnapshotsAsync(ct).ConfigureAwait(true);
    }
}
