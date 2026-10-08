using System;
using System.Linq;
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
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Protocol.Messages.Outgoing.Room.Permissions;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

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

    public async Task EnterRoomAsync(RoomId roomId, string password, CancellationToken ct)
    {
        // No "entry already in flight" check: this grain serialises entries, so a second request
        // runs after the first and is a reload -- the client asks for the room you are standing in
        // as readily as for any other, and by then RoomSessionManager.createSession has already
        // disposed the session and purged the room content. It must be answered in full.

        // A dungeon is played in the group's own copy (plan D21): the client is sent there, and
        // asks for it in turn.
        RoomId? target = await _grainFactory.GetRoomZone(roomId).InstanceForAsync(PlayerId);

        if (target is null)
        {
            await SendComposerAsync(
                new CantConnectMessageComposer { ErrorType = RoomConnectionErrorType.RoomFull }
            );

            return;
        }

        if (target.Value != roomId)
        {
            await SendComposerAsync(new RoomForwardMessageComposer { RoomId = target.Value });

            return;
        }

        IRoomGrain room = _grainFactory.GetRoomGrain(roomId);

        // Back within the grace period (spec Personnage §10), into the room still holding the
        // avatar: it is kept, and SetActiveRoomAsync sees the same room and makes no second one.
        bool resumed = await ResumeOrLeaveAsync(room, roomId, ct);

        await SendComposerAsync(new OpenConnectionMessageComposer { RoomId = roomId });

        // The door's context is this grain's player, never one the caller supplies: a System
        // origin is a moderator to the door, and another player's id would be judged instead.
        ActionContext ctx = new()
        {
            Origin = ActionOrigin.Player,
            SessionKey = SessionKey.Invalid,
            PlayerId = PlayerId,
            RoomId = roomId,
        };
        RoomAdmissionSnapshot admission = await room.AdmitAsync(ctx, password, resumed, ct);

        if (admission.Refusal is { } refusal)
        {
            await SendComposerAsync(new CantConnectMessageComposer { ErrorType = refusal });

            // A resumed avatar refused here would stand in the room for nobody; otherwise the room
            // was already left and this does nothing.
            await ClearActiveRoomAsync(ct);

            return;
        }

        await SendComposerAsync([.. admission.Burst]);
        await SetActiveRoomAsync(roomId, ct);
    }

    public async Task LeaveRoomAsync(CancellationToken ct)
    {
        await ClearActiveRoomAsync(ct);
        await SendComposerAsync(new CloseConnectionMessageComposer());
    }

    /// <summary>True when the player comes back, within the grace period, to the room that still
    /// holds their avatar; otherwise the room they were in is left, as any entry does. A room
    /// reloaded meanwhile (a zone republished) lost the avatar: kept as "resumed", the player would
    /// stand in it with no avatar at all.</summary>
    internal async Task<bool> ResumeOrLeaveAsync(
        IRoomAvatars room,
        RoomId roomId,
        CancellationToken ct
    )
    {
        if (
            await TakeResumeAsync(roomId)
            && (await room.GetAllAvatarSnapshotsAsync(ct)).Any(a =>
                a is RoomPlayerAvatarSnapshot && a.WebId == PlayerId.Value
            )
        )
        {
            return true;
        }

        await ClearActiveRoomAsync(ct);

        return false;
    }

    private async Task SetActiveRoomAsync(RoomId roomId, CancellationToken ct)
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
        _state.ActiveRoomSinceUtc = DateTime.UtcNow;

        // Independent: the directory entry and the summary the avatar is built from.
        Task<PlayerSummarySnapshot> summaryTask = _grainFactory
            .GetPlayerGrain(PlayerId)
            .GetSummaryAsync(ct);

        await Task.WhenAll(summaryTask, AddToDirectoryAsync(next, ct));

        PlayerSummarySnapshot playerSnapshot = await summaryTask;

        IStreamProvider? provider = this.GetStreamProvider(
            OrleansStreamProviders.ROOM_STREAM_PROVIDER
        );
        StreamId streamId = StreamId.Create(OrleansStreamNames.ROOM_STREAM, roomId.Value);
        IAsyncStream<RoomOutbound>? stream = provider.GetStream<RoomOutbound>(streamId);

        _roomOutboundSub = await stream.SubscribeAsync(this);

        IRoomAvatars room = _grainFactory.GetRoomAvatars(roomId);

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

    private async Task AddToDirectoryAsync(RoomId roomId, CancellationToken ct)
    {
        using (_metrics.MeasureRoomDirectoryCall(nameof(IRoomDirectoryGrain.AddPlayerToRoomAsync)))
        {
            await _grainFactory
                .GetRoomDirectoryGrain()
                .AddPlayerToRoomAsync(PlayerId.Value, roomId, ct);
        }
    }

    internal async Task ClearActiveRoomAsync(CancellationToken ct)
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

        // Left for real: nothing to resume any more.
        _resumableRoom = -1;
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
