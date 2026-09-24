using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Primitives.Events;
using Vortex.Primitives.Feed;
using Vortex.Primitives.Moderation.WordFilter;

namespace Vortex.WebApi.Services;

/// <summary>
/// Albums, out of <c>web_albums</c> and <c>web_album_photos</c>, with a card in
/// <c>web_feed_posts</c> each.
/// </summary>
/// <remarks>
/// Photos are re-filtered on every read exactly as the feed's are: the link row says which photo,
/// and the read says whether it is still published. A photo its owner takes back leaves the album.
/// </remarks>
public sealed class WebApiAlbumService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IEventPublisher events,
    IWordFilter wordFilter
) : IWebApiAlbumService
{
    /// <summary>
    /// The most an album holds. A constant rather than live config: nothing has asked to tune it,
    /// and the page draws the whole album at once.
    /// </summary>
    public const int MaximumPhotos = 100;

    /// <summary>What a card shows — the feed's own photo post shows at most four as well.</summary>
    private const int CardPhotos = 4;

    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;

    public async Task<Album?> GetAsync(int id, int? viewerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebAlbumEntity? album = await db
            .WebAlbums.AsNoTracking()
            .Include(a => a.PlayerEntity)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);

        if (album is null)
        {
            return null;
        }

        Dictionary<int, List<PhotoSummary>> photos = await LoadPhotosAsync(db, [album.Id], ct)
            .ConfigureAwait(false);

        return new Album(
            album.Id,
            album.FeedPostEntityId,
            album.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            album.PlayerEntity!.Name,
            album.PlayerEntity.Figure,
            album.Title,
            album.Description,
            album.CreatedAt,
            viewerId is not null && album.PlayerEntityId == viewerId,
            photos.GetValueOrDefault(album.Id) ?? []
        );
    }

    public async Task<IReadOnlyList<FeedAlbum>> ListAsync(int ownerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<WebAlbumEntity> albums = await db
            .WebAlbums.AsNoTracking()
            .Where(a => a.PlayerEntityId == ownerId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<int, List<PhotoSummary>> photos = await LoadPhotosAsync(
                db,
                [.. albums.Select(a => a.Id)],
                ct
            )
            .ConfigureAwait(false);

        return [.. albums.Select(a => Card(a, photos.GetValueOrDefault(a.Id) ?? []))];
    }

    public async Task<(AlbumRefusal Refusal, Album? Album)> CreateAsync(
        int playerId,
        AlbumCreateRequest body,
        CancellationToken ct
    )
    {
        (AlbumRefusal refusal, string title, string description) = Texts(
            playerId,
            body.Title,
            body.Description
        );

        if (refusal != AlbumRefusal.None)
        {
            return (refusal, null);
        }

        string[] keys = Keys(body.PhotoKeys);

        if (keys.Length == 0)
        {
            return (AlbumRefusal.Empty, null);
        }

        if (keys.Length > MaximumPhotos)
        {
            return (AlbumRefusal.Full, null);
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<int>? photoIds = await ResolveAsync(db, playerId, keys, ct).ConfigureAwait(false);

        if (photoIds is null)
        {
            return (AlbumRefusal.PhotoNotYours, null);
        }

        // The card first: the album row points at it. Its body stays empty — the card reads the
        // album, so a rename never leaves a stale title behind in a second place.
        WebFeedPostEntity card = new()
        {
            PlayerEntityId = playerId,
            Kind = FeedPostKind.Album,
            Body = string.Empty,
        };

        db.WebFeedPosts.Add(card);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        WebAlbumEntity album = new()
        {
            PlayerEntityId = playerId,
            Title = title,
            Description = description,
            FeedPostEntityId = card.Id,
        };

        db.WebAlbums.Add(album);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Link(db, album.Id, photoIds, 0);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await events.PublishAsync(new FeedChangedEvent(card.Id), ct).ConfigureAwait(false);

        return (AlbumRefusal.None, await GetAsync(album.Id, playerId, ct).ConfigureAwait(false));
    }

    public async Task<(AlbumRefusal Refusal, Album? Album)> EditAsync(
        int playerId,
        int albumId,
        AlbumEditRequest body,
        CancellationToken ct
    )
    {
        (AlbumRefusal refusal, string title, string description) = Texts(
            playerId,
            body.Title,
            body.Description
        );

        if (refusal != AlbumRefusal.None)
        {
            return (refusal, null);
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebAlbumEntity? album = await OwnedAsync(db, playerId, albumId, ct).ConfigureAwait(false);

        if (album is null)
        {
            return (AlbumRefusal.NotFound, null);
        }

        album.Title = title;
        album.Description = description;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Announced so open feeds redraw the card's title, but not bumped: a rename is not news.
        await events
            .PublishAsync(new FeedChangedEvent(album.FeedPostEntityId), ct)
            .ConfigureAwait(false);

        return (AlbumRefusal.None, await GetAsync(album.Id, playerId, ct).ConfigureAwait(false));
    }

    public async Task<(AlbumRefusal Refusal, Album? Album)> AddPhotosAsync(
        int playerId,
        int albumId,
        AlbumPhotosRequest body,
        CancellationToken ct
    )
    {
        string[] keys = Keys(body.PhotoKeys);

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebAlbumEntity? album = await OwnedAsync(db, playerId, albumId, ct).ConfigureAwait(false);

        if (album is null)
        {
            return (AlbumRefusal.NotFound, null);
        }

        List<int>? photoIds = await ResolveAsync(db, playerId, keys, ct).ConfigureAwait(false);

        if (photoIds is null)
        {
            return (AlbumRefusal.PhotoNotYours, null);
        }

        List<WebAlbumPhotoEntity> links = await db
            .WebAlbumPhotos.Where(l => l.WebAlbumEntityId == album.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        HashSet<int> present = [.. links.Select(l => l.CameraPhotoEntityId)];
        List<int> added = [.. photoIds.Where(id => !present.Contains(id))];

        // Counted on the links, a photo since unpublished included: it can come back.
        if (links.Count + added.Count > MaximumPhotos)
        {
            return (AlbumRefusal.Full, null);
        }

        if (added.Count > 0)
        {
            Link(db, album.Id, added, links.Count == 0 ? 0 : links.Max(l => l.Ordinal) + 1);

            WebFeedPostEntity? card = await db
                .WebFeedPosts.FirstOrDefaultAsync(p => p.Id == album.FeedPostEntityId, ct)
                .ConfigureAwait(false);

            // The card comes back to the top of the feed. Only when something was actually added,
            // so a retried request does not bump it twice.
            card?.BumpedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await events
                .PublishAsync(new FeedChangedEvent(album.FeedPostEntityId), ct)
                .ConfigureAwait(false);
        }

        return (AlbumRefusal.None, await GetAsync(album.Id, playerId, ct).ConfigureAwait(false));
    }

    public async Task<(AlbumRefusal Refusal, Album? Album)> RemovePhotoAsync(
        int playerId,
        int albumId,
        string photoKey,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebAlbumEntity? album = await OwnedAsync(db, playerId, albumId, ct).ConfigureAwait(false);

        if (album is null)
        {
            return (AlbumRefusal.NotFound, null);
        }

        WebAlbumPhotoEntity? link = await db
            .WebAlbumPhotos.FirstOrDefaultAsync(
                l => l.WebAlbumEntityId == album.Id && l.CameraPhotoEntity!.PhotoKey == photoKey,
                ct
            )
            .ConfigureAwait(false);

        if (link is not null)
        {
            // Removed outright, not soft-deleted: a soft delete keeps the pair, and the unique
            // index would then refuse putting the photo back.
            db.WebAlbumPhotos.Remove(link);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await events
                .PublishAsync(new FeedChangedEvent(album.FeedPostEntityId), ct)
                .ConfigureAwait(false);
        }

        return (AlbumRefusal.None, await GetAsync(album.Id, playerId, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The album behind each of these cards, as the feed draws it. Keyed by the card's post id.
    /// </summary>
    internal static async Task<Dictionary<int, FeedAlbum>> LoadCardsAsync(
        VortexDbContext db,
        int[] postIds,
        CancellationToken ct
    )
    {
        List<WebAlbumEntity> albums = await db
            .WebAlbums.AsNoTracking()
            .Where(a => postIds.Contains(a.FeedPostEntityId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (albums.Count == 0)
        {
            return [];
        }

        Dictionary<int, List<PhotoSummary>> photos = await LoadPhotosAsync(
                db,
                [.. albums.Select(a => a.Id)],
                ct
            )
            .ConfigureAwait(false);

        return albums.ToDictionary(
            a => a.FeedPostEntityId,
            a => Card(a, photos.GetValueOrDefault(a.Id) ?? [])
        );
    }

    /// <summary>
    /// Soft-deletes the album whose card this is, if it is one. Tracked, not saved: the caller
    /// saves it with the card, so the two go together or not at all.
    /// </summary>
    internal static async Task DeleteWithCardAsync(
        VortexDbContext db,
        int postId,
        DateTime now,
        CancellationToken ct
    )
    {
        List<WebAlbumEntity> albums = await db
            .WebAlbums.Where(a => a.FeedPostEntityId == postId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (WebAlbumEntity album in albums)
        {
            album.DeletedAt = now;
        }
    }

    private static FeedAlbum Card(WebAlbumEntity album, List<PhotoSummary> photos) =>
        new(album.Id, album.Title, album.Description, photos.Count, [.. photos.Take(CardPhotos)]);

    /// <summary>
    /// Title and description, trimmed, bounded and through the word filter — bounded again after
    /// it, because a replacement can be longer than the word it replaced.
    /// </summary>
    private (AlbumRefusal Refusal, string Title, string Description) Texts(
        int playerId,
        string? title,
        string? description
    )
    {
        string wantedTitle = (title ?? string.Empty).Trim();
        string wantedDescription = (description ?? string.Empty).Trim();

        if (wantedTitle.Length == 0 || wantedTitle.Length > WebAlbumEntity.TitleLength)
        {
            return (AlbumRefusal.TitleInvalid, string.Empty, string.Empty);
        }

        if (wantedDescription.Length > WebAlbumEntity.DescriptionLength)
        {
            return (AlbumRefusal.DescriptionTooLong, string.Empty, string.Empty);
        }

        WordFilterResult filteredTitle = wordFilter.Apply(
            wantedTitle,
            WordFilterSource.Album,
            playerId
        );
        WordFilterResult filteredDescription = wordFilter.Apply(
            wantedDescription,
            WordFilterSource.Album,
            playerId
        );

        if (filteredTitle.Refused || filteredDescription.Refused)
        {
            return (AlbumRefusal.WordFiltered, string.Empty, string.Empty);
        }

        if (filteredTitle.Text.Length > WebAlbumEntity.TitleLength)
        {
            return (AlbumRefusal.TitleInvalid, string.Empty, string.Empty);
        }

        if (filteredDescription.Text.Length > WebAlbumEntity.DescriptionLength)
        {
            return (AlbumRefusal.DescriptionTooLong, string.Empty, string.Empty);
        }

        return (AlbumRefusal.None, filteredTitle.Text, filteredDescription.Text);
    }

    private static string[] Keys(IReadOnlyList<string>? keys) =>
        [
            .. (keys ?? [])
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.Ordinal),
        ];

    /// <summary>
    /// Each key to its photo row, in the order given — or null when one is not the caller's own
    /// published photo. The same check a photo post makes, and a refusal rather than a skip.
    /// </summary>
    private static async Task<List<int>?> ResolveAsync(
        VortexDbContext db,
        int playerId,
        string[] keys,
        CancellationToken ct
    )
    {
        Dictionary<string, int> found = await db
            .CameraPhotos.Where(p =>
                keys.Contains(p.PhotoKey)
                && p.PlayerEntityId == playerId
                && p.PublishedAt != null
                && p.DeletedAt == null
            )
            .ToDictionaryAsync(p => p.PhotoKey, p => p.Id, ct)
            .ConfigureAwait(false);

        return found.Count == keys.Length ? [.. keys.Select(k => found[k])] : null;
    }

    private static async Task<WebAlbumEntity?> OwnedAsync(
        VortexDbContext db,
        int playerId,
        int albumId,
        CancellationToken ct
    ) =>
        await db
            .WebAlbums.FirstOrDefaultAsync(a => a.Id == albumId && a.PlayerEntityId == playerId, ct)
            .ConfigureAwait(false);

    private static void Link(VortexDbContext db, int albumId, List<int> photoIds, int firstOrdinal)
    {
        for (int i = 0; i < photoIds.Count; i++)
        {
            db.WebAlbumPhotos.Add(
                new WebAlbumPhotoEntity
                {
                    WebAlbumEntityId = albumId,
                    CameraPhotoEntityId = photoIds[i],
                    Ordinal = firstOrdinal + i,
                }
            );
        }
    }

    /// <summary>
    /// Every still-published photo of these albums, in the order they were added. The same
    /// conditions and the same shape as the feed's own photo read.
    /// </summary>
    /// <remarks>
    /// ponytail: a card needs four and this reads them all — at most 100 per album, 20 cards a page.
    /// Take per album in SQL if that ever measures badly.
    /// </remarks>
    private static async Task<Dictionary<int, List<PhotoSummary>>> LoadPhotosAsync(
        VortexDbContext db,
        int[] albumIds,
        CancellationToken ct
    )
    {
        if (albumIds.Length == 0)
        {
            return [];
        }

        var rows = await db
            .WebAlbumPhotos.Where(link =>
                albumIds.Contains(link.WebAlbumEntityId)
                && link.CameraPhotoEntity!.PublishedAt != null
                && link.CameraPhotoEntity.DeletedAt == null
            )
            .OrderBy(link => link.Ordinal)
            .Select(link => new
            {
                link.WebAlbumEntityId,
                link.CameraPhotoEntity!.PhotoKey,
                OwnerId = link.CameraPhotoEntity.PlayerEntityId,
                OwnerName = link.CameraPhotoEntity.PlayerEntity!.Name,
                OwnerFigure = link.CameraPhotoEntity.PlayerEntity.Figure,
                link.CameraPhotoEntity.RoomEntityId,
                TakenAt = link.CameraPhotoEntity.PublishedAt!.Value,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.GroupBy(r => r.WebAlbumEntityId)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.Select(r => new PhotoSummary(
                            r.PhotoKey,
                            r.OwnerId.ToString(CultureInfo.InvariantCulture),
                            r.OwnerName,
                            r.OwnerFigure,
                            r.RoomEntityId,
                            null,
                            r.TakenAt,
                            0,
                            false,
                            new List<string>()
                        ))
                        .ToList()
            );
    }
}
