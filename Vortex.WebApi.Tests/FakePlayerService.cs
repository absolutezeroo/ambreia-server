using System.Threading;
using System.Threading.Tasks;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Tests;

/// <summary>In-memory player service: every account owns player <see cref="OwnedPlayerId"/>.</summary>
internal sealed class FakePlayerService : IWebApiPlayerService
{
    public const int OwnedPlayerId = 100;

    public Task<int?> FirstPlayerIdAsync(int accountId, CancellationToken ct) =>
        Task.FromResult<int?>(OwnedPlayerId);
}
