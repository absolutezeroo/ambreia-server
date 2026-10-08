using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Streams;
using Vortex.Logging.Extensions;
using Vortex.Players.Configuration;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Room.Session;

namespace Vortex.Players.Grains;

internal sealed partial class PlayerPresenceGrain
    : Grain,
        IPlayerPresenceGrain,
        IAsyncObserver<RoomOutbound>
{
    internal readonly IEventPublisher _events;
    internal readonly IGrainFactory _grainFactory;
    private readonly ILogger<PlayerPresenceGrain> _logger;
    internal readonly IVortexMetrics _metrics;
    internal readonly PlayerPresenceConfig _config;

    private readonly Queue<IComposer> _outgoingQueue = new();
    internal readonly PlayerPresenceLiveState _state;
    private bool _isProcessingQueue;

    // Set once the outgoing queue has overflowed and the session has been told to close. Cleared
    // when a new socket registers.
    private bool _sessionOverflowed;
    private StreamSubscriptionHandle<RoomOutbound>? _roomOutboundSub;

    private ISessionContextObserver? _sessionObserver;

    /// <summary>
    /// The socket currently holding the player, for the test that pins the replacement ordering.
    /// Reading it is the only way to tell the two sockets apart without driving the outgoing queue,
    /// which is processed off the caller's turn and would make the assertion a race.
    /// </summary>
    internal ISessionContextObserver? AttachedObserver => _sessionObserver;

    /// <summary>
    /// Which socket generation currently holds this player. Handed out by
    /// <see cref="BeginSessionAsync"/> and checked when a socket tries to attach, so one that was
    /// replaced mid-attach cannot take the player back.
    /// </summary>
    private long _sessionGeneration;

    public PlayerPresenceGrain(
        IGrainFactory grainFactory,
        IEventPublisher events,
        ILogger<PlayerPresenceGrain> logger,
        IVortexMetrics metrics,
        IOptions<PlayerPresenceConfig> config,
        IPermissionService permissions,
        IZoneRoomSource zoneSource
    )
    {
        _grainFactory = grainFactory;
        _events = events;
        _logger = logger;
        _metrics = metrics;
        _config = config.Value;
        _permissions = permissions;
        _zoneSource = zoneSource;

        _state = new PlayerPresenceLiveState();
    }

    public Task OnNextAsync(RoomOutbound item, StreamSequenceToken? token = null)
    {
        if (
            _sessionObserver is null
            || (
                item.ExcludedPlayerIds is not null
                && item.ExcludedPlayerIds.Contains((int)this.GetPrimaryKeyLong())
            )
        )
        {
            return Task.CompletedTask;
        }

        return SendComposerAsync(item.Composer);
    }

    public Task OnCompletedAsync()
    {
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Room outbound stream error for player {PlayerId}",
            this.GetPrimaryKeyLong()
        );

        return Task.CompletedTask;
    }

    public Task<long> BeginSessionAsync(CancellationToken ct)
    {
        // Monotonic within the activation, and the activation is the player: Orleans runs one call
        // here at a time, so whoever asks last gets the higher number. That is the whole ordering.
        //
        // It does not survive a reactivation, and does not need to: an activation with no observer
        // has nobody attached, so the first socket after it is the current one by definition.
        return Task.FromResult(++_sessionGeneration);
    }

    public Task RegisterSessionObserverAsync(ISessionContextObserver observer, long generation)
    {
        if (generation < _sessionGeneration)
        {
            // A socket that was replaced while it was still attaching. Registering it would put a
            // dead socket in front of the live one and the player would go quiet -- connected as far
            // as the server is concerned, hearing nothing. Its own teardown still runs; the gateway
            // mapping already refuses to let that teardown touch the session that replaced it.
            _logger.LogWarning(
                "Ignoring a session observer for player {PlayerId} from generation {Generation}; "
                    + "generation {Current} holds the player.",
                this.GetPrimaryKeyLong(),
                generation,
                _sessionGeneration
            );

            return Task.CompletedTask;
        }

        ResumeIfWaiting();

        _sessionObserver = observer;

        // A fresh socket is a fresh queue. The grain outlives the session it was serving, so the
        // give-up flag has to be cleared here or the reconnect this player was just told to make
        // would be answered with nothing at all.
        _sessionOverflowed = false;
        _outgoingQueue.Clear();

        // This activation now holds a socket, so it must outlive the collection age. Nothing else
        // keeps it awake: a player who sends no packet for GrainCollectionAge (two minutes -- an
        // idle tab, a sleeping machine, a cable pulled without a FIN) was collected, and collection
        // runs OnDeactivateAsync, which unregisters this very observer and walks them out of their
        // room. They came back a ghost: still connected, gone from the room, and mute -- the fresh
        // activation has no observer, so every reply queued behind one that is never drained. Released again in UnregisterSessionObserverAsync, so an
        // activation without a socket is still collectable.
        DelayDeactivation(TimeSpan.MaxValue);

        return Task.CompletedTask;
    }

    public async Task UnregisterSessionObserverAsync(CancellationToken ct)
    {
        if (ShouldWaitForReconnect())
        {
            await BeginGraceAsync(ct);

            return;
        }

        await LeaveNowAsync(ct);
    }

    /// <summary>Out of the room and collectable: an ordinary disconnect, the end of a grace period,
    /// or a deactivation.</summary>
    private async Task LeaveNowAsync(CancellationToken ct)
    {
        StopGraceTimer();

        try
        {
            await ClearActiveRoomAsync(ct);
        }
        finally
        {
            // Whatever leaving the room did, the socket is gone: a kept observer would queue for
            // nobody, and the keep-alive taken in RegisterSessionObserverAsync goes with it.
            // Harmless on the OnDeactivateAsync path, which is already deactivating.
            _sessionObserver = null;

            DeactivateOnIdle();
        }
    }

    public Task<bool> IsOnlineAsync(CancellationToken ct)
    {
        return Task.FromResult(_sessionObserver is not null);
    }

    public Task SendComposerAsync(IComposer composer)
    {
        if (composer is not null)
        {
            EnqueueOutgoing(composer);

            LogAndForget(ProcessOutgoingQueueAsync());
        }

        return Task.CompletedTask;
    }

    public Task SendComposerAsync(params IComposer[] composers)
    {
        if (composers.Length > 0)
        {
            foreach (IComposer composer in composers)
            {
                EnqueueOutgoing(composer);
            }

            LogAndForget(ProcessOutgoingQueueAsync());
        }

        return Task.CompletedTask;
    }

    private void EnqueueOutgoing(IComposer composer)
    {
        // Already given up on this session: everything after the close is noise the client will
        // never read, and re-filling the queue behind it would only delay the close.
        if (_sessionOverflowed)
        {
            return;
        }

        if (_outgoingQueue.Count < _config.MaxOutgoingQueueSize)
        {
            _outgoingQueue.Enqueue(composer);

            return;
        }

        // The queue used to drop its OLDEST entry here and carry on. That cannot work: this
        // protocol is ordered and cumulative, and the client builds its world out of the sequence.
        // Drop the ObjectAdd and keep the ObjectUpdate that refers to it and the client is wrong
        // about the room for as long as it stays in it -- no error, no disconnect, no way for it to
        // notice, and every later packet layered on top of a state the server does not share.
        //
        // Overflowing is not a normal condition either: it means this session is not draining, and
        // a session that is not draining is already gone in every way but the socket. So it is
        // fatal. The queue is cleared and replaced by the one composer worth sending -- the client
        // closes, reconnects, and rebuilds its world from a login, which is the only cheap way back
        // to a state both ends agree on.
        _outgoingQueue.Clear();
        _outgoingQueue.Enqueue(new CloseConnectionMessageComposer());
        _sessionOverflowed = true;

        _logger.LogError(
            "Outgoing composer queue for player {PlayerId} reached {MaxOutgoingQueueSize} and the "
                + "session is not draining; closing it rather than desynchronising the client.",
            this.GetPrimaryKeyLong(),
            _config.MaxOutgoingQueueSize
        );
    }

    public override Task OnActivateAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _outgoingQueue.Clear();

        try
        {
            // A deactivation leaves at once, grace period or not.
            await LeaveNowAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to unregister session observer for player {PlayerId} on deactivation",
                this.GetPrimaryKeyLong()
            );
        }

        if (_roomOutboundSub is not null)
        {
            try
            {
                await _roomOutboundSub.UnsubscribeAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to unsubscribe room outbound stream for player {PlayerId} on deactivation",
                    this.GetPrimaryKeyLong()
                );
            }

            _roomOutboundSub = null;
        }
    }

    private async Task ProcessOutgoingQueueAsync()
    {
        if (_isProcessingQueue)
        {
            return;
        }

        _isProcessingQueue = true;

        try
        {
            await Task.Yield();

            if (_sessionObserver is not null)
            {
                while (_outgoingQueue.Count > 0)
                {
                    IComposer payload = _outgoingQueue.Dequeue();

                    await _sessionObserver.SendComposerAsync(payload);
                }
            }
        }
        finally
        {
            // A throwing send used to latch this flag for the rest of the activation: the player
            // stayed connected and simulated while their client never received anything again --
            // no disconnect, no error, just silence. The flag has to come back down either way.
            _isProcessingQueue = false;
        }
    }

    private void LogAndForget(Task task) =>
        task.LogAndForget(
            _logger,
            "Unhandled error while processing outgoing composer queue for player {PlayerId}",
            this.GetPrimaryKeyLong()
        );
}
