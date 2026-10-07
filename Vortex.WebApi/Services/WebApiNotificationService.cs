using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Database.Entities.Web;

namespace Vortex.WebApi.Services;

/// <summary>
/// The website's notification list.
/// </summary>
/// <remarks>
/// <para>
/// Three round trips per page and never more: the count, the page, and one read for every actor
/// named on it. The actors are fetched together rather than through <c>Include</c> because a page
/// of twenty notifications is very often twenty lines about the same two or three people, and the
/// join would carry each of them back once per row.
/// </para>
/// <para>
/// The global soft-delete filter already hides deleted rows, so there is no <c>Readable</c> gate
/// here — nothing about a notification is conditional on who is asking,
/// because the only person who can ask is its owner.
/// </para>
/// </remarks>
public sealed class WebApiNotificationService(IDbContextFactory<VortexDbContext> dbContextFactory)
    : IWebApiNotificationService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 50;

    public async Task<NotificationPage> GetAsync(
        int playerId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        int wantedPage = page < 1 ? 1 : page;
        int wantedSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaximumPageSize => MaximumPageSize,
            _ => pageSize,
        };

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        IQueryable<WebNotificationEntity> mine = db.WebNotifications.Where(n =>
            n.PlayerEntityId == playerId
        );

        // Counted in one trip, because the unread number is what the bell shows and it is wanted
        // on every page — including the pages that hold none of the unread ones.
        var counts = await mine.GroupBy(n => 1)
            .Select(g => new { Total = g.Count(), Unread = g.Count(n => n.SeenAt == null) })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        List<WebNotificationEntity> rows = await mine.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((wantedPage - 1) * wantedSize)
            .Take(wantedSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        Dictionary<int, NotificationActor> actors = await ActorsAsync(db, rows, ct)
            .ConfigureAwait(false);

        List<Notification> items =
        [
            .. rows.Select(row => new Notification(
                row.Id,
                row.Kind,
                row.CreatedAt,
                row.SeenAt is not null,
                row.ActorPlayerEntityId is int actorId
                && actors.TryGetValue(actorId, out NotificationActor? actor)
                    ? actor
                    : null,
                row.TargetId,
                row.Data
            )),
        ];

        return new NotificationPage(
            wantedPage,
            wantedSize,
            counts?.Total ?? 0,
            counts?.Unread ?? 0,
            items
        );
    }

    public async Task<int> MarkSeenAsync(int playerId, CancellationToken ct = default)
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Loaded and stamped rather than one `ExecuteUpdateAsync`. Three reasons and none of them is
        // the statement count:
        //
        //   - ExecuteUpdate BYPASSES the change interceptor, so the rows would miss the auditing
        //     and the updated_at stamp every other write here gets (EntityChangeInterceptor says
        //     so in as many words).
        //   - the InMemory provider the tests run on does not implement it at all, so the
        //     set-based version is the version nothing can cover.
        //   - the set is the UNREAD ones, which is bounded by how long somebody went without
        //     looking, not by how long they have played.
        List<WebNotificationEntity> unread = await db
            .WebNotifications.Where(n => n.PlayerEntityId == playerId && n.SeenAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (unread.Count == 0)
        {
            return 0;
        }

        DateTime now = DateTime.UtcNow;

        foreach (WebNotificationEntity row in unread)
        {
            row.SeenAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return unread.Count;
    }

    /// <summary>
    /// Every player named as an actor on this page, in one read.
    /// </summary>
    /// <remarks>
    /// An actor who no longer exists is simply absent from the result, and the line renders
    /// without a name — which is right: the notification is still true, it just no longer has
    /// anybody to point at.
    /// </remarks>
    private static async Task<Dictionary<int, NotificationActor>> ActorsAsync(
        VortexDbContext db,
        List<WebNotificationEntity> rows,
        CancellationToken ct
    )
    {
        List<int> ids =
        [
            .. rows.Where(r => r.ActorPlayerEntityId is not null)
                .Select(r => r.ActorPlayerEntityId!.Value)
                .Distinct(),
        ];

        if (ids.Count == 0)
        {
            return [];
        }

        return await db
            .Players.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new NotificationActor(p.Id, p.Name, p.Figure))
            .ToDictionaryAsync(a => a.Id, ct)
            .ConfigureAwait(false);
    }
}
