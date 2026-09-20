using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Camera;

namespace Vortex.WebApi.Services;

/// <summary>
/// The website's photo reads, straight out of <c>camera_photos</c> and anonymous.
/// </summary>
/// <remarks>
/// <para>
/// Every query here carries the same three conditions and they are not interchangeable:
/// <c>PublishedAt != null</c> (the player chose to make it public), <c>DeletedAt == null</c> (they
/// have not taken it back), and the owner's <c>ProfileVisible</c> (they are not hiding). The last
/// one is the least obvious and the one worth keeping: a player who has hidden their profile has
/// said they do not want to be browsed, and a gallery that still lists their photos by name is the
/// same disclosure by another door.
/// </para>
/// <para>
/// The room is joined for its NAME only, and it is nullable on purpose — <c>RoomEntityId</c> is
/// cleared when the room goes, because a photo outlives the room it was shot in.
/// </para>
/// </remarks>
public sealed class WebApiPhotoService(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IWebApiPhotoService
{
    private const int DefaultPageSize = 24;
    private const int MaximumPageSize = 60;

    /// <summary>What a profile page shows before its "see them all" link takes over.</summary>
    private const int MaximumProfilePhotos = 32;

    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;

    public async Task<PhotoPage> GetPhotosAsync(int page, int pageSize, CancellationToken ct)
    {
        int wantedPage = page < 1 ? 1 : page;
        int wantedSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaximumPageSize => MaximumPageSize,
            _ => pageSize,
        };

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        IQueryable<CameraPhotoEntity> published = Published(db);

        int total = await published.CountAsync(ct).ConfigureAwait(false);

        List<PhotoSummary> rows = await Project(
                published
                    .OrderByDescending(p => p.PublishedAt)
                    .ThenByDescending(p => p.Id)
                    .Skip((wantedPage - 1) * wantedSize)
                    .Take(wantedSize)
            )
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PhotoPage(wantedPage, wantedSize, total, rows);
    }

    public async Task<IReadOnlyList<PhotoSummary>> GetPlayerPhotosAsync(
        int playerId,
        int limit,
        CancellationToken ct
    )
    {
        int wanted = limit switch
        {
            < 1 => MaximumProfilePhotos,
            > MaximumProfilePhotos => MaximumProfilePhotos,
            _ => limit,
        };

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await Project(
                Published(db)
                    .Where(p => p.PlayerEntityId == playerId)
                    .OrderByDescending(p => p.PublishedAt)
                    .ThenByDescending(p => p.Id)
                    .Take(wanted)
            )
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The only rows that may ever leave this service. Written once and used by both reads, because
    /// the day these three conditions differ between the gallery and a profile is the day one of
    /// them leaks a photo the other one hides.
    /// </summary>
    private static IQueryable<CameraPhotoEntity> Published(VortexDbContext db) =>
        db
            .CameraPhotos.AsNoTracking()
            .Where(p =>
                p.PublishedAt != null
                && p.DeletedAt == null
                && p.PlayerEntity != null
                && p.PlayerEntity.ProfileVisible
            );

    /// <summary>
    /// Projected rather than materialised: the entity carries the scene bytes and the moderation
    /// counters, none of which a card draws.
    /// </summary>
    private static IQueryable<PhotoSummary> Project(IQueryable<CameraPhotoEntity> photos) =>
        photos.Select(p => new PhotoSummary(
            p.PhotoKey,
            p.PlayerEntityId.ToString(),
            p.PlayerEntity!.Name,
            p.PlayerEntity.Figure,
            p.RoomEntityId,
            p.RoomEntity != null ? p.RoomEntity.Name : null,
            // `PublishedAt` rather than `CreatedAt`: the gallery is ordered by it, and a card that
            // said "taken three weeks ago" under a photo published this morning would be describing
            // a different event from the one the ordering used.
            p.PublishedAt!.Value
        ));
}
