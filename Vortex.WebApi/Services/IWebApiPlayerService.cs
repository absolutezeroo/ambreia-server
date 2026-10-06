using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

public sealed record AvatarInfo(
    string UniqueId,
    string Name,
    string Motto,
    string FigureString,
    string Gender
);

/// <summary>
/// What the site's purse shows. The day count is what is LEFT of the Habbo Club subscription, not
/// how long it has run.
/// </summary>
public sealed record PlayerPurse(int Credits, int Diamonds, int Duckets, int HabboClubDays);

public interface IWebApiPlayerService
{
    Task<List<AvatarInfo>> GetAvatarsForAccountAsync(int accountId, CancellationToken ct);

    /// <summary>The wallet of one avatar, or <c>null</c> when no such player exists.</summary>
    Task<PlayerPurse?> GetPurseAsync(int playerId, CancellationToken ct);

    /// <summary>
    /// Whether this avatar's web profile is public, or <c>null</c> when no such player exists.
    /// </summary>
    Task<bool?> GetProfileVisibleAsync(int playerId, CancellationToken ct);

    /// <summary>Publishes or hides one avatar's web profile. False when there is no such player.</summary>
    Task<bool> SetProfileVisibleAsync(int playerId, bool visible, CancellationToken ct);

    Task<(bool Success, int PlayerId, string? Error)> CreateAvatarAsync(
        int accountId,
        string name,
        string figure,
        string gender,
        CancellationToken ct
    );

    Task<bool> NameAvailableAsync(string name, CancellationToken ct);

    Task<bool> SetNameAsync(int playerId, string name, CancellationToken ct);

    Task<bool> SaveFigureAsync(
        int playerId,
        string figureString,
        string gender,
        CancellationToken ct
    );

    Task<AvatarInfo?> GetAvatarAsync(int playerId, CancellationToken ct);
}
