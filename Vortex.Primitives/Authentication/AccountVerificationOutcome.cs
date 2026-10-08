namespace Vortex.Primitives.Authentication;

/// <summary>Why a verification did or did not succeed.</summary>
public enum AccountVerificationOutcome
{
    /// <summary>No such account, or the wrong password. Never says which.</summary>
    InvalidCredentials,

    /// <summary>The credentials were checked and are right.</summary>
    Verified,
}
