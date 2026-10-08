using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

public interface IWebApiAuthService
{
    /// <summary>Authenticates an account and opens a web session.</summary>
    Task<(bool Success, string? SessionId, int AccountId, string? Error)> LoginAsync(
        string email,
        string password,
        CancellationToken ct
    );

    Task<(bool Success, int AccountId, string? Error)> RegisterAsync(
        string email,
        string password,
        CancellationToken ct
    );

    /// <summary>A ticket straight into the world as <paramref name="playerId"/>: the editor's test
    /// route, which skips the character screen.</summary>
    Task<(bool Success, string? Ticket, string? Error)> GetSsoTokenAsync(
        int playerId,
        string ip,
        CancellationToken ct
    );

    /// <summary>A ticket for the account, opening the game socket on the character screen (spec
    /// F3a §5.1); replaces the account's previous one.</summary>
    Task<string> GetAccountSsoTokenAsync(int accountId, string ip, CancellationToken ct);
}
