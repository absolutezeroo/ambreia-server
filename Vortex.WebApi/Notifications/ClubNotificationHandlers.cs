using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// The membership that lapsed while nobody was looking, and the rewards that arrived with it.
/// </summary>
/// <remarks>
/// <c>ClubExpiredEvent</c> is the case that justifies the whole project: it fires off a timer, not
/// off anything the player did, so there is no screen they were on when it happened and no packet
/// that could have told them. Until now the only way to find out was to notice something had
/// stopped working.
/// </remarks>
public sealed class ClubExpiredNotificationHandler(INotificationSink notifications)
    : IEventHandler<ClubExpiredEvent>
{
    public async ValueTask HandleAsync(ClubExpiredEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.ClubExpired,
                data: JsonSerializer.Serialize(new { wasVip = e.WasVip }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>The club kickback. Credits that arrive on a cycle, not on a purchase.</summary>
public sealed class ClubPaydayNotificationHandler(INotificationSink notifications)
    : IEventHandler<ClubPaydayEvent>
{
    public async ValueTask HandleAsync(ClubPaydayEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.ClubPayday,
                data: JsonSerializer.Serialize(new { credits = e.Credits }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A gift token became available — something to go and claim.</summary>
public sealed class ClubGiftTokenNotificationHandler(INotificationSink notifications)
    : IEventHandler<ClubGiftTokenGrantedEvent>
{
    public async ValueTask HandleAsync(
        ClubGiftTokenGrantedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.ClubGiftToken,
                data: JsonSerializer.Serialize(
                    new { tokens = e.Tokens, available = e.TotalAvailable }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
