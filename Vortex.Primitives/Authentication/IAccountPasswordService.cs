using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Authentication;

/// <summary>
/// Changing an account's password. Until this existed there was no way to change one at all --
/// registration wrote a hash and nothing ever wrote another, so an operator who suspected their
/// credentials were compromised had a choice between an UPDATE by hand and doing nothing.
///
/// <para>
/// A change re-authenticates first, through <see cref="IAccountAuthenticator" /> and therefore
/// through the second factor as well: a hijacked session must not be able to take the account with
/// it. Every session of the account is then revoked, the caller's own included -- being signed out
/// everywhere is the point of changing a password, not a side effect of it.
/// </para>
/// </summary>
public interface IAccountPasswordService
{
    /// <summary>
    /// Changes the password of <paramref name="accountId" /> after re-checking
    /// <paramref name="currentPassword" /> (and <paramref name="code" />, when the account has a
    /// second factor). On success every session of the account is revoked.
    /// </summary>
    Task<PasswordChangeResult> ChangeAsync(
        int accountId,
        string currentPassword,
        string newPassword,
        string? code,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sets a password without knowing the old one, for a staff administrator acting on somebody
    /// who cannot sign in. Revokes the account's sessions too. The caller is responsible for having
    /// checked the capability and for auditing the act -- this only performs it.
    /// </summary>
    Task<PasswordChangeResult> ResetAsync(
        int accountId,
        string newPassword,
        CancellationToken ct = default
    );
}

/// <summary>Why a password change did or did not happen.</summary>
public enum PasswordChangeOutcome
{
    /// <summary>No such account.</summary>
    UnknownAccount,

    /// <summary>The current password did not verify.</summary>
    WrongPassword,

    /// <summary>The account has a second factor and no code was supplied.</summary>
    MfaRequired,

    /// <summary>A code was supplied and it did not verify.</summary>
    InvalidCode,

    /// <summary>The new password is shorter than the floor a new one has to clear.</summary>
    TooShort,

    /// <summary>Changed, and the account's sessions were revoked.</summary>
    Changed,
}

/// <summary>
/// The outcome, and how many sessions were revoked by it. The count is what the caller reports back:
/// "signed out of 3 places" is the only visible proof the revocation reached beyond the browser the
/// operator is looking at.
/// </summary>
public readonly record struct PasswordChangeResult(
    PasswordChangeOutcome Outcome,
    int SessionsRevoked
)
{
    /// <summary>
    /// Shortest password a *new* one may be. Deliberately applied here and not to registration:
    /// tightening the door people already came through would lock out accounts that exist, which is
    /// a product decision rather than a fix.
    /// </summary>
    public const int MINIMUM_LENGTH = 8;

    public static PasswordChangeResult Failed(PasswordChangeOutcome outcome) => new(outcome, 0);

    public static PasswordChangeResult Changed(int sessionsRevoked) =>
        new(PasswordChangeOutcome.Changed, sessionsRevoked);

    public bool Succeeded => Outcome == PasswordChangeOutcome.Changed;
}

/// <summary>
/// One place that holds sessions for accounts. There are two -- the dashboard's and the web API's --
/// and a password change has to reach both: revoking a credential while the sessions it already
/// opened keep answering is not revoking anything. Implementations are registered as this interface
/// so whoever revokes does not have to know how many there are.
/// </summary>
public interface IAccountSessionRevoker
{
    /// <summary>
    /// The export key a PLUGIN publishes its revoker under.
    /// </summary>
    /// <remarks>
    /// A plugin cannot register itself in the host's container: the host would then hold one of the
    /// plugin's objects, which pins its <c>AssemblyLoadContext</c> and makes it unloadable for the
    /// life of the process. It publishes an export instead, and the host holds a stable adapter that
    /// reads through it. The constant lives here because the two sides are in assemblies that do not
    /// reference each other, and a key spelled twice is a key that eventually differs.
    /// </remarks>
    public const string ExportKey = "account.session.revoker";

    /// <summary>Which front door this holds sessions for, for the log line.</summary>
    string SessionKind { get; }

    /// <summary>Drops every session of the account and reports how many.</summary>
    int RemoveAllForAccount(int accountId);
}
