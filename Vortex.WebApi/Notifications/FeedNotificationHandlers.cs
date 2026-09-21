using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// The website's feed: being named, being answered, being shared.
/// </summary>
/// <remarks>
/// The only handlers whose events were written for them. Everything else here listens to
/// something the hotel was already saying.
/// </remarks>
internal static class FeedNotificationGuard
{
    /// <summary>
    /// Whether <paramref name="recipientId"/> has blocked <paramref name="actorId"/>.
    /// </summary>
    /// <remarks>
    /// Blocking somebody means not hearing from them, and a mention is the one way a stranger can
    /// reach into your notifications uninvited. The mention still renders in the post — the text
    /// is the author's and this does not censor it — the bell simply does not ring.
    ///
    /// <para>
    /// Only the feed checks this. A block does not stop the hotel telling you your club lapsed,
    /// and it should not stop it telling you somebody blocked you.
    /// </para>
    /// </remarks>
    public static async Task<bool> BlockedAsync(
        IDbContextFactory<VortexDbContext> dbContextFactory,
        int recipientId,
        int actorId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .MessengerBlocked.AsNoTracking()
            .AnyAsync(
                b => b.PlayerEntityId == recipientId && b.BlockedPlayerEntityId == actorId,
                ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>Somebody wrote my name in a post.</summary>
public sealed class FeedMentionNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<FeedMentionedEvent>
{
    public async ValueTask HandleAsync(FeedMentionedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (
            await FeedNotificationGuard
                .BlockedAsync(dbContextFactory, e.MentionedPlayerId, e.ActorPlayerId, ct)
                .ConfigureAwait(false)
        )
        {
            return;
        }

        await notifications
            .NotifyAsync(
                e.MentionedPlayerId,
                NotificationKind.FeedMentioned,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.PostId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Somebody answered me.
/// </summary>
/// <remarks>
/// The target is the post that was ANSWERED rather than the comment itself, because that is the
/// thread the site should open — the new comment is inside it.
/// </remarks>
public sealed class FeedCommentNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<FeedCommentedEvent>
{
    public async ValueTask HandleAsync(FeedCommentedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (
            await FeedNotificationGuard
                .BlockedAsync(dbContextFactory, e.AuthorPlayerId, e.ActorPlayerId, ct)
                .ConfigureAwait(false)
        )
        {
            return;
        }

        await notifications
            .NotifyAsync(
                e.AuthorPlayerId,
                NotificationKind.FeedCommented,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.PostId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>Somebody shared my post.</summary>
public sealed class FeedRepostNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<FeedRepostedEvent>
{
    public async ValueTask HandleAsync(FeedRepostedEvent e, EventContext ctx, CancellationToken ct)
    {
        if (
            await FeedNotificationGuard
                .BlockedAsync(dbContextFactory, e.AuthorPlayerId, e.ActorPlayerId, ct)
                .ConfigureAwait(false)
        )
        {
            return;
        }

        await notifications
            .NotifyAsync(
                e.AuthorPlayerId,
                NotificationKind.FeedReposted,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.PostId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
