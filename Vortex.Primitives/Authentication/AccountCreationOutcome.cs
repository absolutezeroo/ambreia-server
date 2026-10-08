namespace Vortex.Primitives.Authentication;

/// <summary>Why an account was or was not created.</summary>
public enum AccountCreationOutcome
{
    Created,

    /// <summary>Shorter than <see cref="AccountPasswordRules.MINIMUM_LENGTH" />.</summary>
    PasswordTooShort,

    /// <summary>Past what the hash reads: storing something other than what was typed is refused.</summary>
    PasswordTooLong,

    EmailTaken,
}
