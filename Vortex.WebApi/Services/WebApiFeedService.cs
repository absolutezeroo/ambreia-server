using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Primitives.Feed;

namespace Vortex.WebApi.Services;

/// <summary>
/// The feed's reads, out of <c>web_feed_posts</c> and the six tables that hang off it.
/// </summary>
/// <remarks>
/// <para>
/// The counters are correlated counts rather than columns. That is a deliberate ceiling: stored
/// counters would buy nothing at this hotel's size and cost three invalidation paths, one of which
/// is a soft delete that has to walk back over reactions. Revisit it if the feed grows past the
/// point where these measure badly — nothing above this class would change.
/// </para>
/// <para>
/// Photos are re-filtered here rather than trusted from <c>web_feed_post_photos</c>: the row says
/// which photo, and this says whether it is still public. A photo the owner unpublished after
/// posting it leaves the post.
/// </para>
/// </remarks>
public sealed class WebApiFeedService(IDbContextFactory<VortexDbContext> dbCtxFactory)
    : IWebApiFeedService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 50;

    /// <summary>What a card shows before "see all N comments" takes over — the screenshot's own count.</summary>
    private const int PreviewComments = 3;

    private readonly IDbContextFactory<VortexDbContext> _db = dbCtxFactory;

    public async Task<FeedPage> GetFeedAsync(
        int page,
        int pageSize,
        string? tag,
        int? viewerId,
        CancellationToken ct
    )
    {
        int wantedPage = page < 1 ? 1 : page;
        int wantedSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaximumPageSize => MaximumPageSize,
            _ => pageSize,
        };

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        IQueryable<WebFeedPostEntity> roots = Readable(db).Where(p => p.ParentEntityId == null);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            string wanted = Normalise(tag);

            roots = roots.Where(p => p.Tags.Any(t => t.WebFeedTagEntity!.Tag == wanted));
        }

        int total = await roots.CountAsync(ct).ConfigureAwait(false);

        List<WebFeedPostEntity> rows = await roots
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((wantedPage - 1) * wantedSize)
            .Take(wantedSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<FeedPost> items = [];

        foreach (WebFeedPostEntity row in rows)
        {
            items.Add(
                await ProjectAsync(db, row, viewerId, PreviewComments, ct).ConfigureAwait(false)
            );
        }

        return new FeedPage(wantedPage, wantedSize, total, items);
    }

    public async Task<FeedPost?> GetPostAsync(int id, int? viewerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebFeedPostEntity? row = await Readable(db)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        return row is null
            ? null
            : await ProjectAsync(db, row, viewerId, int.MaxValue, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The two conditions every read carries. Changing this changes who can be seen, so it exists
    /// once and every query starts from it.
    /// </summary>
    private static IQueryable<WebFeedPostEntity> Readable(VortexDbContext db) =>
        db.WebFeedPosts.Where(p => p.DeletedAt == null && p.PlayerEntity!.ProfileVisible);

    /// <summary>The search key a tag is stored and matched under.</summary>
    internal static string Normalise(string tag) => tag.Trim().TrimStart('#').ToLowerInvariant();

    private static async Task<FeedPost> ProjectAsync(
        VortexDbContext db,
        WebFeedPostEntity row,
        int? viewerId,
        int commentLimit,
        CancellationToken ct
    )
    {
        // Only a root has comments: the thread is one level deep, so a comment's own children are
        // an empty list rather than a query nobody answers.
        bool root = row.ParentEntityId is null;

        int commentCount = root
            ? await Readable(db)
                .CountAsync(p => p.ParentEntityId == row.Id, ct)
                .ConfigureAwait(false)
            : 0;

        List<FeedPost> comments = [];

        if (root && commentCount > 0 && commentLimit > 0)
        {
            // Newest last, the way a conversation reads, but the PREVIEW has to be the most recent
            // few — so the take is off the end and the order is restored afterwards.
            List<WebFeedPostEntity> rows = await Readable(db)
                .Where(p => p.ParentEntityId == row.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(commentLimit == int.MaxValue ? commentCount : commentLimit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            rows.Reverse();

            foreach (WebFeedPostEntity child in rows)
            {
                comments.Add(await ProjectAsync(db, child, viewerId, 0, ct).ConfigureAwait(false));
            }
        }

        List<PhotoSummary> photos =
            row.Kind == FeedPostKind.Photo
                ? await ProjectPhotosAsync(db, row.Id, ct).ConfigureAwait(false)
                : [];

        FeedPoll? poll =
            row.Kind == FeedPostKind.Poll
                ? await ProjectPollAsync(db, row.Id, viewerId, ct).ConfigureAwait(false)
                : null;

        List<string> tags = await db
            .WebFeedPostTags.Where(t => t.WebFeedPostEntityId == row.Id)
            .Select(t => t.WebFeedTagEntity!.Tag)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int reposts = await db
            .WebFeedReactions.CountAsync(
                r => r.WebFeedPostEntityId == row.Id && r.Kind == FeedReactionKind.Repost,
                ct
            )
            .ConfigureAwait(false);

        int favorites = await db
            .WebFeedReactions.CountAsync(
                r => r.WebFeedPostEntityId == row.Id && r.Kind == FeedReactionKind.Favorite,
                ct
            )
            .ConfigureAwait(false);

        bool repostedByMe =
            viewerId is not null
            && await db
                .WebFeedReactions.AnyAsync(
                    r =>
                        r.WebFeedPostEntityId == row.Id
                        && r.PlayerEntityId == viewerId
                        && r.Kind == FeedReactionKind.Repost,
                    ct
                )
                .ConfigureAwait(false);

        bool favoritedByMe =
            viewerId is not null
            && await db
                .WebFeedReactions.AnyAsync(
                    r =>
                        r.WebFeedPostEntityId == row.Id
                        && r.PlayerEntityId == viewerId
                        && r.Kind == FeedReactionKind.Favorite,
                    ct
                )
                .ConfigureAwait(false);

        await db.Entry(row).Reference(p => p.PlayerEntity).LoadAsync(ct).ConfigureAwait(false);

        if (row.GroupEntityId is not null)
        {
            await db.Entry(row).Reference(p => p.GroupEntity).LoadAsync(ct).ConfigureAwait(false);
        }

        return new FeedPost(
            row.Id,
            // No `UniqueId` on PlayerEntity — the id itself is the only stable public handle this
            // schema has for a player, the same convention WebApiProfileService.BuildUserAsync()
            // and WebApiPhotoService.Project() already use for the identical field.
            row.PlayerEntityId.ToString(CultureInfo.InvariantCulture),
            row.PlayerEntity!.Name,
            row.PlayerEntity.Figure,
            row.GroupEntityId,
            row.GroupEntity?.Name,
            row.GroupEntity?.Badge,
            row.Kind,
            row.Body,
            row.CreatedAt,
            photos,
            poll,
            tags,
            commentCount,
            reposts,
            favorites,
            repostedByMe,
            favoritedByMe,
            viewerId is not null && row.PlayerEntityId == viewerId,
            comments
        );
    }

    /// <summary>
    /// Only the scalars travel through the EF query; <see cref="PhotoSummary"/> is built afterwards
    /// in memory. Constructing it directly inside the <c>Select</c> — as <c>PhotoSummary</c>'s own
    /// <c>Likers</c> field needs an empty <c>List&lt;string&gt;</c> literal — is not something every
    /// provider's query translator is guaranteed to accept; projecting plain columns first sidesteps
    /// the question entirely. The record's shape is the requirement, not where it gets built.
    /// </summary>
    private static async Task<List<PhotoSummary>> ProjectPhotosAsync(
        VortexDbContext db,
        int postId,
        CancellationToken ct
    )
    {
        var rows = await db
            .WebFeedPostPhotos.Where(link =>
                link.WebFeedPostEntityId == postId
                && link.CameraPhotoEntity!.PublishedAt != null
                && link.CameraPhotoEntity.DeletedAt == null
                && link.CameraPhotoEntity.PlayerEntity!.ProfileVisible
            )
            .OrderBy(link => link.Ordinal)
            .Select(link => new
            {
                link.CameraPhotoEntity!.PhotoKey,
                OwnerId = link.CameraPhotoEntity.PlayerEntityId,
                OwnerName = link.CameraPhotoEntity.PlayerEntity!.Name,
                OwnerFigure = link.CameraPhotoEntity.PlayerEntity.Figure,
                link.CameraPhotoEntity.RoomEntityId,
                TakenAt = link.CameraPhotoEntity.CreatedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(r => new PhotoSummary(
                r.PhotoKey,
                r.OwnerId.ToString(CultureInfo.InvariantCulture),
                r.OwnerName,
                r.OwnerFigure,
                r.RoomEntityId,
                // Not joined: the feed card has no use for a room name, unlike the gallery this
                // record was designed for.
                null,
                r.TakenAt,
                0,
                false,
                new List<string>()
            ))
            .ToList();
    }

    private static async Task<FeedPoll> ProjectPollAsync(
        VortexDbContext db,
        int postId,
        int? viewerId,
        CancellationToken ct
    )
    {
        List<FeedPollChoice> choices = await db
            .WebFeedPollChoices.Where(c => c.WebFeedPostEntityId == postId)
            .OrderBy(c => c.Ordinal)
            .Select(c => new FeedPollChoice(
                c.Id,
                c.Label,
                db.WebFeedPollVotes.Count(v => v.WebFeedPollChoiceEntityId == c.Id)
            ))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int total = await db
            .WebFeedPollVotes.CountAsync(v => v.WebFeedPostEntityId == postId, ct)
            .ConfigureAwait(false);

        int? mine = viewerId is null
            ? null
            : await db
                .WebFeedPollVotes.Where(v =>
                    v.WebFeedPostEntityId == postId && v.PlayerEntityId == viewerId
                )
                .Select(v => (int?)v.WebFeedPollChoiceEntityId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

        return new FeedPoll(total, mine, choices);
    }
}
