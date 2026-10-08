using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

public sealed record AvatarInfo(
    string UniqueId,
    string Name,
    string Motto,
    string FigureString,
    string Gender,
    // The character, when the avatar has one (spec Personnage §8); null otherwise.
    string? ClassId = null,
    int? Level = null
);

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
