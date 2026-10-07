using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Orleans;

namespace Vortex.WebApi.Authentication;

/// <summary>
/// Writes <c>player_accounts.safety_locked</c>, then tells every avatar of the account that is
/// currently connected.
/// </summary>
/// <remarks>
/// The account row has exactly one owner — this class — and the player grains keep a copy in state
/// because every spending handler reads it on every purchase. That copy is refreshed here rather
/// than re-read: a grain that learned the new value only at its next activation would keep selling
/// to a thief for as long as the session lasts.
/// </remarks>
public sealed class AccountSafetyLockService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IAccountAuthenticator authenticator,
    IGrainFactory grainFactory,
    ILogger<AccountSafetyLockService> logger
) : IAccountSafetyLockService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IAccountAuthenticator _authenticator = authenticator;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ILogger<AccountSafetyLockService> _logger = logger;

    public async Task<bool?> IsLockedAsync(int accountId, CancellationToken ct = default)
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .PlayerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => (bool?)a.SafetyLocked)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The server throwing the lock itself, on a sign-in from a place the account has never
    /// answered from. No credentials, because there are none to give: nobody has proved anything
    /// yet, which is precisely why the lock is going on.
    /// </summary>
    public Task ArmAsync(int accountId, CancellationToken ct = default) =>
        MoveAsync(accountId, true, ct);

    /// <summary>The challenge was answered, or the questions were removed with the password.</summary>
    public Task ReleaseAsync(int accountId, CancellationToken ct = default) =>
        MoveAsync(accountId, false, ct);

    public async Task<SafetyLockResult> SetAsync(
        int accountId,
        bool locked,
        string currentPassword,
        string? code,
        CancellationToken ct = default
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
            return SafetyLockResult.Failed(SafetyLockOutcome.UnknownAccount);
        }

        // Verified in BOTH directions. Locking is checked too because a thief who could throw the
        // lock without the password would have a way to grief the owner out of their own hotel.
        AccountVerification verification = await _authenticator
            .VerifyCredentialsAsync(account.Email, currentPassword, code, ct)
            .ConfigureAwait(false);

        switch (verification.Outcome)
        {
            case AccountVerificationOutcome.MfaRequired:
                return SafetyLockResult.Failed(SafetyLockOutcome.MfaRequired);
            case AccountVerificationOutcome.InvalidCode:
                return SafetyLockResult.Failed(SafetyLockOutcome.InvalidCode);
            case AccountVerificationOutcome.InvalidCredentials:
                return SafetyLockResult.Failed(SafetyLockOutcome.WrongPassword);
        }

        await MoveAsync(accountId, locked, ct).ConfigureAwait(false);

        return SafetyLockResult.Success();
    }

    /// <summary>
    /// The write itself, with no gate of its own. Every public entry point above decides for itself
    /// what has to be proved before reaching this, which is why it is private: the credential check
    /// and the write must not be separable by a future caller.
    /// </summary>
    private async Task MoveAsync(int accountId, bool locked, CancellationToken ct)
    {
        await using VortexDbContext db = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerAccountEntity? account = await db
            .PlayerAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            .ConfigureAwait(false);

        if (account is null || account.SafetyLocked == locked)
        {
            return;
        }

        account.SafetyLocked = locked;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Account {AccountId} safety lock is now {State}",
            accountId,
            locked ? "on" : "off"
        );

        // Every avatar, not just the connected one: which of them holds the socket is not known
        // here, and a grain that is not activated answers this without waking anything of its own.
        List<int> avatars = await db
            .Players.AsNoTracking()
            .Where(p => p.PlayerAccountEntityId == accountId && p.DeletedAt == null)
            .Select(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (int playerId in avatars)
        {
            await _grainFactory
                .GetPlayerGrain(playerId)
                .OnAccountSafetyLockChangedAsync(locked, ct)
                .ConfigureAwait(false);
        }
    }
}
