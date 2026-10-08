using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.Authentication;
using Vortex.WebApi.Session;

namespace Vortex.WebApi.Services;

/// <summary>
/// The web side of signing in: it opens web sessions and translates outcomes into the site's error
/// codes. Credentials are the authenticator's (<see cref="IAccountAuthenticator" />), tickets the
/// authentication service's (<see cref="IAuthenticationService" />): each rule has one home.
/// </summary>
public sealed class WebApiAuthService(
    IAccountAuthenticator authenticator,
    IAuthenticationService authentication,
    WebApiSessionStore sessions,
    ILogger<WebApiAuthService> logger
) : IWebApiAuthService
{
    private readonly IAccountAuthenticator _authenticator = authenticator;
    private readonly IAuthenticationService _authentication = authentication;
    private readonly WebApiSessionStore _sessions = sessions;
    private readonly ILogger<WebApiAuthService> _logger = logger;

    public async Task<(bool Success, string? SessionId, int AccountId, string? Error)> LoginAsync(
        string email,
        string password,
        CancellationToken ct
    )
    {
        AccountVerification verification = await _authenticator
            .VerifyCredentialsAsync(email, password, ct)
            .ConfigureAwait(false);

        if (!verification.IsVerified)
        {
            _logger.LogWarning("Login failed for {Email}", email.ToLowerInvariant());
            return (false, null, 0, "pocket.auth.login_failed");
        }

        string sessionId = _sessions.CreateSession(verification.AccountId);
        _logger.LogInformation(
            "Account {AccountId} authenticated ({Email})",
            verification.AccountId,
            email.ToLowerInvariant()
        );

        return (true, sessionId, verification.AccountId, null);
    }

    /// <summary>
    /// Creates an account. The caller opens the web session right after; the game ticket comes
    /// from <c>/api/ssotoken</c>.
    /// </summary>
    public async Task<(bool Success, int AccountId, string? Error)> RegisterAsync(
        string email,
        string password,
        CancellationToken ct
    )
    {
        AccountCreation creation = await _authenticator
            .CreateAccountAsync(email, password, ct)
            .ConfigureAwait(false);

        if (!creation.IsCreated)
        {
            _logger.LogWarning("Registration refused: {Outcome}", creation.Outcome);

            return (
                false,
                0,
                creation.Outcome switch
                {
                    AccountCreationOutcome.PasswordTooShort => "pocket.auth.password_too_short",
                    AccountCreationOutcome.PasswordTooLong => "pocket.auth.password_too_long",
                    _ => "pocket.auth.email_already_taken",
                }
            );
        }

        _logger.LogInformation("Account {AccountId} registered", creation.AccountId);

        return (true, creation.AccountId, null);
    }

    public async Task<(bool Success, string? Ticket, string? Error)> GetSsoTokenAsync(
        int playerId,
        string ip,
        CancellationToken ct
    )
    {
        string? ticket = await _authentication
            .IssuePlayerTicketAsync(playerId, ip, ct)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            _logger.LogWarning("SSO token requested for unknown player {PlayerId}", playerId);
            return (false, null, "pocket.auth.login_failed");
        }

        _logger.LogInformation("SSO token issued for player {PlayerId}", playerId);
        return (true, ticket, null);
    }

    public async Task<(bool Success, string? Ticket, string? Error)> GetAccountSsoTokenAsync(
        int accountId,
        string ip,
        CancellationToken ct
    )
    {
        string ticket = await _authentication
            .IssueAccountTicketAsync(accountId, ip, ct)
            .ConfigureAwait(false);

        _logger.LogInformation("Account SSO token issued for account {AccountId}", accountId);
        return (true, ticket, null);
    }
}
