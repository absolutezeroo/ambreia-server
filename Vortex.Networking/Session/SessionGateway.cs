using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Vortex.Networking.Configuration;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;

namespace Vortex.Networking.Session;

public sealed class SessionGateway(
    IGrainFactory grainFactory,
    IEventPublisher events,
    IOptions<NetworkingConfig> networkingConfig,
    ILogger<SessionGateway> logger
) : ISessionGateway
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IEventPublisher _events = events;
    private readonly NetworkingConfig _networkingConfig = networkingConfig.Value;
    private readonly ILogger<SessionGateway> _logger = logger;

    private readonly ConcurrentDictionary<SessionKey, ISessionContext> _sessions = new();
    private readonly ConcurrentDictionary<SessionKey, ObserverEntry> _sessionObservers = new();
    private readonly ConcurrentDictionary<SessionKey, PlayerId> _sessionToPlayer = new();
    private readonly ConcurrentDictionary<PlayerId, SessionKey> _playerToSession = new();
    private readonly ConcurrentDictionary<PlayerId, DateTime> _playerConnectedAt = new();

    /// <summary>
    /// Live session count per remote address, and the address each session was ADMITTED under.
    /// The second map is what makes the count survive: on close the context may already be torn
    /// down and its <see cref="ISessionContext.RemoteIpAddress"/> null, so a decrement that re-read
    /// the address from the context would leak a slot per disconnect until the cap refused
    /// everybody sharing that address (SEC-10).
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _sessionsByIpAddress = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<SessionKey, string> _ipAddressBySession = new();

    /// <summary>
    ///     Serializes mutation of the <see cref="_sessionToPlayer" />/<see cref="_playerToSession" />
    ///     pair so a session/player mapping is never left half-updated by interleaved connects and
    ///     disconnects. Only the map-mutation critical sections below hold this; outbound grain calls
    ///     (observer registration, event publishing) run after release.
    /// </summary>
    private readonly SemaphoreSlim _mappingGate = new(1, 1);

    private sealed record ObserverEntry(SessionContextObserver Impl, ISessionContextObserver Ref);

    public ISessionContext? GetSession(SessionKey key) =>
        _sessions.TryGetValue(key, out ISessionContext? ctx) ? ctx : null;

    public ISessionContextObserver? GetSessionObserver(SessionKey key) =>
        _sessionObservers.TryGetValue(key, out ObserverEntry? observer) ? observer.Ref : null;

    public PlayerId GetPlayerId(SessionKey key) =>
        _sessionToPlayer.TryGetValue(key, out PlayerId playerId) ? playerId : -1;

    public int GetActiveSessionCount() => _sessions.Count;

    public bool IsOnline(PlayerId playerId) => _playerToSession.ContainsKey(playerId);

    public int GetOnlinePlayerCount() => _playerToSession.Count;

    public IReadOnlyCollection<PlayerId> GetOnlinePlayerIds() => _playerToSession.Keys.ToArray();

    public Task<bool> AddSessionAsync(SessionKey key, ISessionContext ctx)
    {
        if (!TryAdmit(key, ctx))
        {
            return Task.FromResult(false);
        }

        _sessions[key] = ctx;

        _sessionObservers.AddOrUpdate(
            key,
            _ =>
            {
                SessionContextObserver impl = new SessionContextObserver(key, this);
                ISessionContextObserver objRef =
                    _grainFactory.CreateObjectReference<ISessionContextObserver>(impl);

                return new ObserverEntry(impl, objRef);
            },
            (_, existing) => existing
        );

        return Task.FromResult(true);
    }

    /// <summary>
    /// Counts this connection against the global and per-address caps, admitting it only when both
    /// have room. The slot is reserved before this returns true, so two connections racing for the
    /// last one cannot both win.
    /// </summary>
    private bool TryAdmit(SessionKey key, ISessionContext ctx)
    {
        int maxTotal = _networkingConfig.MaxTotalSessions;

        if (maxTotal > 0 && _sessions.Count >= maxTotal)
        {
            _logger.LogWarning(
                "Refusing session {SessionKey}: at the hotel-wide session ceiling ({MaxTotal}).",
                key,
                maxTotal
            );

            return false;
        }

        int maxPerIp = _networkingConfig.MaxSessionsPerIpAddress;
        string? ipAddress = ctx.RemoteIpAddress;

        // Nothing to attribute it to -- a loopback transport, a test double -- means no per-address
        // cap to apply. The global ceiling above already counted it.
        if (maxPerIp <= 0 || string.IsNullOrEmpty(ipAddress))
        {
            return true;
        }

        // AddOrUpdate IS the reservation: the increment and the test are one atomic step, so the
        // count can never be read stale by a second connection arriving at the same instant.
        int held = _sessionsByIpAddress.AddOrUpdate(ipAddress, 1, static (_, n) => n + 1);

        if (held > maxPerIp)
        {
            // Hand the slot straight back. Leaving the increment in place would let a refused
            // attacker exhaust the cap for everyone sharing the address -- the refusal would
            // become the attack.
            ReleaseIpSlot(ipAddress);

            _logger.LogWarning(
                "Refusing session {SessionKey} from {IpAddress}: {Held} concurrent sessions, cap {MaxPerIp}.",
                key,
                ipAddress,
                held - 1,
                maxPerIp
            );

            return false;
        }

        _ipAddressBySession[key] = ipAddress;

        return true;
    }

    private void ReleaseIpSlot(string ipAddress) =>
        _sessionsByIpAddress.AddOrUpdate(ipAddress, 0, static (_, n) => n - 1 < 0 ? 0 : n - 1);

    public async Task RemoveSessionAsync(SessionKey key, CancellationToken ct)
    {
        if (_sessionToPlayer.TryGetValue(key, out PlayerId playerId) && playerId > 0)
        {
            await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                await RemovePlayerSessionCoreAsync(playerId, key, closeTransport: false, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                _mappingGate.Release();
            }
        }

        if (_sessionObservers.TryRemove(key, out ObserverEntry? observer))
        {
            try
            {
                _grainFactory.DeleteObjectReference<ISessionContextObserver>(observer.Ref);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to delete session observer for session {SessionKey}",
                    key
                );
            }
        }

        _sessions.TryRemove(key, out _);

        // Keyed on what the session was ADMITTED under, not on the context, which by now may be
        // torn down and reporting no address at all.
        if (_ipAddressBySession.TryRemove(key, out string? admittedIpAddress))
        {
            ReleaseIpSlot(admittedIpAddress);
        }
    }

    public async Task AddSessionToPlayerAsync(
        SessionKey key,
        PlayerId playerId,
        CancellationToken ct = default
    )
    {
        ISessionContextObserver? observer = GetSessionObserver(key);

        if (observer is null)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);
        DateTime connectedAt;
        long generation;

        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (
                _sessionToPlayer.TryGetValue(key, out PlayerId currentPlayerId)
                && currentPlayerId > 0
                && currentPlayerId != playerId
            )
            {
                await RemovePlayerSessionCoreAsync(currentPlayerId, key, closeTransport: false, ct)
                    .ConfigureAwait(false);
            }

            if (
                _playerToSession.TryGetValue(playerId, out SessionKey existingSessionKey)
                && existingSessionKey != key
            )
            {
                await RemovePlayerSessionCoreAsync(
                        playerId,
                        existingSessionKey,
                        closeTransport: true,
                        ct
                    )
                    .ConfigureAwait(false);
            }

            _sessionToPlayer[key] = playerId;
            _playerToSession[playerId] = key;
            connectedAt = DateTime.UtcNow;
            _playerConnectedAt[playerId] = connectedAt;

            // Claimed here, with the mapping, so the order generations come out in is the order the
            // replacements were decided. The registration itself stays outside the lock -- it is the
            // ordering that had to be established inside it, not the call.
            generation = await playerPresence.BeginSessionAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _mappingGate.Release();
        }

        // Outside the lock, and therefore raceable against a socket that replaces this one before
        // the call lands. The generation is what the presence grain uses to refuse it: a socket that
        // was replaced while it was still attaching would otherwise reinstate itself in front of the
        // live one, and the player would go mute while the server believed they were attached.
        await playerPresence
            .RegisterSessionObserverAsync(observer, generation)
            .ConfigureAwait(false);
        await PublishConnectedEventSafelyAsync(playerId, connectedAt).ConfigureAwait(false);
    }

    private async Task PublishConnectedEventSafelyAsync(PlayerId playerId, DateTime connectedAt)
    {
        try
        {
            await _events
                .PublishAsync(new PlayerConnectedEvent(playerId, connectedAt))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish player connected lifecycle event for {PlayerId}",
                playerId
            );
        }
    }

    public async Task RemoveSessionFromPlayerAsync(PlayerId playerId, CancellationToken ct)
    {
        if (!_playerToSession.TryGetValue(playerId, out SessionKey sessionKey))
        {
            return;
        }

        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await RemovePlayerSessionCoreAsync(playerId, sessionKey, closeTransport: true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _mappingGate.Release();
        }
    }

    /// <summary>
    ///     Removes the session/player mapping pair and notifies dependents. Callers must hold
    ///     <see cref="_mappingGate" /> before invoking this — it is not reentrant-safe on its own.
    /// </summary>
    private async Task RemovePlayerSessionCoreAsync(
        PlayerId playerId,
        SessionKey sessionKey,
        bool closeTransport,
        CancellationToken ct
    )
    {
        bool removedActiveMapping = TryRemovePair(_playerToSession, playerId, sessionKey);
        TryRemovePair(_sessionToPlayer, sessionKey, playerId);

        if (!removedActiveMapping)
        {
            return;
        }

        IPlayerPresenceGrain playerPresence = _grainFactory.GetPlayerPresenceGrain(playerId);

        try
        {
            await playerPresence.UnregisterSessionObserverAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to unregister session observer for player {PlayerId}",
                playerId
            );
        }

        if (_playerConnectedAt.TryRemove(playerId, out DateTime connectedAt))
        {
            await PublishDisconnectedEventSafelyAsync(playerId, connectedAt).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Player {PlayerId} disconnected without a recorded connect timestamp; skipping disconnect duration.",
                playerId
            );
        }

        if (closeTransport && _sessions.TryGetValue(sessionKey, out ISessionContext? session))
        {
            await CloseSessionTransportSafelyAsync(sessionKey, session).ConfigureAwait(false);
        }
    }

    private async Task CloseSessionTransportSafelyAsync(
        SessionKey sessionKey,
        ISessionContext session
    )
    {
        try
        {
            await session.CloseSessionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close session transport for {SessionKey}", sessionKey);
        }
    }

    private async Task PublishDisconnectedEventSafelyAsync(PlayerId playerId, DateTime connectedAt)
    {
        DateTime disconnectedAt = DateTime.UtcNow;
        TimeSpan duration = disconnectedAt - connectedAt;

        try
        {
            await _events
                .PublishAsync(
                    new PlayerDisconnectedEvent(
                        playerId,
                        connectedAt,
                        disconnectedAt,
                        Math.Max(0, (long)duration.TotalSeconds)
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish player disconnected lifecycle event for {PlayerId}",
                playerId
            );
        }
    }

    private static bool TryRemovePair<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue value
    )
        where TKey : notnull =>
        ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(
            new KeyValuePair<TKey, TValue>(key, value)
        );
}
