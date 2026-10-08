using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Authentication;

/// <summary>
/// Verifies account credentials (email + password). Lives in <c>Vortex.Primitives</c> so any module
/// can authenticate accounts without referencing the auth runtime or a password-hashing library.
/// One entry point, so a rule added to the check reaches every login.
/// </summary>
public interface IAccountAuthenticator
{
    /// <summary>
    /// Verifies the credentials. Implementations must be constant-time with respect to whether the
    /// account exists.
    /// </summary>
    Task<AccountVerification> VerifyCredentialsAsync(
        string email,
        string password,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates an account: the password rules and the hash live next to the check that reads them,
    /// and the address is normalised exactly as the login normalises it.
    /// </summary>
    Task<AccountCreation> CreateAccountAsync(
        string email,
        string password,
        CancellationToken ct = default
    );
}

/// <summary>Why a verification did or did not succeed.</summary>
public enum AccountVerificationOutcome
{
    /// <summary>No such account, or the wrong password. Never says which.</summary>
    InvalidCredentials,

    /// <summary>The credentials were checked and are right.</summary>
    Verified,
}

/// <summary>
/// The outcome, and the account id when there is one. <see cref="AccountId" /> is only meaningful for
/// <see cref="AccountVerificationOutcome.Verified" />, so a caller cannot half-authenticate someone by
/// reading an id off a failed attempt.
/// </summary>
public readonly record struct AccountVerification(AccountVerificationOutcome Outcome, int AccountId)
{
    public static AccountVerification InvalidCredentials { get; } =
        new(AccountVerificationOutcome.InvalidCredentials, 0);

    public static AccountVerification Verified(int accountId) =>
        new(AccountVerificationOutcome.Verified, accountId);

    public bool IsVerified => Outcome == AccountVerificationOutcome.Verified;
}
