using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Feed;
using Vortex.Database.Entities.Groups;
using Vortex.Primitives.Events;
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
/// <para>
/// Both entry points assemble their posts through <see cref="AssembleAsync"/>, which fetches every
/// supporting table (comments, photos, poll choices, poll votes, tags, reactions) ONCE for the whole
/// batch of posts being answered — never once per post, and never once per comment. A page is a
/// handful of round trips regardless of how many posts are on it or how many comments each one has;
/// the alternative (a post's own queries run inside a loop over the page, and again inside a loop
/// over each post's comments) turns a 20-post page into hundreds of sequential round trips against a
/// real database.
/// </para>
/// <para>
/// It announces on the event bus and listens for nothing. Four of its events are personal — a
/// mention, a comment, a repost, a like — and name whom they concern; the fifth,
/// <c>FeedChangedEvent</c>, is public and says only that a thread moved, which is what lets every
/// open page refresh the one card without polling. What turns any of them into something a reader
/// sees lives under <c>Notifications/</c>, and the feed is deliberately ignorant of it: the same
/// events are there for a plugin to hear.
/// </para>
/// </remarks>
public sealed class WebApiFeedService(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IEventPublisher events
) : IWebApiFeedService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 50;

    /// <summary>What a card shows before "see all N comments" takes over — the screenshot's own count.</summary>
    private const int PreviewComments = 3;

    private static readonly ReactionSummary EmptyReactions = new(0, 0, false, false);

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

        // The feed is not a list of posts: it is a list of ENTRIES, and a post enters it once as
        // its author's original and once more for every player who shares it. That is what makes
        // a repost a repost rather than a counter — before this, pressing share wrote a row that
        // moved a number and put the post in nobody's feed.
        //
        // Ordered by the moment of the ENTRY, so a share of an old post arrives at the top where
        // the sharer meant to put it.
        IQueryable<FeedEntry> originals = roots.Select(p => new FeedEntry
        {
            PostId = p.Id,
            ReposterId = null,
            At = p.CreatedAt,
        });

        IQueryable<FeedEntry> shares = db
            .WebFeedReactions.Where(r =>
                r.Kind == FeedReactionKind.Repost
                && r.DeletedAt == null
                // Roots only. A reaction may be left on a comment too, and a shared comment
                // surfacing as a top-level card would be a thread fragment with no thread.
                && roots.Select(p => p.Id).Contains(r.WebFeedPostEntityId)
            )
            .Select(r => new FeedEntry
            {
                PostId = r.WebFeedPostEntityId,
                ReposterId = r.PlayerEntityId,
                At = r.CreatedAt,
            });

        // Concat and not Union: two entries can legitimately be identical in every column the
        // projection carries — the same post shared by the same player twice cannot happen, the
        // unique index sees to it — but Union would also deduplicate, and paging over a set whose
        // size depends on deduplication is a page count that drifts.
        IQueryable<FeedEntry> entries = originals.Concat(shares);

        int total = await entries.CountAsync(ct).ConfigureAwait(false);

        // The tiebreak has to separate an original from a share OF THAT POST, which `PostId`
        // cannot: they carry the same one. `ReposterId` does, and it breaks the tie the right way
        // — null sorts last descending, so the share lands above the original it copies, which is
        // the order the two happened in.
        List<FeedEntry> shown = await entries
            .OrderByDescending(e => e.At)
            .ThenByDescending(e => e.ReposterId)
            .ThenByDescending(e => e.PostId)
            .Skip((wantedPage - 1) * wantedSize)
            .Take(wantedSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<FeedPost> items = await AssembleEntriesAsync(db, shown, viewerId, ct)
            .ConfigureAwait(false);

        return new FeedPage(wantedPage, wantedSize, total, items);
    }

    public async Task<FeedPost?> GetPostAsync(int id, int? viewerId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebFeedPostEntity? row = await Readable(db)
            .Include(p => p.PlayerEntity)
            .Include(p => p.GroupEntity)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // A single post still goes through AssembleAsync — with a batch of one — rather than a
        // second, simpler copy of the projection: a comment can carry a photo, a poll, tags and
        // reactions exactly like a post, and that assembly is exactly what AssembleAsync already
        // does. Duplicating it here is how the two would quietly drift.
        List<FeedPost> items = await AssembleAsync(db, [row], viewerId, int.MaxValue, ct)
            .ConfigureAwait(false);

        return items[0];
    }

    /// <summary>The screenshot's card shows two; four is the most a two-column grid draws well.</summary>
    private const int MaximumPhotos = 4;

    private const int MinimumChoices = 2;
    private const int MaximumChoices = 5;

    public async Task<(FeedRefusal Refusal, FeedPost? Post)> PublishAsync(
        int playerId,
        FeedPublishRequest body,
        CancellationToken ct
    )
    {
        string text = (body.Body ?? string.Empty).Trim();

        if (text.Length > WebFeedPostEntity.BodyLength)
        {
            return (FeedRefusal.BodyTooLong, null);
        }

        IReadOnlyList<string> keys = body.PhotoKeys ?? [];
        IReadOnlyList<string> choices = (body.PollChoices ?? [])
            .Select(c => (c ?? string.Empty).Trim())
            .Where(c => c.Length > 0)
            .ToList();

        // The kind has to agree with what came with it, in both directions: a Photo post with no
        // photos renders as an empty card, and a Status post carrying poll choices would silently
        // drop them.
        bool agrees = body.Kind switch
        {
            FeedPostKind.Status => keys.Count == 0 && choices.Count == 0,
            FeedPostKind.Photo => keys.Count > 0 && choices.Count == 0,
            FeedPostKind.Poll => keys.Count == 0 && choices.Count > 0,
            _ => false,
        };

        if (!agrees)
        {
            return (FeedRefusal.KindMismatch, null);
        }

        // Text is required except on a photo post, where the pictures are the content.
        if (text.Length == 0 && body.Kind != FeedPostKind.Photo)
        {
            return (FeedRefusal.BodyEmpty, null);
        }

        if (keys.Count > MaximumPhotos)
        {
            return (FeedRefusal.TooManyPhotos, null);
        }

        if (
            body.Kind == FeedPostKind.Poll
            && (
                choices.Count < MinimumChoices
                || choices.Count > MaximumChoices
                || choices.Any(c => c.Length > WebFeedPollChoiceEntity.LabelLength)
            )
        )
        {
            return (FeedRefusal.PollInvalid, null);
        }

        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (body.ParentId is int parentId)
        {
            // Readable(db), not a bare WebFeedPosts query: a deleted parent must answer
            // ParentNotFound exactly like a read would, so the write path and the read path agree
            // on what "exists" means. Readable no longer also requires ProfileVisible — see its own
            // remarks — but keeping the call is what stops the two from drifting apart again the
            // next time that condition changes.
            WebFeedPostEntity? parent = await Readable(db)
                .FirstOrDefaultAsync(p => p.Id == parentId, ct)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return (FeedRefusal.ParentNotFound, null);
            }

            // TWO levels deep: a post, its comments, and replies to those comments. A reply to a
            // reply is refused, which is what bounds the tree — the read path fetches exactly two
            // batches of children and the page indents exactly twice, and neither would survive an
            // arbitrary depth without becoming a loop.
            //
            // So the parent may have a parent, but its parent may not: that is the fourth level.
            if (parent.ParentEntityId is int grandParentId)
            {
                bool parentIsItselfAReply = await Readable(db)
                    .AnyAsync(p => p.Id == grandParentId && p.ParentEntityId != null, ct)
                    .ConfigureAwait(false);

                if (parentIsItselfAReply)
                {
                    return (FeedRefusal.ParentNotRoot, null);
                }
            }
        }

        if (body.GroupId is int groupId)
        {
            bool member = await db
                .GroupMembers.AnyAsync(
                    m => m.GroupEntityId == groupId && m.PlayerEntityId == playerId,
                    ct
                )
                .ConfigureAwait(false);

            if (!member)
            {
                return (FeedRefusal.GroupNotMember, null);
            }
        }

        // The photos, resolved from keys to rows, each one checked to be this player's and public.
        List<int> photoIds = [];

        foreach (string key in keys)
        {
            int id = await db
                .CameraPhotos.Where(p =>
                    p.PhotoKey == key
                    && p.PlayerEntityId == playerId
                    && p.PublishedAt != null
                    && p.DeletedAt == null
                )
                .Select(p => p.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (id == 0)
            {
                return (FeedRefusal.PhotoNotYours, null);
            }

            photoIds.Add(id);
        }

        WebFeedPostEntity post = new()
        {
            PlayerEntityId = playerId,
            GroupEntityId = body.GroupId,
            ParentEntityId = body.ParentId,
            Kind = body.Kind,
            Body = text,
        };

        db.WebFeedPosts.Add(post);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        for (int i = 0; i < photoIds.Count; i++)
        {
            db.WebFeedPostPhotos.Add(
                new WebFeedPostPhotoEntity
                {
                    WebFeedPostEntityId = post.Id,
                    CameraPhotoEntityId = photoIds[i],
                    Ordinal = i,
                }
            );
        }

        for (int i = 0; i < choices.Count; i++)
        {
            db.WebFeedPollChoices.Add(
                new WebFeedPollChoiceEntity
                {
                    WebFeedPostEntityId = post.Id,
                    Ordinal = i,
                    Label = choices[i],
                }
            );
        }

        foreach (string tag in FeedTags.Extract(text))
        {
            // Get-or-create, one row per tag for the whole hotel. Not a race worth locking: the
            // unique index settles a simultaneous first use of the same tag, and the loser's next
            // post finds the row the winner made.
            int tagId = await db
                .WebFeedTags.Where(t => t.Tag == tag)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (tagId == 0)
            {
                WebFeedTagEntity row = new() { Tag = tag };

                db.WebFeedTags.Add(row);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                tagId = row.Id;
            }

            db.WebFeedPostTags.Add(
                new WebFeedPostTagEntity
                {
                    WebFeedPostEntityId = post.Id,
                    WebFeedTagEntityId = tagId,
                }
            );
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await AnnounceAsync(db, post, playerId, text, ct).ConfigureAwait(false);
        await AnnounceChangeAsync(db, post, ct).ConfigureAwait(false);

        return (FeedRefusal.None, await GetPostAsync(post.Id, playerId, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Says on the bus what this post did to other people: who it named, and whose post it
    /// answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After the post is committed, never before. A notification pointing at a post id that was
    /// then rolled back is a link to nothing, and the post existing is the only part that has to
    /// be true.
    /// </para>
    /// <para>
    /// A name that matches no player raises nothing. That is why the lookup is here at all — the
    /// page turns every <c>@word</c> into a link without asking anybody, and only a ping needs to
    /// know the person is real.
    /// </para>
    /// </remarks>
    private async Task AnnounceAsync(
        VortexDbContext db,
        WebFeedPostEntity post,
        int playerId,
        string text,
        CancellationToken ct
    )
    {
        IReadOnlyList<string> names = FeedMentions.Extract(text);

        if (names.Count > 0)
        {
            // One query for the whole list. Compared on the name as stored, case-insensitively,
            // because a player writing @prodiigy means Prodiigy.
            List<int> mentioned = await db
                .Players.AsNoTracking()
                .Where(p => names.Contains(p.Name))
                .Select(p => p.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (int id in mentioned)
            {
                await events
                    .PublishAsync(new FeedMentionedEvent(playerId, id, post.Id), ct)
                    .ConfigureAwait(false);
            }
        }

        if (post.ParentEntityId is not int parentId)
        {
            return;
        }

        // The author of the thing being answered -- which is a comment's author when this is a
        // reply, not the root's. Answering somebody is what is worth telling them about.
        int author = await db
            .WebFeedPosts.AsNoTracking()
            .Where(p => p.Id == parentId)
            .Select(p => p.PlayerEntityId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (author != 0)
        {
            await events
                .PublishAsync(new FeedCommentedEvent(playerId, author, parentId, post.Id), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells every open feed that this thread moved. Separate from <see cref="AnnounceAsync"/>,
    /// which is about the people concerned; this one is about the page.
    /// </summary>
    private async Task AnnounceChangeAsync(
        VortexDbContext db,
        WebFeedPostEntity post,
        CancellationToken ct
    ) => await AnnounceChangeAsync(db, post.ParentEntityId ?? post.Id, ct).ConfigureAwait(false);

    public async Task<FeedPost?> VoteAsync(
        int playerId,
        int postId,
        int choiceId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Both halves, and neither is redundant. The choice has to belong to THIS poll, or a
        // choice id from anywhere in the hotel lands here; and the post has to be a readable poll,
        // or a vote attaches to a status nothing will ever render it under.
        bool choiceBelongs = await db
            .WebFeedPollChoices.AnyAsync(
                c => c.Id == choiceId && c.WebFeedPostEntityId == postId,
                ct
            )
            .ConfigureAwait(false);

        bool votable = await Readable(db)
            .AnyAsync(p => p.Id == postId && p.Kind == FeedPostKind.Poll, ct)
            .ConfigureAwait(false);

        if (!choiceBelongs || !votable)
        {
            return null;
        }

        WebFeedPollVoteEntity? existing = await db
            .WebFeedPollVotes.FirstOrDefaultAsync(
                v => v.WebFeedPostEntityId == postId && v.PlayerEntityId == playerId,
                ct
            )
            .ConfigureAwait(false);

        // Moved, not added — see IWebApiFeedService.VoteAsync for why this cannot be left to the
        // unique index alone.
        if (existing is null)
        {
            db.WebFeedPollVotes.Add(
                new WebFeedPollVoteEntity
                {
                    WebFeedPostEntityId = postId,
                    WebFeedPollChoiceEntityId = choiceId,
                    PlayerEntityId = playerId,
                }
            );
        }
        else
        {
            existing.WebFeedPollChoiceEntityId = choiceId;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // A vote moves the bars on everyone's copy of the poll, so it is a change like any other.
        // No notification, though: a poll is answered by design, and telling its author about each
        // answer would be the noisiest kind on the feed.
        await AnnounceChangeAsync(db, postId, ct).ConfigureAwait(false);

        return await GetPostAsync(postId, playerId, ct).ConfigureAwait(false);
    }

    public async Task<FeedPost?> SetReactionAsync(
        int playerId,
        int postId,
        FeedReactionKind kind,
        bool on,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (!await Readable(db).AnyAsync(p => p.Id == postId, ct).ConfigureAwait(false))
        {
            return null;
        }

        // Matched on all three columns, the same span as the unique index. On the pair alone, a
        // favourite would find the repost and move it instead of joining it.
        WebFeedReactionEntity? existing = await db
            .WebFeedReactions.FirstOrDefaultAsync(
                r =>
                    r.WebFeedPostEntityId == postId
                    && r.PlayerEntityId == playerId
                    && r.Kind == kind,
                ct
            )
            .ConfigureAwait(false);

        bool reacted = false;

        if (on && existing is null)
        {
            db.WebFeedReactions.Add(
                new WebFeedReactionEntity
                {
                    WebFeedPostEntityId = postId,
                    PlayerEntityId = playerId,
                    Kind = kind,
                }
            );

            // Only a NEW reaction is worth announcing, either kind. `existing is null` is what
            // makes that true: a retried request finds the row already there and falls through,
            // so a flaky connection cannot ring the same bell twice.
            reacted = true;
        }
        else if (!on && existing is not null)
        {
            // Removed outright, not soft-deleted: a soft delete keeps the pair and the unique
            // index then refuses the next honest reaction. See WebFeedReactionEntity.
            db.WebFeedReactions.Remove(existing);
        }

        // Both branches fall through when the row is already in the state asked for — that is what
        // makes a retried request a no-op rather than the opposite of what the reader pressed.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (reacted)
        {
            int author = await db
                .WebFeedPosts.AsNoTracking()
                .Where(p => p.Id == postId)
                .Select(p => p.PlayerEntityId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (author != 0)
            {
                await events
                    .PublishAsync(
                        kind == FeedReactionKind.Repost
                            ? new FeedRepostedEvent(playerId, author, postId)
                            : new FeedFavouritedEvent(playerId, author, postId),
                        ct
                    )
                    .ConfigureAwait(false);
            }
        }

        // Announced whichever way the reaction went, including a withdrawal: every reader's copy
        // of that card carries a count, and a count that only ever grows is worse than one that
        // never moves.
        await AnnounceChangeAsync(db, postId, ct).ConfigureAwait(false);

        return await GetPostAsync(postId, playerId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Says on the bus that a thread moved, so every open feed can refresh that one card.
    /// </summary>
    /// <remarks>
    /// Always the ROOT, never the comment: a reader's feed is a list of roots, and refreshing the
    /// root brings its comments with it. Walking up is one indexed read and a thread is at most
    /// two levels deep, so this is never more than two.
    /// </remarks>
    private async Task AnnounceChangeAsync(VortexDbContext db, int postId, CancellationToken ct)
    {
        int rootId = postId;

        for (int hop = 0; hop < 2; hop++)
        {
            int? parent = await db
                .WebFeedPosts.AsNoTracking()
                .Where(p => p.Id == rootId)
                .Select(p => p.ParentEntityId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (parent is not int up)
            {
                break;
            }

            rootId = up;
        }

        await events.PublishAsync(new FeedChangedEvent(rootId), ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(int playerId, int postId, CancellationToken ct)
    {
        await using VortexDbContext db = await _db.CreateDbContextAsync(ct).ConfigureAwait(false);

        WebFeedPostEntity? post = await db
            .WebFeedPosts.FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null, ct)
            .ConfigureAwait(false);

        // Ownership, not visibility: `Readable(db)` is the read gate and this is a write, so the
        // question here is whose post it is. Already deleted answers false rather than true, so a
        // page that retried learns the post is gone.
        if (post is null || post.PlayerEntityId != playerId)
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;

        post.DeletedAt = now;

        // The comments go too, other people's included — see the interface for why. Loaded and
        // looped rather than `ExecuteUpdateAsync`: a thread holds tens of rows, not thousands, and
        // the in-memory provider the tests run on does not implement that call.
        //
        // Two passes because the thread is two levels deep: the comments on this post, then the
        // replies to those comments. Missing the second pass would leave replies live under a
        // parent nobody can reach — in nobody's feed and nobody's thread.
        List<WebFeedPostEntity> comments = await db
            .WebFeedPosts.Where(p => p.ParentEntityId == postId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<int> commentIds = [.. comments.Select(c => c.Id)];

        List<WebFeedPostEntity> replies =
            commentIds.Count == 0
                ? []
                : await db
                    .WebFeedPosts.Where(p =>
                        p.ParentEntityId != null && commentIds.Contains(p.ParentEntityId.Value)
                    )
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

        foreach (WebFeedPostEntity child in comments.Concat(replies))
        {
            child.DeletedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Announced with the post's parent already read, because the row is now soft-deleted and
        // walking up from it would still work — but a reader whose card just vanished needs the
        // ROOT refreshed, and for a deleted root that is itself: the refresh comes back empty and
        // the card goes.
        await AnnounceChangeAsync(db, post.ParentEntityId ?? post.Id, ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// The one condition every read carries. Changing this changes who can be seen, so it exists
    /// once and every query starts from it.
    /// </summary>
    /// <remarks>
    /// Not deleted — that is the whole of it. This used to also require the author's
    /// <c>ProfileVisible</c>, copied across from <see cref="IWebApiPhotoService"/>'s rule without
    /// checking what that column defaults to: it defaults to <c>false</c>, so gating the feed on it
    /// made a new player's own posts invisible to everyone including themselves, and made
    /// <see cref="PublishAsync"/> read its own freshly-written row back through the same gate,
    /// answering the write with <see cref="FeedRefusal.None"/> and a null post. Publishing to the
    /// hotel's public feed is itself an act of publication and a different act from letting someone
    /// browse a profile; <c>ProfileVisible</c> governs the second and says nothing about the first.
    /// The gallery (<see cref="IWebApiPhotoService"/>) keeps its own rule — that decision stands.
    /// </remarks>
    /// <remarks>
    /// The <c>DeletedAt</c> clause is REDUNDANT and is kept deliberately. Every
    /// <c>VortexEntity</c> already carries a global soft-delete query filter
    /// (<c>ModelBuilderExtensions.ApplySoftDeleteQueryFilter</c>), so an ordinary query cannot see
    /// a deleted row whether or not this says so. Written out because a reader of this service
    /// should not have to know about an invisible model-wide filter to understand what a read
    /// returns — and because the day someone opts a query out with <c>IgnoreQueryFilters()</c>,
    /// this is the line that keeps the feed honest. It does mean the tests that assert a deleted
    /// post is unreadable are really exercising the global filter, not this clause.
    /// </remarks>
    private static IQueryable<WebFeedPostEntity> Readable(VortexDbContext db) =>
        db.WebFeedPosts.Where(p => p.DeletedAt == null);

    /// <summary>The search key a tag is stored and matched under.</summary>
    internal static string Normalise(string tag) => tag.Trim().TrimStart('#').ToLowerInvariant();

    /// <summary>
    /// Builds a <see cref="FeedPost"/> for every entry in <paramref name="roots"/>, fetching every
    /// supporting table exactly once for the whole batch instead of once per post.
    /// </summary>
    /// <param name="commentPreviewLimit">
    /// How many of each root's most recent comments to carry. <see cref="PreviewComments"/> for a
    /// feed page, <see cref="int.MaxValue"/> for a single post read in full.
    /// </param>
    /// <summary>
    /// One line of the feed before it is filled in: which post, and who put it there.
    /// </summary>
    /// <remarks>
    /// A class with settable properties rather than a record, because EF has to project into it
    /// from two different queries and then <c>Concat</c> them — a positional record's constructor
    /// is not something the provider can translate on both sides.
    /// </remarks>
    private sealed class FeedEntry
    {
        public int PostId { get; set; }

        /// <summary>Null for an original; the sharer for a repost.</summary>
        public int? ReposterId { get; set; }

        /// <summary>When it entered the feed — the post's date, or the share's.</summary>
        public DateTime At { get; set; }
    }

    /// <summary>
    /// Turns a page of entries into a page of cards.
    /// </summary>
    /// <remarks>
    /// The DISTINCT posts are assembled once and then handed out per entry, because a page can
    /// hold the same post twice — its author's original and somebody's share of it — and
    /// assembling it twice would double every query behind it.
    /// </remarks>
    private static async Task<List<FeedPost>> AssembleEntriesAsync(
        VortexDbContext db,
        List<FeedEntry> entries,
        int? viewerId,
        CancellationToken ct
    )
    {
        if (entries.Count == 0)
        {
            return [];
        }

        int[] postIds = [.. entries.Select(e => e.PostId).Distinct()];

        List<WebFeedPostEntity> rows = await Readable(db)
            .Include(p => p.PlayerEntity)
            .Include(p => p.GroupEntity)
            .Where(p => postIds.Contains(p.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<FeedPost> assembled = await AssembleAsync(db, rows, viewerId, PreviewComments, ct)
            .ConfigureAwait(false);

        Dictionary<int, FeedPost> byId = assembled.ToDictionary(p => p.Id);

        // Every sharer named on this page, in one read. A page is very often several shares by the
        // same handful of players, so this is a dictionary and not a lookup per entry.
        int[] reposterIds =
        [
            .. entries
                .Where(e => e.ReposterId is not null)
                .Select(e => e.ReposterId!.Value)
                .Distinct(),
        ];

        Dictionary<int, (string Name, string Figure)> reposters =
            reposterIds.Length == 0
                ? []
                : await db
                    .Players.AsNoTracking()
                    .Where(p => reposterIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.Figure,
                    })
                    .ToDictionaryAsync(p => p.Id, p => (p.Name, p.Figure), ct)
                    .ConfigureAwait(false);

        List<FeedPost> items = [];

        foreach (FeedEntry entry in entries)
        {
            if (!byId.TryGetValue(entry.PostId, out FeedPost? post))
            {
                // The post went between the entry query and this one. Skipping beats a hole.
                continue;
            }

            if (
                entry.ReposterId is int sharer
                && reposters.TryGetValue(sharer, out (string Name, string Figure) who)
            )
            {
                post = post with { Reposter = new FeedReposter(who.Name, who.Figure, entry.At) };
            }

            items.Add(post);
        }

        return items;
    }

    private static async Task<List<FeedPost>> AssembleAsync(
        VortexDbContext db,
        List<WebFeedPostEntity> roots,
        int? viewerId,
        int commentPreviewLimit,
        CancellationToken ct
    )
    {
        if (roots.Count == 0)
        {
            return [];
        }

        int[] rootIds = [.. roots.Select(r => r.Id)];

        // ONE query for every readable comment on every root in this batch — not one query per
        // root. Newest first, same as a single post's own comment query used to be; the "take three"
        // happens afterwards, in memory, per parent, which is also where the full count comes from,
        // since every comment row is already in hand by then.
        List<WebFeedPostEntity> allComments = await Readable(db)
            .Where(p => p.ParentEntityId != null && rootIds.Contains(p.ParentEntityId!.Value))
            .Include(p => p.PlayerEntity)
            .Include(p => p.GroupEntity)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        ILookup<int, WebFeedPostEntity> commentsByParent = allComments.ToLookup(c =>
            c.ParentEntityId!.Value
        );

        // A SECOND batch, and exactly one: the thread is two levels deep, so replies hang off the
        // comments just fetched and nothing hangs off a reply. One more query, not one per
        // comment — and the loop stops here rather than continuing, which is what `PublishAsync`'s
        // depth guard is protecting.
        int[] commentIds = [.. allComments.Select(c => c.Id)];

        List<WebFeedPostEntity> allReplies =
            commentIds.Length == 0
                ? []
                : await Readable(db)
                    .Where(p =>
                        p.ParentEntityId != null && commentIds.Contains(p.ParentEntityId!.Value)
                    )
                    .Include(p => p.PlayerEntity)
                    .Include(p => p.GroupEntity)
                    .OrderByDescending(p => p.CreatedAt)
                    .ThenByDescending(p => p.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

        ILookup<int, WebFeedPostEntity> repliesByParent = allReplies.ToLookup(r =>
            r.ParentEntityId!.Value
        );

        // Every post this batch answers for — the roots, their comments AND those comments'
        // replies — because a comment is a post with a parent and can carry its own photos, poll,
        // tags and reactions, and so can a reply.
        List<int> allPostIds =
        [
            .. rootIds,
            .. allComments.Select(c => c.Id),
            .. allReplies.Select(r => r.Id),
        ];

        Dictionary<int, List<PhotoSummary>> photosByPost = await LoadPhotosAsync(db, allPostIds, ct)
            .ConfigureAwait(false);
        Dictionary<int, FeedPoll> pollsByPost = await LoadPollsAsync(db, allPostIds, viewerId, ct)
            .ConfigureAwait(false);
        Dictionary<int, List<string>> tagsByPost = await LoadTagsAsync(db, allPostIds, ct)
            .ConfigureAwait(false);
        Dictionary<int, ReactionSummary> reactionsByPost = await LoadReactionsAsync(
                db,
                allPostIds,
                viewerId,
                ct
            )
            .ConfigureAwait(false);

        List<FeedPost> items = [];

        foreach (WebFeedPostEntity root in roots)
        {
            // Already newest-first from the query above.
            List<WebFeedPostEntity> children = [.. commentsByParent[root.Id]];

            int take =
                commentPreviewLimit == int.MaxValue
                    ? children.Count
                    : Math.Min(commentPreviewLimit, children.Count);

            // The PREVIEW is the most recent few, but a thread reads oldest first — so the take is
            // off the newest-first list and the order is restored afterwards.
            List<WebFeedPostEntity> preview = children.Take(take).ToList();
            preview.Reverse();

            // A comment carries its own replies, oldest first like the thread above it, and its
            // own count — the page shows both the same way it shows a root's.
            List<FeedPost> commentPosts =
            [
                .. preview.Select(child =>
                {
                    List<WebFeedPostEntity> theirReplies = [.. repliesByParent[child.Id]];

                    theirReplies.Reverse();

                    return Build(
                        child,
                        viewerId,
                        photosByPost,
                        pollsByPost,
                        tagsByPost,
                        reactionsByPost,
                        theirReplies.Count,
                        [
                            .. theirReplies.Select(reply =>
                                Build(
                                    reply,
                                    viewerId,
                                    photosByPost,
                                    pollsByPost,
                                    tagsByPost,
                                    reactionsByPost,
                                    // A reply has no children: the depth guard makes sure of it.
                                    0,
                                    []
                                )
                            ),
                        ]
                    );
                }),
            ];

            items.Add(
                Build(
                    root,
                    viewerId,
                    photosByPost,
                    pollsByPost,
                    tagsByPost,
                    reactionsByPost,
                    children.Count,
                    commentPosts
                )
            );
        }

        return items;
    }

    private static FeedPost Build(
        WebFeedPostEntity row,
        int? viewerId,
        Dictionary<int, List<PhotoSummary>> photosByPost,
        Dictionary<int, FeedPoll> pollsByPost,
        Dictionary<int, List<string>> tagsByPost,
        Dictionary<int, ReactionSummary> reactionsByPost,
        int commentCount,
        List<FeedPost> comments
    )
    {
        ReactionSummary reactions = reactionsByPost.GetValueOrDefault(row.Id) ?? EmptyReactions;

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
            photosByPost.GetValueOrDefault(row.Id) ?? [],
            pollsByPost.GetValueOrDefault(row.Id),
            tagsByPost.GetValueOrDefault(row.Id) ?? [],
            commentCount,
            reactions.Reposts,
            reactions.Favorites,
            reactions.RepostedByMe,
            reactions.FavoritedByMe,
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
    private static async Task<Dictionary<int, List<PhotoSummary>>> LoadPhotosAsync(
        VortexDbContext db,
        List<int> postIds,
        CancellationToken ct
    )
    {
        // Published and not deleted — the owner's ProfileVisible is deliberately not re-checked
        // here: the author attached their own already-published photo to their own public post,
        // the same publishing-is-the-consent rule Readable() states. WebApiPhotoService's gallery
        // keeps ProfileVisible for its own reads; that is a different question (browsing someone's
        // gallery by name) and its answer does not change.
        var rows = await db
            .WebFeedPostPhotos.Where(link =>
                postIds.Contains(link.WebFeedPostEntityId)
                && link.CameraPhotoEntity!.PublishedAt != null
                && link.CameraPhotoEntity.DeletedAt == null
            )
            .OrderBy(link => link.Ordinal)
            .Select(link => new
            {
                link.WebFeedPostEntityId,
                link.CameraPhotoEntity!.PhotoKey,
                OwnerId = link.CameraPhotoEntity.PlayerEntityId,
                OwnerName = link.CameraPhotoEntity.PlayerEntity!.Name,
                OwnerFigure = link.CameraPhotoEntity.PlayerEntity.Figure,
                link.CameraPhotoEntity.RoomEntityId,
                // PublishedAt, not CreatedAt — WebApiPhotoService.cs:207 uses the same field of the
                // same PhotoSummary for the same reason: the gallery orders by PublishedAt, and a
                // card that said "taken three weeks ago" under a photo published this morning would
                // be describing a different event from the one the ordering used. The two services
                // have to agree, or the same photo carries two different dates depending on which
                // page answered it. Safe to force non-null: the Where above already requires
                // `PublishedAt != null`.
                TakenAt = link.CameraPhotoEntity.PublishedAt!.Value,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.GroupBy(r => r.WebFeedPostEntityId)
            .ToDictionary(
                g => g.Key,
                g =>
                    g.Select(r => new PhotoSummary(
                            r.PhotoKey,
                            r.OwnerId.ToString(CultureInfo.InvariantCulture),
                            r.OwnerName,
                            r.OwnerFigure,
                            r.RoomEntityId,
                            // Not joined: the feed card has no use for a room name, unlike the
                            // gallery this record was designed for.
                            null,
                            r.TakenAt,
                            0,
                            false,
                            new List<string>()
                        ))
                        .ToList()
            );
    }

    private static async Task<Dictionary<int, FeedPoll>> LoadPollsAsync(
        VortexDbContext db,
        List<int> postIds,
        int? viewerId,
        CancellationToken ct
    )
    {
        List<WebFeedPollChoiceEntity> choiceRows = await db
            .WebFeedPollChoices.Where(c => postIds.Contains(c.WebFeedPostEntityId))
            .OrderBy(c => c.Ordinal)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (choiceRows.Count == 0)
        {
            return [];
        }

        List<WebFeedPollVoteEntity> voteRows = await db
            .WebFeedPollVotes.Where(v => postIds.Contains(v.WebFeedPostEntityId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        ILookup<int, WebFeedPollVoteEntity> votesByChoice = voteRows.ToLookup(v =>
            v.WebFeedPollChoiceEntityId
        );
        ILookup<int, WebFeedPollVoteEntity> votesByPost = voteRows.ToLookup(v =>
            v.WebFeedPostEntityId
        );

        Dictionary<int, FeedPoll> polls = [];

        foreach (
            IGrouping<int, WebFeedPollChoiceEntity> group in choiceRows.GroupBy(c =>
                c.WebFeedPostEntityId
            )
        )
        {
            int postId = group.Key;

            List<FeedPollChoice> choices =
            [
                .. group.Select(c => new FeedPollChoice(
                    c.Id,
                    c.Label,
                    votesByChoice[c.Id].Count()
                )),
            ];

            int total = votesByPost[postId].Count();

            int? mine = viewerId is null
                ? null
                : votesByPost[postId]
                    .Where(v => v.PlayerEntityId == viewerId)
                    .Select(v => (int?)v.WebFeedPollChoiceEntityId)
                    .FirstOrDefault();

            polls[postId] = new FeedPoll(total, mine, choices);
        }

        return polls;
    }

    private static async Task<Dictionary<int, List<string>>> LoadTagsAsync(
        VortexDbContext db,
        List<int> postIds,
        CancellationToken ct
    )
    {
        var rows = await db
            .WebFeedPostTags.Where(t => postIds.Contains(t.WebFeedPostEntityId))
            .Select(t => new { t.WebFeedPostEntityId, Tag = t.WebFeedTagEntity!.Tag })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.GroupBy(r => r.WebFeedPostEntityId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Tag).ToList());
    }

    private static async Task<Dictionary<int, ReactionSummary>> LoadReactionsAsync(
        VortexDbContext db,
        List<int> postIds,
        int? viewerId,
        CancellationToken ct
    )
    {
        List<WebFeedReactionEntity> rows = await db
            .WebFeedReactions.Where(r => postIds.Contains(r.WebFeedPostEntityId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.GroupBy(r => r.WebFeedPostEntityId)
            .ToDictionary(
                g => g.Key,
                g => new ReactionSummary(
                    g.Count(r => r.Kind == FeedReactionKind.Repost),
                    g.Count(r => r.Kind == FeedReactionKind.Favorite),
                    viewerId is not null
                        && g.Any(r =>
                            r.PlayerEntityId == viewerId && r.Kind == FeedReactionKind.Repost
                        ),
                    viewerId is not null
                        && g.Any(r =>
                            r.PlayerEntityId == viewerId && r.Kind == FeedReactionKind.Favorite
                        )
                )
            );
    }

    /// <summary>The four reaction-derived numbers a post carries, batched per post id.</summary>
    private sealed record ReactionSummary(
        int Reposts,
        int Favorites,
        bool RepostedByMe,
        bool FavoritedByMe
    );
}
