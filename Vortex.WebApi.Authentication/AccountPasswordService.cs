using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Events;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Writes <c>player_accounts.password_hash</c>, and only ever after the account has re-proved itself
/// or a staff administrator has taken responsibility for it.
///
/// <para>
/// Re-authentication goes through <see cref="IAccountAuthenticator" /> rather than a BCrypt call of
/// its own -- the same rule the architecture check holds -- which is also how the second factor
/// comes along for free: an account that has one cannot have its password changed by a session
/// alone.
/// </para>
/// </summary>
public sealed class AccountPasswordService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountAuthenticator authenticator,
    IEnumerable<IAccountSessionRevoker> revokers,
    IEventPublisher events,
    ILogger<AccountPasswordService> logger
) : IAccountPasswordService
{
    // Matches registration's factor: raising it here alone would make a changed password slower to
    // verify than the one it replaced, for no gain that is not also available by raising both.
    private const int WORK_FACTOR = 12;

    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IAccountAuthenticator _authenticator = authenticator;
    private readonly IAccountSessionRevoker[] _revokers = [.. revokers];
    private readonly IEventPublisher _events = events;
    private readonly ILogger<AccountPasswordService> _logger = logger;

    public async Task<PasswordChangeResult> ChangeAsync(
        int accountId,
        string currentPassword,
        string newPassword,
        string? code,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrEmpty(newPassword)
            || newPassword.Length < PasswordChangeResult.MINIMUM_LENGTH
        )
        {
            return PasswordChangeResult.Failed(PasswordChangeOutcome.TooShort);
        }

        string? email = await ReadEmailAsync(accountId, ct).ConfigureAwait(false);

        if (email is null)
        {
            return PasswordChangeResult.Failed(PasswordChangeOutcome.UnknownAccount);
        }

        AccountVerification verification = await _authenticator
            .VerifyCredentialsAsync(email, currentPassword, code, ct)
            .ConfigureAwait(false);

        switch (verification.Outcome)
        {
            case AccountVerificationOutcome.MfaRequired:
                return PasswordChangeResult.Failed(PasswordChangeOutcome.MfaRequired);
            case AccountVerificationOutcome.InvalidCode:
                return PasswordChangeResult.Failed(PasswordChangeOutcome.InvalidCode);
            case AccountVerificationOutcome.InvalidCredentials:
                return PasswordChangeResult.Failed(PasswordChangeOutcome.WrongPassword);
        }

        return await WriteAsync(accountId, newPassword, "changed", ct).ConfigureAwait(false);
    }

    public async Task<PasswordChangeResult> ResetAsync(
        int accountId,
        string newPassword,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrEmpty(newPassword)
            || newPassword.Length < PasswordChangeResult.MINIMUM_LENGTH
        )
        {
            return PasswordChangeResult.Failed(PasswordChangeOutcome.TooShort);
        }

        return await WriteAsync(accountId, newPassword, "reset", ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadEmailAsync(int accountId, CancellationToken ct)
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

    private async Task<PasswordChangeResult> WriteAsync(
        int accountId,
        string newPassword,
        string how,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null)
        {
            return PasswordChangeResult.Failed(PasswordChangeOutcome.UnknownAccount);
        }

        // BCrypt is CPU-bound by design; keep it off the caller's thread.
        account.PasswordHash = await Task.Run(
                () => BCrypt.Net.BCrypt.HashPassword(newPassword, WORK_FACTOR),
                ct
            )
            .ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Only after the write. Revoking first would sign everyone out of an account whose password
        // then failed to change.
        int revoked = 0;

        foreach (IAccountSessionRevoker revoker in _revokers)
        {
            int dropped = revoker.RemoveAllForAccount(accountId);
            revoked += dropped;

            if (dropped > 0)
            {
                _logger.LogInformation(
                    "Password {How} for account {AccountId}: revoked {Count} {Kind} session(s)",
                    how,
                    accountId,
                    dropped,
                    revoker.SessionKind
                );
            }
        }

        // The characters on the account, so the record is findable from any of their profiles --
        // read here rather than in the handler, which has no database of its own.
        ImmutableArray<int> playerIds =
        [
            .. await db
                .Players.AsNoTracking()
                .Where(p => p.PlayerAccountEntityId == accountId)
                .Select(p => p.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];

        await _events
            .PublishAsync(
                new AccountPasswordChangedEvent(
                    accountId,
                    playerIds,
                    StaffReset: how == "reset",
                    revoked
                ),
                ct
            )
            .ConfigureAwait(false);

        return PasswordChangeResult.Changed(revoked);
    }
}
