namespace Vortex.Primitives.Authentication;

/// <summary>The outcome, and the new account's id when it was created.</summary>
public readonly record struct AccountCreation(AccountCreationOutcome Outcome, int AccountId)
{
    public static AccountCreation Refused(AccountCreationOutcome outcome) => new(outcome, 0);

    public static AccountCreation Created(int accountId) =>
        new(AccountCreationOutcome.Created, accountId);

    public bool IsCreated => Outcome == AccountCreationOutcome.Created;
}
