using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Writes <c>player_accounts.email</c>, and only after the account has re-proved itself.
/// </summary>
/// <remarks>
/// Verification goes through <see cref="IAccountAuthenticator"/> — the same rule the architecture
/// check holds for <see cref="AccountPasswordService"/> — so the second factor is enforced here
/// without this class knowing anything about it. That matters more for the address than for the
/// password: the address IS the login identifier, so moving it is how an account is taken over for
/// good.
/// </remarks>
public sealed class AccountEmailService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountAuthenticator authenticator,
    ILogger<AccountEmailService> logger
) : IAccountEmailService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IAccountAuthenticator _authenticator = authenticator;
    private readonly ILogger<AccountEmailService> _logger = logger;

    public async Task<string?> GetAsync(int accountId, CancellationToken ct = default)
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<EmailChangeResult> ChangeAsync(
        int accountId,
        string currentPassword,
        string newEmail,
        string? code,
        CancellationToken ct = default
    )
    {
        string candidate = (newEmail ?? string.Empty).Trim();

        if (!IsWellFormed(candidate))
        {
            return EmailChangeResult.Failed(EmailChangeOutcome.Invalid);
        }

        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return EmailChangeResult.Failed(EmailChangeOutcome.UnknownAccount);
        }

        // Verified against the address the account signs in with TODAY, not the one it is asking
        // for: the new address proves nothing yet.
        AccountVerification verification = await _authenticator
            .VerifyCredentialsAsync(account.Email, currentPassword, code, ct)
            .ConfigureAwait(false);

        switch (verification.Outcome)
        {
            case AccountVerificationOutcome.MfaRequired:
                return EmailChangeResult.Failed(EmailChangeOutcome.MfaRequired);
            case AccountVerificationOutcome.InvalidCode:
                return EmailChangeResult.Failed(EmailChangeOutcome.InvalidCode);
            case AccountVerificationOutcome.InvalidCredentials:
                return EmailChangeResult.Failed(EmailChangeOutcome.WrongPassword);
        }

        if (string.Equals(account.Email, candidate, StringComparison.OrdinalIgnoreCase))
        {
            // Already theirs. Answering "taken" would be true and useless.
            return EmailChangeResult.Success();
        }

        bool taken = await db
            .PlayerAccounts.AsNoTracking()
            .AnyAsync(a => a.Id != accountId && a.Email == candidate, ct)
            .ConfigureAwait(false);

        if (taken)
        {
            _logger.LogWarning(
                "Email change refused for account {AccountId}: address already in use",
                accountId
            );

            return EmailChangeResult.Failed(EmailChangeOutcome.Taken);
        }

        account.Email = candidate;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The address itself is never logged: it is the account's identifier and personal data, and
        // an operator reading a log does not need it to know the change happened.
        _logger.LogInformation("Account {AccountId} changed its sign-in address", accountId);

        return EmailChangeResult.Success();
    }

    /// <summary>
    /// Shape only — one <c>@</c>, something either side, no whitespace. Deliberately not a grammar:
    /// the addresses a strict RFC check rejects are mostly real, the ones it accepts are mostly
    /// undeliverable anyway, and the only test that settles it is sending a message. This hotel
    /// cannot send one, so it does not pretend to know more than this.
    /// </summary>
    private static bool IsWellFormed(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int at = email.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && email.IndexOf('.', at) > at + 1;
    }
}
