using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

/// <summary>
/// habbo.com's forgotten-password flow: <c>/public/forgotPassword/send</c> mails a link, and
/// <c>/public/forgotPassword/changePassword</c> takes the token it carries and a new password.
/// </summary>
public interface IWebApiPasswordResetService
{
    /// <summary>
    /// Mails a reset link to <paramref name="email"/> when an account signs in with it. False only
    /// when this hotel cannot send mail at all; an unknown address is <b>true</b>, exactly like a
    /// known one, so the answer never says which addresses have accounts.
    /// </summary>
    Task<bool> RequestAsync(string email, CancellationToken ct);

    Task<PasswordResetOutcome> ResetAsync(string token, string password, CancellationToken ct);
}

public enum PasswordResetOutcome
{
    Changed,

    /// <summary>Malformed, expired, or already used — the password it was signed with changed.</summary>
    InvalidToken,

    TooShort,
}

/// <summary>Sends the one mail this hotel sends. An interface only so tests can read the link.</summary>
public interface IPasswordResetMailer
{
    Task SendAsync(string to, string link, CancellationToken ct);
}
