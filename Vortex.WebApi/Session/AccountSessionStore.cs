using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace Vortex.WebApi.Session;

/// <summary>
/// In-memory table of authenticated sessions, keyed by an opaque token of 256 cryptographic bits.
/// The web API's session store sits on top of it.
///
/// <para>
/// What a session carries besides an account id is <typeparamref name="TState" />, and how long it
/// lasts is the caller's to choose. Sessions do not survive a restart, which is deliberate --
/// nothing persistent means no token to steal from a table.
/// </para>
/// </summary>
/// <typeparam name="TState">
/// What the session remembers besides the account: the selected avatar, for the web API.
/// </typeparam>
public sealed class AccountSessionStore<TState>
{
    /// <summary>Live sessions past which <see cref="Create" /> sweeps the expired ones first.</summary>
    private const int PRUNE_THRESHOLD = 64;

    private readonly TimeSpan _lifetime;
    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public AccountSessionStore(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "A session lifetime must be positive; a zero one expires every session on creation."
            );
        }

        _lifetime = lifetime;
    }

    public TimeSpan Lifetime => _lifetime;

    public int LifetimeSeconds => (int)_lifetime.TotalSeconds;

    /// <summary>
    /// Mints a session token: 256 bits from the cryptographic generator, hex-encoded. The token is
    /// the whole credential, so it is the one thing here that must not be guessable -- a GUID is
    /// random but says so by accident rather than by contract.
    /// </summary>
    public string Create(int accountId, TState state)
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTime now = DateTime.UtcNow;

        lock (_gate)
        {
            // Entries are otherwise only dropped when re-presented, so a visitor who never comes
            // back leaves one behind until restart. Login is the only place the table grows, so it
            // is also the only place worth sweeping -- cheaper than a timer.
            if (_sessions.Count >= PRUNE_THRESHOLD)
            {
                PruneExpired(now);
            }

            _sessions[sessionId] = new Entry(accountId, state, now.Add(_lifetime));
        }

        return sessionId;
    }

    /// <summary>The account and state behind a token, or null when it is unknown or expired.</summary>
    public (int AccountId, TState State)? Resolve(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out Entry entry))
            {
                return null;
            }

            if (entry.ExpiresAt <= DateTime.UtcNow)
            {
                _sessions.Remove(sessionId);
                return null;
            }

            return (entry.AccountId, entry.State);
        }
    }

    /// <summary>
    /// Replaces the state of a live session, leaving its expiry alone. False when the token is
    /// unknown or expired -- an expired session must not be revived by writing to it.
    /// </summary>
    public bool TryUpdate(string? sessionId, Func<TState, TState> update)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out Entry entry))
            {
                return false;
            }

            if (entry.ExpiresAt <= DateTime.UtcNow)
            {
                _sessions.Remove(sessionId);
                return false;
            }

            _sessions[sessionId] = entry with { State = update(entry.State) };
            return true;
        }
    }

    public void Remove(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        lock (_gate)
        {
            _sessions.Remove(sessionId);
        }
    }

    /// <summary>
    /// Drops every session of an account and reports how many. This is what revoking a credential
    /// needs: revoking it is only half of it while the sessions it already opened keep answering.
    /// Nothing calls it today.
    /// </summary>
    public int RemoveAllForAccount(int accountId)
    {
        lock (_gate)
        {
            List<string> doomed = [];

            foreach (KeyValuePair<string, Entry> pair in _sessions)
            {
                if (pair.Value.AccountId == accountId)
                {
                    doomed.Add(pair.Key);
                }
            }

            foreach (string sessionId in doomed)
            {
                _sessions.Remove(sessionId);
            }

            return doomed.Count;
        }
    }

    /// <summary>Live session count.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Count;
            }
        }
    }

    private void PruneExpired(DateTime now)
    {
        List<string> expired = [];

        foreach (KeyValuePair<string, Entry> pair in _sessions)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (string sessionId in expired)
        {
            _sessions.Remove(sessionId);
        }
    }

    private readonly record struct Entry(int AccountId, TState State, DateTime ExpiresAt);
}
