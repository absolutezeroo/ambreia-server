using System.Threading;
using System.Threading.Tasks;
using Vortex.Messages.Registry;
using Vortex.Primitives.Snapshots.Catalog;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Outgoing.Catalog;

namespace Vortex.PacketHandlers.Catalog;

public class GetBundleDiscountRulesetMessageHandler
    : IMessageHandler<GetBundleDiscountRulesetMessage>
{
    public async ValueTask HandleAsync(
        GetBundleDiscountRulesetMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        await ctx.SendComposerAsync(
                new BundleDiscountRulesetMessageComposer
                {
                    // No bundle discount. The client prices a multi-buy itself from this ruleset,
                    // so advertising one it would then apply -- "buy 6, pay 5" was what the sizes
                    // below used to say -- shows the buyer a total the purchase grain never
                    // charges. Restoring a discount means teaching CatalogPurchaseGrain the same
                    // arithmetic first; until then the honest ruleset is the empty one, which is
                    // also what the official server sends.
                    BundleDiscountRuleset = new BundleDiscountRulesetSnapshot(
                        MaxPurchaseSize: BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE,
                        BundleSize: 0,
                        BundleDiscountSize: 0,
                        BonusThreshold: -1,
                        AdditionalBonusDiscountThresholdQuantities: []
                    ),
                },
                ct
            )
            .ConfigureAwait(false);
    }
}
