using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.World;

namespace Vortex.World;

/// <summary>The edit lock: one account at a time saves a zone, and the lock lapses after
/// <see cref="WorldConfig.LockMinutes"/> without a save.</summary>
public sealed partial class ZoneEditorService
{
    public async Task<bool> TryLockAsync(
        string key,
        int accountId,
        bool force,
        CancellationToken ct
    )
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity zone =
            await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Zone '{key}' does not exist.");
        int? holder = LockHolder(zone);

        if (holder is not null && holder != accountId && !force)
        {
            return false;
        }

        if (holder is not null && holder != accountId)
        {
            logger.LogWarning(
                "Zone {Key} lock forced by account {Account} (held by {Holder})",
                key,
                accountId,
                holder
            );
        }

        zone.LockedByAccountId = accountId;
        zone.LockedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    public async Task UnlockAsync(string key, int accountId, CancellationToken ct)
    {
        await using VortexDbContext ctx = await db.CreateDbContextAsync(ct).ConfigureAwait(false);

        ZoneEntity? zone = await FindTrackedAsync(ctx, key, ct).ConfigureAwait(false);

        if (zone is null || zone.LockedByAccountId != accountId)
        {
            return;
        }

        zone.LockedByAccountId = null;
        zone.LockedAt = null;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private int? LockHolder(ZoneEntity zone) =>
        zone.LockedByAccountId is int holder
        && zone.LockedAt is DateTime at
        && at.AddMinutes(_config.LockMinutes) > DateTime.UtcNow
            ? holder
            : null;
}
