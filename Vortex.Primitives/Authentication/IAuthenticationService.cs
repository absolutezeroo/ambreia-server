using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Authentication;

public interface IAuthenticationService
{
    /// <summary>
    /// The player or the account a sign-in ticket names (spec F3a §5.1); <see cref="TicketOwner.None"/>
    /// when it is unknown or expired. Both kinds follow the same TTL, sliding expiry and single-use
    /// rules (<c>AuthenticationConfig</c>).
    /// </summary>
    public Task<TicketOwner> ResolveTicketAsync(
        string ticket,
        string? remoteIp = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// A new sign-in ticket for a player, replacing their last one; null when there is no such
    /// player. Issued here, next to the rules that check it.
    /// </summary>
    public Task<string?> IssuePlayerTicketAsync(
        int playerId,
        string? remoteIp,
        CancellationToken ct = default
    );

    /// <summary>A new account ticket (the character screen, spec F3a §5.1), replacing the last one.</summary>
    public Task<string> IssueAccountTicketAsync(
        int accountId,
        string? remoteIp,
        CancellationToken ct = default
    );

    /// <summary>
    /// A player signed in through the character screen: the login a player ticket records when it is
    /// resolved (the audit's <c>auth.login.success</c>), for an account ticket that became a player.
    /// </summary>
    public Task RecordLoginAsync(int playerId, string? remoteIp, CancellationToken ct = default);
}
