using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;

namespace Vortex.Catalog.Grains;

public sealed partial class CatalogPurchaseGrain
{
    public async Task<CatalogOfferSnapshot> PurchaseOfferAsGiftAsync(
        CatalogType catalogType,
        int offerId,
        string extraParam,
        PlayerId receiverId,
        GiftWrappingSpec wrapping,
        CancellationToken ct
    )
    {
        int buyerId = (int)this.GetPrimaryKeyLong();

        if (receiverId.Value <= 0)
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
        }

        CatalogSnapshot snapshot = _catalogService.GetCatalogSnapshot(catalogType);

        if (!snapshot.OffersById.TryGetValue(offerId, out CatalogOfferSnapshot? offer))
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.OfferNotFound);
        }

        if (!offer.CanGift)
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
        }

        // The club gate and the discount follow the payer, not the recipient: gifting is a purchase
        // the buyer makes, so it cannot become a way around a club-only offer.
        int discountPercent = await ResolveMembershipPricingAsync(offer, ct).ConfigureAwait(true);

        // Gifting has no quantity on the wire -- the client sends one recipient and one offer.
        TryGetDebitRequests(offer, 1, discountPercent, out List<WalletDebitRequest> debitRequests);

        int creditCost = debitRequests
            .Where(r => r.CurrencyKind.CurrencyType == CurrencyType.Credits)
            .Sum(r => r.Amount);

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(buyerId);

        // Under an operation, like the purchase it is a variant of. CommerceOperationKind.Gift has
        // existed since the journal was written; gifting simply never minted one, so the flow that
        // moves a purchase between two players was the one with no record of having happened.
        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.Gift,
                buyerId,
                $"gift catalog={catalogType} offer={offerId} to={receiverId}",
                ct
            )
            .ConfigureAwait(true);

        WalletPurchaseResult<CatalogOfferSnapshot> result = await wallet
            .ExecutePurchaseAsync(
                debitRequests,
                operation,
                async innerCt =>
                {
                    await _journal
                        .TransitionAsync(
                            operation,
                            CommerceOperationState.Debited,
                            CommerceStepKeys.DEBIT,
                            null,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    // Ordered least-to-most reversible, and the notification events moved outside
                    // this compensated scope entirely (see SEC-06): a failure tracking a stat is
                    // harmless to compensate, but a failure *after* the inventory grant would leave
                    // the recipient's gift delivered for free once the wallet refunds the buyer.
                    if (creditCost > 0)
                    {
                        await _grainFactory
                            .GetPlayerGrain(PlayerId.Parse(buyerId))
                            .TrackCreditSpendAsync(creditCost, innerCt)
                            .ConfigureAwait(true);
                    }

                    await GrantGiftToReceiverAsync(
                            buyerId,
                            receiverId,
                            offer,
                            extraParam,
                            wrapping,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    // The recipient has it. Refunding the buyer past here would be a gift paid for
                    // by nobody.
                    await _journal
                        .TransitionAsync(
                            operation,
                            CommerceOperationState.Completed,
                            CommerceStepKeys.GIFT_WRAP,
                            null,
                            innerCt
                        )
                        .ConfigureAwait(true);

                    return offer;
                },
                _logger,
                ct,
                _journal
            )
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.FailedBeforePivot,
                    CommerceStepKeys.DEBIT,
                    "insufficient balance",
                    ct
                )
                .ConfigureAwait(true);

            throw CreateInsufficientBalanceException(result.Failure);
        }

        await _events
            .PublishAsync(
                new CatalogPurchasedEvent(buyerId, catalogType.ToString(), offerId, 1, creditCost),
                ct
            )
            .ConfigureAwait(true);

        // Emitted on top of the purchase event rather than instead of it: the buyer's spend belongs
        // in the same series as every other purchase, and a gift also has to answer "to whom".
        await _events
            .PublishAsync(
                new CatalogGiftPurchasedEvent(buyerId, receiverId.Value, offerId, creditCost),
                ct
            )
            .ConfigureAwait(true);

        return result.Reward!;
    }

    /// <summary>
    /// Wraps the purchase into a present furniture for the recipient, falling back to the plain
    /// grant when the wrapping cannot be built. The fallback is not a nicety: this runs inside the
    /// wallet's compensated scope, where throwing would refund a buyer whose gift may already have
    /// been delivered, and a gift arriving unwrapped is a far smaller wrong than one arriving twice
    /// or not at all.
    /// </summary>
    private async Task GrantGiftToReceiverAsync(
        int buyerId,
        PlayerId receiverId,
        CatalogOfferSnapshot offer,
        string extraParam,
        GiftWrappingSpec wrapping,
        CancellationToken ct
    )
    {
        // Only read when the parcel will actually show it. An anonymous gift names nobody, so
        // fetching the buyer's summary would be a grain call inside the wallet's compensated scope
        // to fill two fields that are then discarded.
        (string name, string figure) = wrapping.ShowPurchaserName
            ? await ReadPurchaserAsync(buyerId, ct).ConfigureAwait(true)
            : (string.Empty, string.Empty);

        bool wrapped = await _grainFactory
            .GetInventoryGrain(receiverId.Value)
            .GrantWrappedGiftAsync(offer, extraParam, wrapping, name, figure, ct)
            .ConfigureAwait(true);

        if (wrapped)
        {
            return;
        }

        await _grainFactory
            .GetInventoryGrain(receiverId.Value)
            .GrantCatalogOfferAsync(offer, extraParam, 1, ct)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The buyer's name and look for the parcel. Tolerates a summary that cannot be read: a gift
    /// that arrives unsigned is a small wrong, and throwing here would refund a purchase whose
    /// furniture may already have been granted.
    /// </summary>
    private async Task<(string Name, string Figure)> ReadPurchaserAsync(
        int buyerId,
        CancellationToken ct
    )
    {
        PlayerSummarySnapshot? buyer = await _grainFactory
            .GetPlayerGrain(PlayerId.Parse(buyerId))
            .GetSummaryAsync(ct)
            .ConfigureAwait(true);

        return buyer is null ? (string.Empty, string.Empty) : (buyer.Name, buyer.Figure);
    }

    public async Task<CatalogOfferSnapshot?> GrantPresentContentsAsync(
        int offerId,
        string extraParam,
        CancellationToken ct
    )
    {
        // Searched across every catalogue rather than one: a present outlives the page it was bought
        // from, and the wire never told us which catalogue that was.
        CatalogOfferSnapshot? offer = FindOfferAnywhere(offerId);

        if (offer is null)
        {
            _logger.LogWarning(
                "Present held offer {OfferId}, which no longer exists in any catalogue; player {PlayerId} gets nothing.",
                offerId,
                this.GetPrimaryKeyLong()
            );

            return null;
        }

        await _grainFactory
            .GetInventoryGrain(this.GetPrimaryKeyLong())
            .GrantCatalogOfferAsync(offer, extraParam, 1, ct)
            .ConfigureAwait(true);

        return offer;
    }

    private CatalogOfferSnapshot? FindOfferAnywhere(int offerId)
    {
        foreach (CatalogType catalogType in Enum.GetValues<CatalogType>())
        {
            if (
                _catalogService
                    .GetCatalogSnapshot(catalogType)
                    .OffersById.TryGetValue(offerId, out CatalogOfferSnapshot? offer)
            )
            {
                return offer;
            }
        }

        return null;
    }
}
