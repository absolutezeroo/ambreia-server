using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Observers;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Networking;

public interface ISessionGateway
{
    public ISessionContext? GetSession(SessionKey key);
    public ISessionContextObserver? GetSessionObserver(SessionKey key);
    public PlayerId GetPlayerId(SessionKey key);
    public int GetActiveSessionCount();

    /// <summary>
    /// Whether one player is connected.
    /// </summary>
    /// <remarks>
    /// One map lookup, where copying <see cref="GetOnlinePlayerIds"/> and scanning it would allocate
    /// in proportion to the population, per lookup, for a boolean.
    /// </remarks>
    public bool IsOnline(PlayerId playerId);

    /// <summary>
    /// How many distinct players are connected.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="GetActiveSessionCount"/>, which counts sockets: a player with two
    /// windows open is one player and two sessions. Separate from
    /// <see cref="GetOnlinePlayerIds"/> because the connection gauge only wants the number, and it is
    /// read on every metrics scrape: allocating the whole list to call <c>.Count</c> on it was waste.
    /// </remarks>
    public int GetOnlinePlayerCount();

    /// <summary>
    /// Every connected player. Allocates a snapshot, so prefer <see cref="IsOnline"/> or
    /// <see cref="GetOnlinePlayerCount"/> when one of those answers the question.
    /// </summary>
    public IReadOnlyCollection<PlayerId> GetOnlinePlayerIds();

    /// <summary>
    /// Registers a freshly connected transport session. Returns <c>false</c> when the connection is
    /// refused &#8212; the caller must then close it &#8212; which today means a per-IP or global
    /// session cap was reached (SEC-10). A refused session is never registered, so nothing needs
    /// unwinding.
    /// </summary>
    public Task<bool> AddSessionAsync(SessionKey key, ISessionContext ctx);
    public Task RemoveSessionAsync(SessionKey key, CancellationToken ct);
    public Task AddSessionToPlayerAsync(
        SessionKey key,
        PlayerId playerId,
        CancellationToken ct = default
    );
    public Task RemoveSessionFromPlayerAsync(PlayerId playerId, CancellationToken ct);
}
