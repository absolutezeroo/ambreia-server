using System;
using Microsoft.Extensions.Options;
using Vortex.WebApi.Configuration;

namespace Vortex.WebApi.Session;

/// <summary>
/// Authenticated web sessions, keyed by the cookie id. Cleared on server restart.
///
/// <para>
/// The mechanics are <see cref="AccountSessionStore" />. They used to be a second implementation
/// here, and it had drifted: a GUID rather than 256 cryptographic bits, a day hard-coded rather than
/// configured, expired entries dropped only if someone happened to ask.
/// </para>
/// </summary>
public sealed class WebApiSessionStore
{
    private readonly AccountSessionStore _sessions;

    public WebApiSessionStore(IOptions<WebApiConfig> options)
    {
        int hours = Math.Max(1, options.Value.SessionLifetimeHours);
        _sessions = new AccountSessionStore(TimeSpan.FromHours(hours));
    }

    public int LifetimeSeconds => _sessions.LifetimeSeconds;

    public string CreateSession(int accountId) => _sessions.Create(accountId);

    public int? GetAccountId(string? sessionId) => _sessions.Resolve(sessionId);

    public void RemoveSession(string sessionId) => _sessions.Remove(sessionId);
}
