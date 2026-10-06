using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// An LTD raffle came out in my favour.
/// </summary>
/// <remarks>
/// The serial is carried in the payload because it is the whole point of an LTD — "you won one"
/// and "you won number 3 of 100" are not the same sentence — and because the furniture can be
/// traded away long before the notification is read.
/// </remarks>
public sealed class RaffleWonNotificationHandler(INotificationSink notifications)
    : IEventHandler<LtdRaffleWonEvent>
{
    public async ValueTask HandleAsync(LtdRaffleWonEvent e, EventContext ctx, CancellationToken ct)
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.RaffleWon,
                targetId: e.FurniDefinitionId,
                data: JsonSerializer.Serialize(
                    new { series = e.SeriesId, serial = e.SerialNumber }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>A code was redeemed on my account, and what it paid out.</summary>
public sealed class VoucherNotificationHandler(INotificationSink notifications)
    : IEventHandler<VoucherRedeemedEvent>
{
    public async ValueTask HandleAsync(
        VoucherRedeemedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.PlayerId,
                NotificationKind.VoucherRedeemed,
                data: JsonSerializer.Serialize(
                    new
                    {
                        code = e.Code,
                        amount = e.Amount,
                        pointType = e.ActivityPointType,
                    }
                ),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
