using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// What other players did to me.
/// </summary>
/// <remarks>
/// Every handler here notifies the TARGET, never the actor — the actor was there when they did it.
/// The sink drops a row whose actor is its own recipient, so a self-directed edge (which the
/// social graph does allow to be attempted) writes nothing.
/// </remarks>
public sealed class UserBlockedNotificationHandler(INotificationSink notifications)
    : IEventHandler<UserBlockedEvent>
{
    public async ValueTask HandleAsync(UserBlockedEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.Blocked,
                actorPlayerId: e.ActorPlayerId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>Somebody asked to be my friend.</summary>
public sealed class FriendRequestNotificationHandler(INotificationSink notifications)
    : IEventHandler<FriendRequestSentEvent>
{
    public async ValueTask HandleAsync(
        FriendRequestSentEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.FriendRequestReceived,
                actorPlayerId: e.ActorPlayerId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// My request was accepted. The event names the ACCEPTER as actor and the requester as target, so
/// the person to tell is the target — the one who asked and has been waiting.
/// </summary>
public sealed class FriendRequestAcceptedNotificationHandler(INotificationSink notifications)
    : IEventHandler<FriendRequestAcceptedEvent>
{
    public async ValueTask HandleAsync(
        FriendRequestAcceptedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.FriendRequestAccepted,
                actorPlayerId: e.ActorPlayerId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Somebody respected me.
/// </summary>
/// <remarks>
/// On <c>RespectGivenEvent</c> and not <c>RespectReceivedEvent</c>, although the latter is named
/// for exactly this: the received one carries only a running total and no actor, so it can say
/// "you were respected" and never by whom. The given one has both halves.
/// </remarks>
public sealed class RespectNotificationHandler(INotificationSink notifications)
    : IEventHandler<RespectGivenEvent>
{
    public async ValueTask HandleAsync(RespectGivenEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.TargetPlayerId,
                NotificationKind.RespectReceived,
                actorPlayerId: e.ActorPlayerId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
