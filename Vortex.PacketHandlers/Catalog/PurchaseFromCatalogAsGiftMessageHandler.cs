using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Moderation;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class PurchaseFromCatalogAsGiftMessageHandler(
    IGrainFactory grainFactory,
    IWordFilter wordFilter
) : IMessageHandler<PurchaseFromCatalogAsGiftMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IWordFilter _wordFilter = wordFilter;

    public async ValueTask HandleAsync(
        PurchaseFromCatalogAsGiftMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0 || string.IsNullOrWhiteSpace(message.RecieverName))
        {
            return;
        }

        if (await SafetyLockGuard.RefusedAsync(_grainFactory, ctx, ct).ConfigureAwait(false))
        {
            return;
        }

        PlayerId? receiverId = await _grainFactory
            .GetPlayerDirectoryGrain()
            .GetPlayerIdAsync(message.RecieverName, ct)
            .ConfigureAwait(false);

        if (receiverId is null)
        {
            await ctx.SendComposerAsync(new GiftReceiverNotFoundEventMessageComposer(), ct)
                .ConfigureAwait(false);

            return;
        }

        // The card's message, and the name a gifted pet or bot carries in the extra parameter —
        // both before the grain, which debits. The receiver's name is a lookup key, not prose.
        string? giftMessage = await _wordFilter
            .PassAsync(ctx, message.Message ?? string.Empty, WordFilterSource.Gift, ct)
            .ConfigureAwait(false);
        string? extraParam = giftMessage is null
            ? null
            : await _wordFilter
                .PassPurchaseExtraAsync(ctx, message.ExtraParam, ct)
                .ConfigureAwait(false);

        if (giftMessage is null || extraParam is null)
        {
            await ctx.SendComposerAsync(new PurchaseErrorMessageComposer(), ct)
                .ConfigureAwait(false);

            return;
        }

        try
        {
            // Everything the purchase involves -- offer lookup, the giftable check, the club gate,
            // the HC discount, the debit and its refund, spend tracking, the audit trail -- belongs
            // to the purchase grain. This handler only resolves the recipient and answers the client.
            CatalogOfferSnapshot offer = await _grainFactory
                .GetCatalogPurchaseGrain(ctx.PlayerId)
                .PurchaseOfferAsGiftAsync(
                    CatalogType.Normal,
                    message.OfferCode,
                    extraParam,
                    receiverId.Value,
                    new GiftWrappingSpec
                    {
                        StuffTypeId = message.BoxStuffTypeId,
                        BoxTypeId = message.BoxTypeId,
                        RibbonTypeId = message.RibbonTypeId,
                        Message = giftMessage,
                        ShowPurchaserName = message.ShowPurchaserName,
                    },
                    ct
                )
                .ConfigureAwait(false);

            await ctx.SendComposerAsync(new PurchaseOKMessageComposer { Offer = offer }, ct)
                .ConfigureAwait(false);
        }
        catch (CatalogPurchaseException ex)
        {
            await CatalogPurchaseErrorResponder.SendAsync(ctx, ex, ct).ConfigureAwait(false);
        }
    }
}
