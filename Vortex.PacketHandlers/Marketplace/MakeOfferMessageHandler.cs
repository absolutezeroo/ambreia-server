using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Marketplace.Grains;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Incoming.Marketplace;
using Vortex.Protocol.Messages.Outgoing.Marketplace;

namespace Vortex.PacketHandlers.Marketplace;

public class MakeOfferMessageHandler(IGrainFactory grainFactory) : IMessageHandler<MakeOfferMessage>
{
    private readonly IGrainFactory _grainFactory = grainFactory;

    public async ValueTask HandleAsync(
        MakeOfferMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.PlayerId <= 0)
        {
            return;
        }

        // Listing is spending too — it costs the commission — and the client hides the whole
        // marketplace while the lock is on. AS3 5 is this message's own "error".
        if (
            await Catalog
                .SafetyLockGuard.IsLockedAsync(_grainFactory, ctx, ct)
                .ConfigureAwait(false)
        )
        {
            await ctx.SendComposerAsync(
                    new MarketplaceMakeOfferResultMessageComposer { Result = 5 },
                    ct
                )
                .ConfigureAwait(false);

            return;
        }

        if (message.FurnitureItemIds.IsDefaultOrEmpty)
        {
            return;
        }

        IMarketplacePurchaseGrain purchases = _grainFactory.GetMarketplacePurchaseGrain(
            ctx.PlayerId
        );

        // One offer per item, in the order the client listed them. The awaits are sequential on
        // purpose: this is one grain, so there is nothing to win by fanning out, and a partial
        // failure has to leave the offers that did go through standing.
        bool allListed = true;

        foreach (int itemId in message.FurnitureItemIds)
        {
            // The client sends the inventory strip's `ref`, which carries the item kind in its
            // sign — negative for a floor item under the layout we serialize today. Same guard as
            // PlaceObjectMessageHandler, and it stays correct whichever way the sign goes.
            (int result, _) = await purchases
                .MakeOfferAsync(Math.Abs(itemId), message.Price, ct)
                .ConfigureAwait(false);

            allListed &= result == 0;
        }

        // result: 0=success(1 in AS3), 1=error(5 in AS3)
        int as3Result = allListed ? 1 : 5;

        await ctx.SendComposerAsync(
                new MarketplaceMakeOfferResultMessageComposer { Result = as3Result },
                ct
            )
            .ConfigureAwait(false);
    }
}
