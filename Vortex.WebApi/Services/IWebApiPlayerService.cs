using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

public interface IWebApiPlayerService
{
    Task<List<AvatarInfo>> GetAvatarsForAccountAsync(int accountId, CancellationToken ct);

    Task<(bool Success, int PlayerId, string? Error)> CreateAvatarAsync(
        int accountId,
        string name,
        string figure,
        string gender,
        CancellationToken ct
    );

    Task<bool> NameAvailableAsync(string name, CancellationToken ct);

    Task<bool> SetNameAsync(int playerId, string name, CancellationToken ct);
}
