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
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players;

namespace Vortex.Networking.Session;

/// <summary>
/// Every live socket, and who it belongs to. Its parts: admitting and removing a socket (here), the
/// account phase (<c>SessionGateway.Account.cs</c>), the player phase (<c>.Player.cs</c>), and
/// closing and the lifecycle events (<c>.Farewell.cs</c>).
/// </summary>
public sealed partial class SessionGateway(
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

    // The account phase (spec F3a §5.2): a socket on the character screen, before it is a player.
    // Written under _mappingGate like the player pair.
    private readonly ConcurrentDictionary<SessionKey, int> _sessionToAccount = new();
    private readonly ConcurrentDictionary<int, SessionKey> _accountToSession = new();
    private long _accountGeneration;

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
    ///     disconnects. Held across the teardown of a replaced or closed session (unregister,
    ///     disconnect event, farewell), because the replacement's <c>BeginSessionAsync</c> has to
    ///     come after its predecessor's unregister; only the new observer's registration and the
    ///     connected event run after release.
    ///     <para>
    ///     ponytail: one global gate, so a slow presence or room grain stalls every login and
    ///     disconnect behind it until the call times out. Per-player gates if that ever shows.
    ///     </para>
    /// </summary>
    private readonly SemaphoreSlim _mappingGate = new(1, 1);

    private sealed record ObserverEntry(SessionContextObserver Impl, ISessionContextObserver Ref);

    public ISessionContext? GetSession(SessionKey key) =>
        _sessions.TryGetValue(key, out ISessionContext? ctx) ? ctx : null;

    public ISessionContextObserver? GetSessionObserver(SessionKey key) =>
        _sessionObservers.TryGetValue(key, out ObserverEntry? observer) ? observer.Ref : null;

    public PlayerId GetPlayerId(SessionKey key) =>
        _sessionToPlayer.TryGetValue(key, out PlayerId playerId) ? playerId : -1;

    public int GetAccountId(SessionKey key) =>
        _sessionToAccount.TryGetValue(key, out int accountId) ? accountId : 0;

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
        // First, and outside the gate: a login still attaching this socket checks for it (under
        // the gate, and again after registering) and stands down instead of mapping a closed
        // session to the player.
        _sessions.TryRemove(key, out _);

        // The mapping is read under the gate, never before it: a login holding the gate may be
        // about to write it, and a read taken outside would miss it and leave the player mapped to
        // a socket that is gone.
        await _mappingGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_sessionToPlayer.TryGetValue(key, out PlayerId playerId) && playerId > 0)
            {
                await RemovePlayerSessionCoreAsync(playerId, key, closeTransport: false, ct)
                    .ConfigureAwait(false);
            }

            await LeaveAccountPhaseAsync(key, ct).ConfigureAwait(false);
        }
        finally
        {
            _mappingGate.Release();
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

        // Keyed on what the session was ADMITTED under, not on the context, which by now may be
        // torn down and reporting no address at all.
        if (_ipAddressBySession.TryRemove(key, out string? admittedIpAddress))
        {
            ReleaseIpSlot(admittedIpAddress);
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
