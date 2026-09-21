using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vortex.Database.Context;
using Vortex.Database.Entities.Web;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// Writes one <c>web_notifications</c> row. Every handler in this folder ends here.
/// </summary>
/// <remarks>
/// <para>
/// A failure is logged and swallowed. This runs inside an event handler, and an event handler is
/// downstream of something that already happened: throwing here would fail a club renewal because
/// the bell could not be rung, which is the wrong way round. A lost notification is a lost line of
/// text.
/// </para>
/// <para>
/// No deduplication. Two identical rows mean the thing genuinely happened twice — blocked,
/// unblocked, blocked again is two notifications and should be — and a unique index would quietly
/// swallow the second. The one thing guarded is an over-long <see cref="Data"/>, which MySQL would
/// refuse outright and take the row with it.
/// </para>
/// </remarks>
public sealed class NotificationSink(
    IDbContextFactory<VortexDbContext> dbContextFactory,
    ILogger<NotificationSink> logger
) : INotificationSink
{
    public async Task NotifyAsync(
        int playerId,
        NotificationKind kind,
        int? actorPlayerId = null,
        int? targetId = null,
        string? data = null,
        CancellationToken ct = default
    )
    {
        // Nobody to tell. Reached whenever an event names an optional player -- a decline-all
        // friend request names no requester -- and it is not worth a handler each guarding it.
        if (playerId <= 0)
        {
            return;
        }

        // A player is never notified of their own doing. Guarded here rather than in each handler
        // because it is true of every kind: the interesting half of "X blocked you" is X, and if
        // X is me the line says nothing.
        if (actorPlayerId is int actor && actor == playerId)
        {
            return;
        }

        try
        {
            await using VortexDbContext db = await dbContextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(false);

            db.WebNotifications.Add(
                new WebNotificationEntity
                {
                    PlayerEntityId = playerId,
                    Kind = kind,
                    ActorPlayerEntityId = actorPlayerId,
                    TargetId = targetId,
                    Data = Fit(data),
                }
            );

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            logger.LogWarning(
                failure,
                "Could not write a {Kind} notification for player {PlayerId}",
                kind,
                playerId
            );
        }
    }

    /// <summary>
    /// Drops an over-long payload rather than truncating it. Truncated JSON does not parse, so the
    /// website would render a broken line; without it, the line renders from the kind alone.
    /// </summary>
    private static string? Fit(string? data) =>
        data is null || data.Length <= WebNotificationEntity.DataLength ? data : null;
}
