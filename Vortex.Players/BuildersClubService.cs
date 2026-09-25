using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;

namespace Vortex.Players;

/// <summary>
/// Reads the Builders Club (SubscriptionType.BuildersClub) subscription row directly rather than
/// through PlayerGrain state -- unlike Habbo Club, there's no gift-token/kickback/badge bookkeeping
/// to carry across grain activations, so a live per-call read is simpler and just as correct.
/// </summary>
internal sealed class BuildersClubService(IDbContextFactory<VortexDbContext> dbContextFactory)
    : IBuildersClubService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;

    public async Task<BuildersClubSubscriptionSnapshot> GetSubscriptionAsync(
        int playerId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        PlayerSubscriptionEntity? sub = await dbCtx
            .PlayerSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(
                s =>
                    s.PlayerEntityId == playerId
                    && s.SubscriptionType == SubscriptionType.BuildersClub,
                ct
            )
            .ConfigureAwait(false);

        bool isActive = sub is not null && sub.ExpiresAt > DateTime.UtcNow;

        if (!isActive)
        {
            return new BuildersClubSubscriptionSnapshot(false, null, 0);
        }

        int furniLimit = await dbCtx
            .BuildersClubTiers.AsNoTracking()
            .Where(t => t.Level == sub!.Level && t.DeletedAt == null)
            .Select(t => t.FurniLimit)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new BuildersClubSubscriptionSnapshot(true, sub!.ExpiresAt, furniLimit);
    }

    public async Task<int> GetOwnedFurnitureCountAsync(int playerId, CancellationToken ct = default)
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await dbCtx
            .Furnitures.AsNoTracking()
            .CountAsync(f => f.PlayerEntityId == playerId && f.DeletedAt == null, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetMembershipDaysAsync(int playerId, CancellationToken ct = default)
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        var sub = await dbCtx
            .PlayerSubscriptions.AsNoTracking()
            .Where(s =>
                s.PlayerEntityId == playerId && s.SubscriptionType == SubscriptionType.BuildersClub
            )
            .Select(s => new { Start = s.FirstSubscribedAt ?? s.CreatedAt, s.ExpiresAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (sub is null)
        {
            return 0;
        }

        // ponytail: one span from the first subscription to now (or expiry). Nothing renews a
        // Builders Club row yet, so there is no lapse to subtract; track past days once one does.
        DateTime end = sub.ExpiresAt < DateTime.UtcNow ? sub.ExpiresAt : DateTime.UtcNow;

        return end <= sub.Start ? 0 : (int)Math.Ceiling((end - sub.Start).TotalDays);
    }

    public async Task<bool> IsRoomSealedAsync(int roomId, CancellationToken ct = default)
    {
        if (roomId <= 0)
        {
            return false;
        }

        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Asked in the cheap order: most rooms hold no Builders Club furniture at all, and that
        // question is one indexed EXISTS, where the subscription answer needs the room's owner
        // first.
        bool hasBuildersClubFurniture = await dbCtx
            .Furnitures.AsNoTracking()
            .AnyAsync(f => f.RoomEntityId == roomId && f.DeletedAt == null && f.IsBuildersClub, ct)
            .ConfigureAwait(false);

        if (!hasBuildersClubFurniture)
        {
            return false;
        }

        int ownerId = await dbCtx
            .Rooms.AsNoTracking()
            .Where(r => r.Id == roomId && r.DeletedAt == null)
            .Select(r => r.PlayerEntityId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (ownerId <= 0)
        {
            return false;
        }

        BuildersClubSubscriptionSnapshot subscription = await GetSubscriptionAsync(ownerId, ct)
            .ConfigureAwait(false);

        return !subscription.IsActive;
    }
}
