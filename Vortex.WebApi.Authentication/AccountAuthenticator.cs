using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Verifies account credentials against the <c>player_accounts</c> table using BCrypt. Password
/// verification is CPU-bound, so it runs off the
/// calling thread. When the account is missing a dummy BCrypt verification is still performed so the
/// response time does not reveal account existence.
///
/// <para>
/// This is the only place in the emulator that checks or hashes a password, and the
/// architecture-walls check holds it there, so a rule added here reaches every login and every
/// registration.
/// </para>
/// </summary>
public sealed class AccountAuthenticator(IDbContextFactory<VortexDbContext> dbContextFactory)
    : IAccountAuthenticator
{
    // A pre-computed BCrypt hash of a random value, used to keep timing constant when no account
    // matches the supplied email (prevents user-enumeration via response timing).
    private const string DummyHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEeO3qj8b1l1u8j0Y9o6m4w8h2tY6q0Q1Qe";

    /// <summary>Where BCrypt stops reading. Anything past this byte is not part of the secret.</summary>
    private const int BCRYPT_MAX_PASSWORD_BYTES = 72;

    public async Task<AccountVerification> VerifyCredentialsAsync(
        string email,
        string password,
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

        return AccountVerification.Verified(account.Id);
    }

    public async Task<AccountCreation> CreateAccountAsync(
        string email,
        string password,
        CancellationToken ct = default
    )
    {
        if (password.Length < AccountPasswordRules.MINIMUM_LENGTH)
        {
            return AccountCreation.Refused(AccountCreationOutcome.PasswordTooShort);
        }

        // BCrypt hashes the first 72 bytes and silently ignores the rest, so a longer secret is not
        // the stronger one the user thinks they chose. Refused rather than truncated: quietly
        // storing something other than what was typed is worse than saying no.
        if (Encoding.UTF8.GetByteCount(password) > BCRYPT_MAX_PASSWORD_BYTES)
        {
            return AccountCreation.Refused(AccountCreationOutcome.PasswordTooLong);
        }

        // Trimmed as well as lowered, exactly as the login does: an address stored with a stray
        // space could never sign in again.
        string normalizedEmail = email.Trim().ToLowerInvariant();

        await using VortexDbContext dbCtx = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        if (
            await dbCtx
                .PlayerAccounts.AsNoTracking()
                .AnyAsync(a => a.Email == normalizedEmail, ct)
                .ConfigureAwait(false)
        )
        {
            return AccountCreation.Refused(AccountCreationOutcome.EmailTaken);
        }

        // Work factor 12 is strong enough for production; CPU-bound, so off the calling thread.
        string hash = await Task.Run(
                () => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                ct
            )
            .ConfigureAwait(false);

        PlayerAccountEntity account = new() { Email = normalizedEmail, PasswordHash = hash };

        dbCtx.PlayerAccounts.Add(account);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        return AccountCreation.Created(account.Id);
    }
}
