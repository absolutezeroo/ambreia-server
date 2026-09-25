using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Notifications;

namespace Vortex.Players.Events;

/// <summary>
/// Tells the client about bought Builders Club time or furni: the new status, which is what
/// unlocks the add-ons page's buttons and the placement limit, and the notification for it.
/// </summary>
/// <remarks>
/// The notification type is the client's own (<c>notification.builders_club.membership_made</c>
/// and siblings in external_variables), so the pop-up and its text need no server-side copy.
/// </remarks>
public sealed class BuildersClubGrantedPushHandler(
    IGrainFactory grainFactory,
    IBuildersClubService buildersClubService
) : IEventHandler<BuildersClubMembershipGrantedEvent>
{
    public async ValueTask HandleAsync(
        BuildersClubMembershipGrantedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        BuildersClubSubscriptionSnapshot subscription = await buildersClubService
            .GetSubscriptionAsync(e.PlayerId.Value, ct)
            .ConfigureAwait(false);

        IComposer status = BuildersClubSubscriptionStatusMessageComposer.From(subscription);

        IComposer[] composers = e.Notification is null
            ? [status]
            :
            [
                status,
                new NotificationDialogMessageComposer
                {
                    Type = e.Notification,
                    Parameters = ImmutableArray<NotificationDialogParameter>.Empty,
                },
            ];

        await grainFactory
            .GetPlayerPresenceGrain(e.PlayerId.Value)
            .SendComposerAsync(composers)
            .ConfigureAwait(false);
    }
}
