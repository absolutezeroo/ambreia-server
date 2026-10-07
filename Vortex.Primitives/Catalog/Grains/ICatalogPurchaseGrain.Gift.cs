using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Players;

namespace Vortex.Primitives.Catalog.Grains;

public partial interface ICatalogPurchaseGrain
{
    /// <summary>
    /// Buys an offer on behalf of the grain's player and grants it to <paramref name="receiverId"/>.
    /// Identical to <c>PurchaseOfferFromCatalogAsync</c> in every respect that protects the economy
    /// -- club gate, HC discount, refund-on-failure, credit-spend tracking, audit events -- because
    /// the only thing a gift changes is whose inventory the furniture lands in.
    /// </summary>
    /// <exception cref="System.Exception">
    /// Throws <c>CatalogPurchaseException</c> with <see cref="CatalogPurchaseErrorType.OfferNotFound"/>
    /// for an unknown offer, <see cref="CatalogPurchaseErrorType.PurchaseFailed"/> when the offer is
    /// not giftable, <see cref="CatalogPurchaseErrorType.RequiresMembership"/> when the buyer lacks the
    /// club level, or a balance error when they cannot pay.
    /// </exception>
    public Task<CatalogOfferSnapshot> PurchaseOfferAsGiftAsync(
        CatalogType catalogType,
        int offerId,
        string extraParam,
        PlayerId receiverId,
        GiftWrappingSpec wrapping,
        CancellationToken ct
    );

    /// <summary>
    /// Hands the grain's player what a present they just unwrapped was holding. No payment and no
    /// club gate: the offer was bought, gated and charged when it was wrapped, and re-checking here
    /// would let a since-retired offer trap its own gift inside a box forever.
    /// </summary>
    /// <returns>The offer that was granted, or null when it no longer exists in the catalogue.</returns>
    public Task<CatalogOfferSnapshot?> GrantPresentContentsAsync(
        int offerId,
        string extraParam,
        CancellationToken ct
    );
}
