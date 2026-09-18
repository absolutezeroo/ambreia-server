using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Collectibles.Grains;

/// <summary>
/// The Collectors Guild shop: what is for sale, and selling it.
/// </summary>
/// <remarks>
/// A singleton caching its offers the same way <see cref="INftCollectionsGrain"/> caches
/// collections — they change when an admin edits them, not while anybody is shopping. Selling goes
/// through here rather than through a handler so that the limited-edition count is decided in one
/// place: two players buying the last copy at once both reach this grain, and only one of them can
/// be inside it at a time.
/// </remarks>
public interface INftStoreGrain : IGrainWithStringKey
{
    /// <summary>What the shop tab lists: everything an admin has left enabled, sold-out included —
    /// the client draws that one full and unbuyable, which is the answer a player who missed a
    /// limited edition should get. Only <see cref="PurchaseAsync"/> decides what is still
    /// buyable.</summary>
    public Task<ImmutableArray<NftStoreOfferSnapshot>> GetOffersAsync(CancellationToken ct);

    /// <summary>
    /// Sells one offer to a player: takes the emeralds, hands over the furniture and counts the
    /// sale. Identified by product code, because that is what the client sends back.
    /// </summary>
    public Task<NftStorePurchaseOutcome> PurchaseAsync(
        PlayerId playerId,
        string productCode,
        CancellationToken ct
    );

    /// <summary>Re-reads the offers, so an admin's edits go live without a restart.</summary>
    public Task ReloadAsync(CancellationToken ct);
}

/// <summary>
/// How a shop purchase ended. The client only tells success from failure — it alerts on one code and
/// celebrates on everything else — but the reason is worth keeping for the log.
/// </summary>
public enum NftStorePurchaseOutcome
{
    Sold = 0,
    UnknownOffer = 1,
    SoldOut = 2,
    NotEnoughEmeralds = 3,
    NoSuchFurniture = 4,
    Failed = 5,
}
