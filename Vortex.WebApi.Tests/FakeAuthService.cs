using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;
using Vortex.Primitives.Observability;
using Vortex.WebApi.Services;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Tests;

/// <summary>
/// In-memory auth service for the integration tests. It creates real sessions in the shared
/// <see cref="WebApiSessionStore"/> so the authenticated endpoints resolve an account id exactly as
/// they would in production, without touching the database.
/// </summary>
internal sealed class FakeAuthService(WebApiSessionStore sessions) : IWebApiAuthService
{
    public const string ValidEmail = "user@test.com";
    public const string ValidPassword = "correct-horse";
    public const int AccountId = 1;

    private readonly ConcurrentDictionary<string, byte> _registered = new() { [ValidEmail] = 0 };

    private int _nextAccountId = AccountId + 1;

    public Task<(bool Success, string? SessionId, int AccountId, string? Error)> LoginAsync(
        string email,
        string password,
        CancellationToken ct
    )
    {
        if (email != ValidEmail || password != ValidPassword)
        {
            return Task.FromResult<(bool, string?, int, string?)>(
                (false, null, 0, "pocket.auth.login_failed")
            );
        }

        string sessionId = sessions.CreateSession(AccountId);

        return Task.FromResult<(bool, string?, int, string?)>((true, sessionId, AccountId, null));
    }

    public Task<(bool Success, int AccountId, string? Error)> RegisterAsync(
        string email,
        string password,
        CancellationToken ct
    )
    {
        if (!_registered.TryAdd(email, 0))
        {
            return Task.FromResult<(bool, int, string?)>(
                (false, 0, "pocket.auth.valid_email_required")
            );
        }

        int accountId = Interlocked.Increment(ref _nextAccountId);

        return Task.FromResult<(bool, int, string?)>((true, accountId, null));
    }

    public Task<(bool Success, string? Ticket, string? Error)> GetSsoTokenAsync(
        int playerId,
        string ip,
        CancellationToken ct
    )
    {
        if (playerId <= 0)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, "pocket.auth.login_failed")
            );
        }

        return Task.FromResult<(bool, string?, string?)>((true, $"ticket-{playerId}", null));
    }

    public Task<string> GetAccountSsoTokenAsync(int accountId, string ip, CancellationToken ct) =>
        Task.FromResult($"account-ticket-{accountId}");
}
