using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// Single authorization choke point. Resolves a subject's
/// effective <see cref="PermissionSet"/> (account roles -&gt; capabilities), with caching and explicit
/// invalidation when roles change. Callers ask the returned set whether it holds a capability rather
/// than testing ranks.
/// </summary>
public interface IPermissionService
{
    /// <summary>Effective permissions for an account.</summary>
    Task<PermissionSet> ResolveForAccountAsync(int accountId, CancellationToken ct = default);

    /// <summary>Effective permissions for a player's owning account (the in-game subject).</summary>
    Task<PermissionSet> ResolveForPlayerAsync(int playerId, CancellationToken ct = default);

    /// <summary>Drop the cached permissions for an account (call after a role change).</summary>
    void InvalidateAccount(int accountId);

    /// <summary>Drop the cached permissions for a player's owning account.</summary>
    void InvalidatePlayer(int playerId);
}
