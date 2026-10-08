using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Players.Grains;

/// <summary>
/// The grace period (spec Personnage §10): a socket that drops while its player is in a room leaves
/// the avatar there, still, for <c>DisconnectGraceSeconds</c>. A new socket in time takes it back —
/// sign-in forwards to that room and the entry keeps the avatar — otherwise it leaves as before.
/// </summary>
internal sealed partial class PlayerPresenceGrain
{
    /// <summary>Margin over the grace so the activation outlives its own timer.</summary>
    private static readonly TimeSpan GraceKeepAliveMargin = TimeSpan.FromSeconds(10);

    private IDisposable? _graceTimer;

    /// <summary>Internal for the sign-in tests, which put a player back in grace directly.</summary>
    internal RoomId _resumableRoom = -1;

    internal Task<RoomId?> GetResumableRoomAsync() =>
        Task.FromResult(_resumableRoom > 0 ? (RoomId?)_resumableRoom : null);

    internal Task<bool> TakeResumeAsync(RoomId roomId)
    {
        if (_resumableRoom <= 0 || _resumableRoom != roomId)
        {
            return Task.FromResult(false);
        }

        _resumableRoom = -1;

        return Task.FromResult(true);
    }

    /// <summary>A dropped socket of a player in a room waits; one outside a room (Quit) does not.</summary>
    private bool ShouldWaitForReconnect() =>
        _state.ActiveRoomId > 0
        && _config.DisconnectGraceSeconds > 0
        && _sessionObserver is not null
        && _graceTimer is null;

    private async Task BeginGraceAsync(CancellationToken ct)
    {
        RoomId room = _state.ActiveRoomId;
        TimeSpan grace = TimeSpan.FromSeconds(_config.DisconnectGraceSeconds);

        _sessionObserver = null;
        _outgoingQueue.Clear();

        try
        {
            ActionContext ctx = new()
            {
                Origin = ActionOrigin.Player,
                SessionKey = SessionKey.Invalid,
                PlayerId = PlayerId.Parse((int)this.GetPrimaryKeyLong()),
                RoomId = room,
            };

            await _grainFactory.GetRoomAvatars(room).StopAvatarAsync(ctx, ctx.PlayerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not stop the avatar of player {PlayerId} in room {RoomId} for its grace period.",
                this.GetPrimaryKeyLong(),
                room.Value
            );
        }

        // The timer's token is not passed on: ending the grace stops the timer first, which cancels
        // it, and a leave sent with it never reached the room -- the avatar stayed as a ghost.
        _graceTimer = this.RegisterGrainTimer<object?>(
            static async (self, _) => await ((PlayerPresenceGrain)self!).EndGraceAsync(),
            this,
            grace,
            Timeout.InfiniteTimeSpan
        );

        // Released by the sign-in that comes back, or by EndGraceAsync.
        DelayDeactivation(grace + GraceKeepAliveMargin);

        _logger.LogInformation(
            "Player {PlayerId} dropped in room {RoomId}; the avatar waits {Seconds}s for a reconnect.",
            this.GetPrimaryKeyLong(),
            room.Value,
            _config.DisconnectGraceSeconds
        );
    }

    /// <summary>The grace ran out: leave the room as an ordinary disconnect would have.</summary>
    internal async Task EndGraceAsync()
    {
        StopGraceTimer();

        if (_sessionObserver is not null)
        {
            return;
        }

        _logger.LogInformation(
            "Player {PlayerId} did not come back within the grace period; leaving room {RoomId}.",
            this.GetPrimaryKeyLong(),
            _state.ActiveRoomId.Value
        );

        await LeaveNowAsync(CancellationToken.None);
    }

    /// <summary>A socket attached while the avatar was waiting: the room is resumable.</summary>
    private void ResumeIfWaiting()
    {
        if (_graceTimer is null)
        {
            return;
        }

        StopGraceTimer();
        _resumableRoom = _state.ActiveRoomId;
    }

    private void StopGraceTimer()
    {
        _graceTimer?.Dispose();
        _graceTimer = null;
    }
}
