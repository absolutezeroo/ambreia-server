using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// What staff did to me.
/// </summary>
/// <remarks>
/// <para>
/// A sanction a player cannot see is a sanction they cannot learn from, and the in-game dialog
/// only reaches whoever is connected at the time. A trading lock in particular is applied far more
/// often to somebody who has already logged off.
/// </para>
/// <para>
/// The actor is deliberately NOT recorded on these. It is a staff member, the website would name
/// them in the line, and who moderated is not the player's business — the sanction is.
/// </para>
/// </remarks>
public sealed class MutedNotificationHandler(INotificationSink notifications)
    : IEventHandler<PlayerMutedInRoomEvent>
{
    public async ValueTask HandleAsync(
        PlayerMutedInRoomEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.Muted,
                targetId: e.RoomId,
                data: JsonSerializer.Serialize(new { seconds = e.DurationSeconds }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// My trading was locked.
/// </summary>
/// <remarks>
/// <c>LockedUntil</c> is null for a permanent lock, and the payload passes that through as-is
/// rather than inventing a far-future date — "until further notice" and "until Thursday" are
/// different sentences and the site has to be able to tell them apart.
/// </remarks>
public sealed class TradingLockedNotificationHandler(INotificationSink notifications)
    : IEventHandler<PlayerTradingLockedEvent>
{
    public async ValueTask HandleAsync(
        PlayerTradingLockedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.TradingLocked,
                data: JsonSerializer.Serialize(new { until = e.LockedUntil }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// A report I filed was dealt with.
/// </summary>
/// <remarks>
/// <para>
/// The one handler here that tells somebody good news, and the one that needs a lookup: the
/// event carries the ticket IDS and the staff member who closed them, never the people who
/// reported them. Those are read back off <c>cfh_tickets</c> — one query for the whole batch,
/// since staff close tickets in handfuls.
/// </para>
/// <para>
/// Whether the report was upheld is NOT passed on. <c>Sanctioned</c> says whether somebody was
/// punished, and telling a reporter that is telling them about a third party's sanction — which
/// is not theirs to know. "It was dealt with" is the whole of what they are owed.
/// </para>
/// <para>
/// Read after the close, so a ticket already closed before this batch is simply not in the
/// result: <c>CloseTicketsAsync</c> only touches the ones that were open, but it does not say
/// which, and re-reading by id would notify a reporter twice for a ticket somebody else's batch
/// had already closed. The state filter here is what stops that.
/// </para>
/// </remarks>
public sealed class CfhClosedNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<CfhTicketsClosedEvent>
{
    public async ValueTask HandleAsync(
        CfhTicketsClosedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        if (e.IssueIds.Length == 0)
        {
            return;
        }

        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<int> reporters = await db
            .CfhTickets.AsNoTracking()
            .Where(t => e.IssueIds.Contains(t.Id) && t.State == CfhTicketState.Closed)
            .Select(t => t.ReporterPlayerEntityId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (int reporter in reporters)
        {
            // No actor: it was staff, and who moderated is not the reporter's business. Same
            // reasoning as the sanctions above.
            await notifications
                .NotifyAsync(reporter, NotificationKind.CfhClosed, ct: ct)
                .ConfigureAwait(false);
        }
    }
}
