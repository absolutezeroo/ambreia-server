using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Primitives.Permissions;

namespace Vortex.Permissions;

/// <summary>
/// Resolves a subject's effective <see cref="PermissionSet"/> from its account roles, caching the
/// result with a short TTL plus explicit invalidation. This is a read-mostly service (not a grain):
/// no per-subject serialization is needed, so a concurrent cache is the right fit. The database is
/// the source of truth; the cache is just the hot path.
/// </summary>
public sealed class PermissionService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ILogger<PermissionService> logger
) : IPermissionService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<PermissionService> _logger = logger;

    private readonly ConcurrentDictionary<int, CacheEntry> _byAccount = new();
    private readonly ConcurrentDictionary<int, int> _playerToAccount = new();

    public async Task<PermissionSet> ResolveForAccountAsync(
        int accountId,
        CancellationToken ct = default
    )
    {
        if (accountId <= 0)
        {
            return PermissionSet.Empty;
        }

        if (
            _byAccount.TryGetValue(accountId, out CacheEntry entry)
            && entry.ExpiresAtUtc > DateTime.UtcNow
        )
        {
            return entry.Set;
        }

        PermissionSet set = await LoadAccountAsync(accountId, ct).ConfigureAwait(false);
        _byAccount[accountId] = new CacheEntry(set, DateTime.UtcNow + CacheTtl);

        return set;
    }

    public async Task<PermissionSet> ResolveForPlayerAsync(
        int playerId,
        CancellationToken ct = default
    )
    {
        if (playerId <= 0)
        {
            return PermissionSet.Empty;
        }

        int accountId = await ResolveAccountIdAsync(playerId, ct).ConfigureAwait(false);

        return accountId <= 0
            ? PermissionSet.Empty
            : await ResolveForAccountAsync(accountId, ct).ConfigureAwait(false);
    }

    public void InvalidateAccount(int accountId) => _byAccount.TryRemove(accountId, out _);

    public void InvalidatePlayer(int playerId)
    {
        if (_playerToAccount.TryGetValue(playerId, out int accountId))
        {
            InvalidateAccount(accountId);
        }
    }

    private async Task<int> ResolveAccountIdAsync(int playerId, CancellationToken ct)
    {
        if (_playerToAccount.TryGetValue(playerId, out int cached))
        {
            return cached;
        }

        VortexDbContext db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            int? accountId = await db
                .Players.AsNoTracking()
                .Where(p => p.Id == playerId)
                .Select(p => p.PlayerAccountEntityId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (accountId is > 0)
            {
                _playerToAccount[playerId] = accountId.Value;
            }

            return accountId ?? 0;
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<PermissionSet> LoadAccountAsync(int accountId, CancellationToken ct)
    {
        VortexDbContext db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        try
        {
            List<int> roleIds = await db
                .PlayerAccountRoles.AsNoTracking()
                .Where(ar => ar.PlayerAccountEntityId == accountId)
                .Select(ar => ar.RoleEntityId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (roleIds.Count == 0)
            {
                return PermissionSet.Empty;
            }

            List<string> roles = await db
                .Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Key)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            List<string> capabilities = await db
                .RolePermissions.AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleEntityId))
                .Select(rp => rp.CapabilityKey)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return new PermissionSet(roles, capabilities);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resolve permissions for account {AccountId}",
                accountId
            );
            return PermissionSet.Empty;
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private readonly record struct CacheEntry(PermissionSet Set, DateTime ExpiresAtUtc);
}
