using System;
using Microsoft.Extensions.Options;
using Vortex.WebApi.Configuration;

namespace Vortex.WebApi.Session;

/// <summary>
/// Authenticated web sessions, keyed by the cookie id, remembering which avatar the visitor picked.
/// Cleared on server restart.
///
/// <para>
/// The mechanics are <see cref="AccountSessionStore{TState}" />. They used to be a second
/// implementation here, and it had drifted: a GUID rather than 256 cryptographic bits, a day
/// hard-coded rather than configured, expired entries dropped only if someone happened to ask, and
/// a selected-avatar read that answered for sessions that had already expired.
/// </para>
/// </summary>
public sealed class WebApiSessionStore
{
    private readonly AccountSessionStore<WebSessionState> _sessions;

    public WebApiSessionStore(IOptions<WebApiConfig> options)
    {
        int hours = Math.Max(1, options.Value.SessionLifetimeHours);
        _sessions = new AccountSessionStore<WebSessionState>(TimeSpan.FromHours(hours));
    }

    public int LifetimeSeconds => _sessions.LifetimeSeconds;

    public string CreateSession(int accountId) =>
        _sessions.Create(accountId, new WebSessionState(null));

    public int? GetAccountId(string? sessionId) => _sessions.Resolve(sessionId)?.AccountId;

    public void RemoveSession(string sessionId) => _sessions.Remove(sessionId);

    public void SetSelectedPlayer(string? sessionId, int playerId) =>
        _sessions.TryUpdate(sessionId, state => state with { SelectedPlayer = playerId });

    public int? GetSelectedPlayer(string? sessionId) =>
        _sessions.Resolve(sessionId)?.State.SelectedPlayer;
}
