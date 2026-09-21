using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// What a group decided about me.
/// </summary>
/// <remarks>
/// They all name the group as the target rather than putting it in the payload: the website links
/// the line to the group, and a group id does not go stale the way a name would.
/// </remarks>
public sealed class GroupAcceptedNotificationHandler(INotificationSink notifications)
    : IEventHandler<GroupMembershipAcceptedEvent>
{
    public async ValueTask HandleAsync(
        GroupMembershipAcceptedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.GroupMembershipAccepted,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.GroupId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>My request to join was turned down.</summary>
public sealed class GroupRejectedNotificationHandler(INotificationSink notifications)
    : IEventHandler<GroupMembershipRejectedEvent>
{
    public async ValueTask HandleAsync(
        GroupMembershipRejectedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.GroupMembershipRejected,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.GroupId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// I was removed from a group.
/// </summary>
/// <remarks>
/// Worth telling precisely because nothing else does: a kicked member finds out by noticing the
/// group is gone from their profile, which can be weeks later.
/// </remarks>
public sealed class GroupKickedNotificationHandler(INotificationSink notifications)
    : IEventHandler<GroupMemberKickedEvent>
{
    public async ValueTask HandleAsync(
        GroupMemberKickedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.GroupKicked,
                actorPlayerId: e.ActorPlayerId,
                targetId: e.GroupId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Somebody asked to join a group I run.
/// </summary>
/// <remarks>
/// <para>
/// The only handler that tells SEVERAL people. The event names the asker and the group and
/// nobody else, because a request is made to a group rather than to a person — so the recipients
/// are looked up here: every admin, plus the owner.
/// </para>
/// <para>
/// The owner is unioned in rather than assumed to hold an admin row. They usually do, and a
/// group whose owner is missing from <c>group_members</c> is exactly the case where nobody would
/// ever be told — the one that must not depend on a row being there.
/// </para>
/// <para>
/// One row each, not one row and a fan-out at read time. A notification belongs to a player and
/// is read and cleared by that player: two admins who each glance at it have each seen it, and a
/// shared row could only be marked read by whichever looked first.
/// </para>
/// </remarks>
public sealed class GroupJoinRequestNotificationHandler(
    INotificationSink notifications,
    IDbContextFactory<VortexDbContext> dbContextFactory
) : IEventHandler<GroupMembershipRequestedEvent>
{
    public async ValueTask HandleAsync(
        GroupMembershipRequestedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await dbContextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        List<int> admins = await db
            .GroupMembers.AsNoTracking()
            .Where(m => m.GroupEntityId == e.GroupId && m.Rank == GroupMemberRank.Admin)
            .Select(m => m.PlayerEntityId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int owner = await db
            .Groups.AsNoTracking()
            .Where(g => g.Id == e.GroupId)
            .Select(g => g.OwnerPlayerEntityId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (owner != 0)
        {
            admins.Add(owner);
        }

        foreach (int recipient in admins.Distinct())
        {
            // The sink drops the one whose recipient is the asker -- an admin asking to join their
            // own group would otherwise notify themselves.
            await notifications
                .NotifyAsync(
                    recipient,
                    NotificationKind.GroupJoinRequested,
                    actorPlayerId: e.ActorPlayerId,
                    targetId: e.GroupId,
                    ct: ct
                )
                .ConfigureAwait(false);
        }
    }
}
