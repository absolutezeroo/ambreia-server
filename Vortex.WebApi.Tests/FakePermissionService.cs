using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Permissions;

namespace Vortex.WebApi.Tests;

/// <summary>Every account holds exactly the capabilities the test gave the factory.</summary>
internal sealed class FakePermissionService(string[] capabilities) : IPermissionService
{
    public Task<PermissionSet> ResolveForAccountAsync(
        int accountId,
        CancellationToken ct = default
    ) => Task.FromResult(new PermissionSet([], capabilities));

    public Task<PermissionSet> ResolveForPlayerAsync(
        int playerId,
        CancellationToken ct = default
    ) => Task.FromResult(new PermissionSet([], capabilities));

    public void InvalidateAccount(int accountId) { }

    public void InvalidatePlayer(int playerId) { }
}
