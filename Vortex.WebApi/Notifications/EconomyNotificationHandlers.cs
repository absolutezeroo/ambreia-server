using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Notifications;
using Vortex.WebApi.Services;

namespace Vortex.WebApi.Notifications;

/// <summary>
/// My offer sold.
/// </summary>
/// <remarks>
/// The SELLER is the one told, and they are the one party guaranteed not to have been there: the
/// buyer pressed the button. The event carries both ids, so no lookup is needed — the seller was
/// put on it precisely because a sale has an absent half.
/// </remarks>
public sealed class MarketplaceSoldNotificationHandler(INotificationSink notifications)
    : IEventHandler<MarketplaceOfferBoughtEvent>
{
    public async ValueTask HandleAsync(
        MarketplaceOfferBoughtEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        await notifications
            .NotifyAsync(
                e.SellerId,
                NotificationKind.MarketplaceSold,
                targetId: e.DefinitionId,
                data: JsonSerializer.Serialize(new { price = e.Price, offer = e.OfferId }),
                ct: ct
            )
            .ConfigureAwait(false);
    }
}

/// <summary>Somebody bought me something.</summary>
public sealed class GiftNotificationHandler(INotificationSink notifications)
    : IEventHandler<CatalogGiftPurchasedEvent>
{
    public async ValueTask HandleAsync(
        CatalogGiftPurchasedEvent e,
        EventContext ctx,
        CancellationToken ct
    )
    {
        // The buyer is recorded as actor, which is right for the ordinary gift. A gift sent
        // anonymously would need the catalog to say so on the event, and it does not -- worth
        // knowing before this line is used to word "from X".
        await notifications
            .NotifyAsync(
                e.ReceiverPlayerId,
                NotificationKind.GiftReceived,
                actorPlayerId: e.BuyerPlayerId,
                targetId: e.OfferId,
                ct: ct
            )
            .ConfigureAwait(false);
    }
}
