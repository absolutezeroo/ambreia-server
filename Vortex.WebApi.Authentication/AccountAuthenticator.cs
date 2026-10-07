using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Verifies account credentials against the <c>player_accounts</c> table using BCrypt, then the
/// second factor when the account has one. Password verification is CPU-bound, so it runs off the
/// calling thread. When the account is missing a dummy BCrypt verification is still performed so the
/// response time does not reveal account existence.
///
/// <para>
/// This is the only place in the emulator that checks a password, and the architecture-walls check
/// holds it there. The web API used to keep its own copy -- same table, same dummy hash -- which is
/// precisely why the second factor reached one login and not the other.
/// </para>
/// </summary>
public sealed class AccountAuthenticator(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountMfaService mfa
) : IAccountAuthenticator
{
    // A pre-computed BCrypt hash of a random value, used to keep timing constant when no account
    // matches the supplied email (prevents user-enumeration via response timing).
    private const string DummyHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEeO3qj8b1l1u8j0Y9o6m4w8h2tY6q0Q1Qe";

    public async Task<AccountVerification> VerifyCredentialsAsync(
        string email,
        string password,
        string? code,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return AccountVerification.InvalidCredentials;
        }

        string normalizedEmail = email.Trim().ToLowerInvariant();

        await using VortexDbContext dbCtx = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await dbCtx
            .PlayerAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == normalizedEmail, ct)
            .ConfigureAwait(false);

        string hash = account?.PasswordHash ?? DummyHash;

        bool valid = await Task.Run(() => BCrypt.Net.BCrypt.Verify(password, hash), ct)
            .ConfigureAwait(false);

        if (!valid || account is null)
        {
            return AccountVerification.InvalidCredentials;
        }

        // Read off the row already loaded rather than through IsEnabledAsync: one query, and the
        // factor state cannot change between the two checks.
        if (!string.IsNullOrWhiteSpace(account.TotpSecret))
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return AccountVerification.MfaRequired;
            }

            if (!await mfa.VerifyAsync(account.Id, code, ct).ConfigureAwait(false))
            {
                return AccountVerification.InvalidCode;
            }
        }

        return AccountVerification.Verified(account.Id);
    }
}
