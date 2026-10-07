using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Crypto;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// TOTP second factor stored on <c>player_accounts.totp_secret</c>. Codes are verified by
/// <see cref="TotpCodes" />; this type only decides what is stored, when, and on whose say-so.
/// </summary>
public sealed class AccountMfaService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IEventPublisher events
) : IAccountMfaService
{
    private const string ISSUER = "Vortex";

    public async Task<bool> IsEnabledAsync(int accountId, CancellationToken ct = default)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .PlayerAccounts.AsNoTracking()
            .AnyAsync(a => a.Id == accountId && a.TotpSecret != null, ct)
            .ConfigureAwait(false);
    }

    public async Task<MfaEnrolment> BeginEnrolmentAsync(
        int accountId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        string? email = await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        string secret = TotpCodes.GenerateSecret();

        return new MfaEnrolment(
            secret,
            TotpCodes.BuildUri(secret, ISSUER, email ?? $"account-{accountId}")
        );
    }

    public async Task<bool> ConfirmEnrolmentAsync(
        int accountId,
        string secret,
        string code,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(secret) || !TotpCodes.Verify(secret, code, DateTime.UtcNow))
        {
            return false;
        }

        // Enrolment, never replacement. The code proves an authenticator holds the *supplied*
        // secret -- which the caller generated -- so on its own it proves nothing about who is
        // asking. Without this, a stolen session could post a secret of its own and silently become
        // the account's second factor: the step-up gate on currency, staff roles and the console
        // would then be held by the attacker, and the real operator, whose login now demands a code
        // they cannot compute, would be locked out.
        //
        // Replacing a factor goes through DisableAsync first, which requires a code from the factor
        // already on the account. That is the proof this call cannot supply.
        if (await ReadSecretAsync(accountId, ct).ConfigureAwait(false) is not null)
        {
            return false;
        }

        return await WriteSecretAsync(accountId, secret, ct).ConfigureAwait(false);
    }

    public async Task<bool> VerifyAsync(int accountId, string? code, CancellationToken ct = default)
    {
        string? secret = await ReadSecretAsync(accountId, ct).ConfigureAwait(false);

        return secret is not null && TotpCodes.Verify(secret, code, DateTime.UtcNow);
    }

    public async Task<bool> DisableAsync(
        int accountId,
        string? code,
        CancellationToken ct = default
    )
    {
        string? secret = await ReadSecretAsync(accountId, ct).ConfigureAwait(false);

        if (secret is null)
        {
            // Already off. Reported as done rather than refused: the caller asked for a state, and
            // that is the state.
            return true;
        }

        // A null code is the administrator path (the caller has already been checked for the staff
        // capability); anything else has to prove it holds the factor it is switching off.
        if (code is not null && !TotpCodes.Verify(secret, code, DateTime.UtcNow))
        {
            return false;
        }

        return await WriteSecretAsync(accountId, null, ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadSecretAsync(int accountId, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.TotpSecret)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> WriteSecretAsync(int accountId, string? secret, CancellationToken ct)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return false;
        }

        account.TotpSecret = secret;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Same reason as the password write: the audit is searched by character, and an account
        // holds several. See AccountPasswordChangedEvent.
        ImmutableArray<int> playerIds =
        [
            .. await db
                .Players.AsNoTracking()
                .Where(p => p.PlayerAccountEntityId == accountId)
                .Select(p => p.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];

        await events
            .PublishAsync(
                new AccountMfaChangedEvent(accountId, playerIds, Enabled: secret is not null),
                ct
            )
            .ConfigureAwait(false);

        return true;
    }
}
