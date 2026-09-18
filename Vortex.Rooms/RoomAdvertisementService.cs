using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives.Events;
using Vortex.Primitives.Rooms;

namespace Vortex.Rooms;

internal sealed class RoomAdvertisementService(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    IEventPublisher events
) : IRoomAdvertisementService
{
    private readonly IDbContextFactory<VortexDbContext> _dbContextFactory = dbContextFactory;
    private readonly IEventPublisher _events = events;

    public async Task<RoomAdvertisementSnapshot?> EditAsync(
        int advertisementId,
        int actorPlayerId,
        string name,
        string? description,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        RoomAdvertisementEntity? entity = await dbCtx
            .RoomAdvertisements.Include(a => a.RoomEntity)
                .ThenInclude(r => r!.PlayerEntity)
            .FirstOrDefaultAsync(
                a => a.Id == advertisementId && a.ExpiresAt > now && a.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (entity is null || entity.RoomEntity?.PlayerEntityId != actorPlayerId)
        {
            return null;
        }

        entity.Name = name.Length > 100 ? name[..100] : name;
        entity.Description =
            description is not null && description.Length > 255 ? description[..255] : description;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        await _events
            .PublishAsync(
                new RoomAdvertisementChangedEvent(
                    actorPlayerId,
                    advertisementId,
                    entity.RoomEntityId,
                    "edited"
                ),
                ct
            )
            .ConfigureAwait(false);

        return ToSnapshot(entity);
    }

    public async Task<RoomAdvertisementSnapshot?> GetActiveAdvertisementAsync(
        int roomId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        // One row per purchase, so a long-lived room has a pile of them. The live one is the
        // non-expired row that runs out last -- the same rule HasActiveAdvertisementAsync tests.
        RoomAdvertisementEntity? entity = await dbCtx
            .RoomAdvertisements.Include(a => a.RoomEntity)
                .ThenInclude(r => r!.PlayerEntity)
            .Where(a => a.RoomEntityId == roomId && a.ExpiresAt > now && a.DeletedAt == null)
            .OrderByDescending(a => a.ExpiresAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToSnapshot(entity);
    }

    /// <summary>
    /// The advertisement as the rest of the server sees it. The owner travels with it because the
    /// client shows an advertisement next to whoever put it up, and reads both the id and the name.
    /// </summary>
    private static RoomAdvertisementSnapshot ToSnapshot(RoomAdvertisementEntity entity) =>
        new()
        {
            AdvertisementId = entity.Id,
            RoomId = entity.RoomEntityId,
            Name = entity.Name,
            Description = entity.Description,
            CategoryId = entity.CategoryId,
            ExpiresAt = entity.ExpiresAt,
            CreatedAt = entity.CreatedAt,
            OwnerPlayerId = entity.RoomEntity?.PlayerEntityId ?? 0,
            OwnerName = entity.RoomEntity?.PlayerEntity?.Name ?? string.Empty,
        };

    public async Task<RoomId?> CancelAsync(
        int advertisementId,
        int actorPlayerId,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        RoomAdvertisementEntity? entity = await dbCtx
            .RoomAdvertisements.Include(a => a.RoomEntity)
            .FirstOrDefaultAsync(
                a => a.Id == advertisementId && a.ExpiresAt > now && a.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);

        if (entity is null || entity.RoomEntity?.PlayerEntityId != actorPlayerId)
        {
            return null;
        }

        entity.ExpiresAt = now;

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);

        await _events
            .PublishAsync(
                new RoomAdvertisementChangedEvent(
                    actorPlayerId,
                    advertisementId,
                    entity.RoomEntityId,
                    "cancelled"
                ),
                ct
            )
            .ConfigureAwait(false);

        return entity.RoomEntityId;
    }

    public async Task<bool> HasActiveAdvertisementAsync(int roomId, CancellationToken ct = default)
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        DateTime now = DateTime.UtcNow;

        return await dbCtx
            .RoomAdvertisements.AnyAsync(
                a => a.RoomEntityId == roomId && a.ExpiresAt > now && a.DeletedAt == null,
                ct
            )
            .ConfigureAwait(false);
    }

    public async Task CreateAsync(
        int roomId,
        string name,
        string? description,
        int categoryId,
        bool extended,
        DateTime expiresAt,
        CancellationToken ct = default
    )
    {
        await using VortexDbContext dbCtx = await _dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        dbCtx.RoomAdvertisements.Add(
            new RoomAdvertisementEntity
            {
                RoomEntityId = roomId,
                Name = name.Length > 100 ? name[..100] : name,
                Description =
                    description is not null && description.Length > 255
                        ? description[..255]
                        : description,
                CategoryId = categoryId,
                Extended = extended,
                ExpiresAt = expiresAt,
            }
        );

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
