using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Tests;

/// <summary>In-memory player service returning deterministic avatars for contract assertions.</summary>
internal sealed class FakePlayerService : IWebApiPlayerService
{
    public const string OwnedUniqueId = "100";
    public const string TakenName = "taken";

    public Task<List<AvatarInfo>> GetAvatarsForAccountAsync(int accountId, CancellationToken ct) =>
        Task.FromResult(
            new List<AvatarInfo> { new AvatarInfo(OwnedUniqueId, "Tester", "Hi", "hd-180-1", "M") }
        );

    public Task<(bool Success, int PlayerId, string? Error)> CreateAvatarAsync(
        int accountId,
        string name,
        string figure,
        string gender,
        CancellationToken ct
    )
    {
        Created++;

        return Task.FromResult<(bool, int, string?)>((true, 101, null));
    }

    /// <summary>How many avatars the routes created.</summary>
    public int Created { get; private set; }

    public Task<bool> NameAvailableAsync(string name, CancellationToken ct) =>
        Task.FromResult(name != TakenName);

    public Task<bool> SetNameAsync(int playerId, string name, CancellationToken ct) =>
        Task.FromResult(name != TakenName);
}
