namespace Vortex.Primitives.Authentication;

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
