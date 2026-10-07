using System.Linq;
using System.Text.Json;
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
/// What happened to my room.
/// </summary>
/// <remarks>
/// The only handlers here that hit the database, and for one reason: these events name the ROOM
/// and the person to tell is its owner. That lookup is a single indexed read by primary key, and
/// it is done here rather than added to the events because the events are the room's, not the
/// notification system's — widening them for one consumer is how a domain event becomes a DTO.
/// </remarks>
internal static class RoomOwner
{
    /// <summary>The room's owner, or 0 when the room is gone — which a deleted room's tail-end events do reach.</summary>
    public static async Task<int> OfAsync(
        IDbContextFactory<VortexDbContext> dbContextFactory,
        int roomId,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .Rooms.AsNoTracking()
            .Where(r => r.Id == roomId)
            .Select(r => r.PlayerEntityId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Staff acted on my room.
/// </summary>
/// <remarks>
/// The owner is told what was done and not who did it — same reasoning as the sanctions in
/// <c>ModerationNotificationHandlers</c>. A name reset in particular is otherwise discovered by
/// walking into your own room and not recognising it.
/// </remarks>
public sealed class RoomModeratedNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<RoomModeratedByStaffEvent>
{
    public async ValueTask HandleAsync(
        RoomModeratedByStaffEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        int owner = await RoomOwner.OfAsync(dbContextFactory, e.RoomId, ct).ConfigureAwait(false);

        await notifications
            .NotifyAsync(
                owner,
                NotificationKind.RoomModerated,
                targetId: e.RoomId,
                data: JsonSerializer.Serialize(
                    new
                    {
                        doorUnlocked = e.DoorUnlocked,
                        nameReset = e.NameReset,
                        usersKicked = e.UsersKicked,
                    }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// I was given rights in a room.
/// </summary>
/// <remarks>
/// Only <c>"granted"</c>. The event covers four changes — granted, removed, removed_all, gave_up —
/// and the other three either name nobody (a mass removal) or are the player's own doing. Being
/// GIVEN rights is the one a player wants to know about and cannot otherwise see until they next
/// walk in.
/// </remarks>
public sealed class RoomRightsNotificationHandler(INotificationSink notifications)
    : IEventHandler<RoomRightsChangedEvent>
{
    public async ValueTask HandleAsync(
        RoomRightsChangedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        if (e.Change != "granted" || e.TargetPlayerId is not { } target)
        {
            return;
        }

        await notifications
            .NotifyAsync(
                target,
                NotificationKind.RoomRightsGranted,
                actorPlayerId: e.ActorId,
                targetId: e.RoomId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
