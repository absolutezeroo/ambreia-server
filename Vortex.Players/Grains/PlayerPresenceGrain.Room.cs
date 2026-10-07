using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using Vortex.Primitives.Action;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerPresenceGrain
{
    public Task<RoomPointerSnapshot> GetActiveRoomAsync() =>
        Task.FromResult(
            new RoomPointerSnapshot
            {
                RoomId = _state.ActiveRoomId,
                ActiveSinceUtc = _state.ActiveRoomSinceUtc,
            }
        );

    public Task<RoomPendingSnapshot> GetPendingRoomAsync() =>
        Task.FromResult(
            new RoomPendingSnapshot
            {
                RoomId = _state.PendingRoomId,
                Approved = _state.PendingRoomApproved,
            }
        );

    public async Task SetActiveRoomAsync(RoomId roomId, CancellationToken ct)
    {
        if (roomId <= 0)
        {
            return;
        }

        if (_state.ActiveRoomId == roomId)
        {
            return;
        }

        await ClearActiveRoomAsync(ct);

        RoomId next = roomId;

        _state.ActiveRoomId = next;
        _state.PendingRoomId = -1;
        _state.PendingRoomApproved = false;
        _state.ActiveRoomSinceUtc = DateTime.UtcNow;

        using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.AddPlayerToRoomAsync)))
        {
            await _grainFactory
                .GetRoomDirectoryGrain()
                .AddPlayerToRoomAsync((int)this.GetPrimaryKeyLong(), next, ct);
        }

        IStreamProvider? provider = this.GetStreamProvider(
            OrleansStreamProviders.ROOM_STREAM_PROVIDER
        );
        StreamId streamId = StreamId.Create(OrleansStreamNames.ROOM_STREAM, roomId.Value);
        IAsyncStream<RoomOutbound>? stream = provider.GetStream<RoomOutbound>(streamId);

        _roomOutboundSub = await stream.SubscribeAsync(this);

        IRoomAvatars room = _grainFactory.GetRoomAvatars(roomId);

        PlayerSummarySnapshot playerSnapshot = await _grainFactory
            .GetPlayerGrain((PlayerId)this.GetPrimaryKeyLong())
            .GetSummaryAsync(ct);

        ActionContext ctx = new ActionContext
        {
            Origin = ActionOrigin.Player,
            SessionKey = SessionKey.Invalid,
            PlayerId = (PlayerId)this.GetPrimaryKeyLong(),
            RoomId = roomId,
        };

        bool entered = await room.CreateAvatarFromPlayerAsync(ctx, playerSnapshot, ct);

        if (entered)
        {
            DateTime enteredAt = DateTime.UtcNow;
            _state.ActiveRoomSinceUtc = enteredAt;
            await _events.PublishAsync(
                new PlayerEnteredRoomEvent(
                    (PlayerId)this.GetPrimaryKeyLong(),
                    roomId.Value,
                    enteredAt
                ),
                ct
            );
        }
    }

    public async Task ClearActiveRoomAsync(CancellationToken ct)
    {
        if (_state.ActiveRoomId <= 0)
        {
            return;
        }

        RoomId prev = _state.ActiveRoomId;
        DateTime leftAt = DateTime.UtcNow;
        TimeSpan duration = leftAt - _state.ActiveRoomSinceUtc;
        long durationSeconds = (long)duration.TotalSeconds;

        ActionContext ctx = new ActionContext
        {
            Origin = ActionOrigin.Player,
            SessionKey = SessionKey.Invalid,
            PlayerId = PlayerId.Parse((int)this.GetPrimaryKeyLong()),
            RoomId = prev,
        };

        IRoomAvatars roomGrain = _grainFactory.GetRoomAvatars(prev);

        // The room call is best-effort: if it fails or is cancelled (this also runs from
        // OnDeactivateAsync, whose token is cancelled once the grace period elapses), the player must
        // still stop counting as an occupant here and in the directory, or they linger as a ghost in
        // the room's occupant count and cannot cleanly re-enter.
        try
        {
            await roomGrain.RemoveAvatarFromPlayerAsync(ctx, ctx.PlayerId, ct);
            await _events.PublishAsync(
                new PlayerLeftRoomEvent(
                    ctx.PlayerId,
                    prev.Value,
                    leftAt,
                    Math.Max(0, durationSeconds)
                ),
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove player {PlayerId} from room {RoomId}; clearing local room state anyway",
                this.GetPrimaryKeyLong(),
                prev.Value
            );
        }

        _state.ActiveRoomId = -1;
        _state.ActiveRoomSinceUtc = leftAt;

        using (
            _metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.RemovePlayerFromRoomAsync))
        )
        {
            await _grainFactory
                .GetRoomDirectoryGrain()
                .RemovePlayerFromRoomAsync(
                    (PlayerId)this.GetPrimaryKeyLong(),
                    prev,
                    CancellationToken.None
                );
        }

        if (_roomOutboundSub is not null)
        {
            await _roomOutboundSub.UnsubscribeAsync();

            _roomOutboundSub = null;
        }
    }

    public Task SetPendingRoomAsync(RoomId roomId, bool approved)
    {
        _state.PendingRoomId = roomId;
        _state.PendingRoomApproved = approved;

        return Task.CompletedTask;
    }

    public async Task OnControllerLevelUpdatedAsync(
        RoomId roomId,
        RoomControllerType controllerType,
        CancellationToken ct
    )
    {
        if (_state.ActiveRoomId != roomId)
        {
            return;
        }

        if (controllerType >= RoomControllerType.Rights)
        {
            await SendComposerAsync(
                new YouAreControllerMessageComposer
                {
                    RoomId = roomId,
                    ControllerLevel = controllerType,
                }
            );

            if (controllerType >= RoomControllerType.Owner)
            {
                await SendComposerAsync(new YouAreOwnerMessageComposer { RoomId = roomId });
            }

            return;
        }

        await SendComposerAsync(new YouAreNotControllerMessageComposer { RoomId = roomId });
    }
}
