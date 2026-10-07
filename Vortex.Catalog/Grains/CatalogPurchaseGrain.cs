using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Catalog.Exceptions;
using Vortex.Events.Registry;
using Vortex.Primitives.Catalog;
using Vortex.Primitives.Catalog.Enums;
using Vortex.Primitives.Catalog.Grains;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Snapshots.Catalog;

namespace Vortex.Catalog.Grains;

public sealed partial class CatalogPurchaseGrain(
    IGrainFactory grainFactory,
    ICatalogService catalogService,
    IEventPublisher events,
    ICancellableEventPublisher cancellableEvents,
    ICommerceJournal journal,
    ILogger<CatalogPurchaseGrain> logger
) : Grain, ICatalogPurchaseGrain
{
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly ICatalogService _catalogService = catalogService;
    private readonly IEventPublisher _events = events;
    private readonly ICancellableEventPublisher _cancellableEvents = cancellableEvents;
    private readonly ICommerceJournal _journal = journal;
    private readonly ILogger<CatalogPurchaseGrain> _logger = logger;

    public async Task<CatalogOfferSnapshot> PurchaseOfferFromCatalogAsync(
        CatalogType catalogType,
        int offerId,
        string extraParam,
        int quantity,
        CancellationToken ct
    )
    {
        quantity = Math.Max(1, quantity);

        // Quantity arrives straight off the wire, and only its lower bound was ever checked. Two
        // things break above the ceiling the client is handed, both silently: the cost is computed
        // as `CostCredits * quantity` in unchecked int arithmetic, so a large enough quantity wraps
        // the price to zero-or-negative, drops the debit request entirely and hands the goods over
        // for nothing; and the grant allocates one entity per copy, so an offer that happens to be
        // free lets a single packet allocate until the host dies. The real client never exceeds the
        // advertised ceiling, so anything above it is crafted and is refused rather than clamped.
        if (quantity > BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE)
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
        }

        CatalogSnapshot snapshot = _catalogService.GetCatalogSnapshot(catalogType);

        if (!snapshot.OffersById.TryGetValue(offerId, out CatalogOfferSnapshot? offer))
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.OfferNotFound);
        }

        int discountPercent = await ResolveMembershipPricingAsync(offer, ct).ConfigureAwait(true);

        TryGetDebitRequests(
            offer,
            quantity,
            discountPercent,
            out List<WalletDebitRequest> debitRequests
        );

        int creditCost = debitRequests
            .Where(r => r.CurrencyKind.CurrencyType == CurrencyType.Credits)
            .Sum(r => r.Amount);

        // The last point at which refusing costs nothing: the price is known, and neither the wallet
        // nor the commerce journal has been touched. A cancel is reported as a plain purchase
        // failure, which is the one outcome the client already knows how to show.
        EventContext purchasing = await _cancellableEvents
            .PublishCancellableAsync(
                new CatalogPurchasingEvent(
                    (int)this.GetPrimaryKeyLong(),
                    catalogType.ToString(),
                    offerId,
                    quantity,
                    creditCost
                ),
                ct
            )
            .ConfigureAwait(true);

        if (purchasing.Cancel)
        {
            _logger.LogInformation(
                "Catalog purchase of offer {OfferId} by player {PlayerId} was cancelled: {Reason}",
                offerId,
                (int)this.GetPrimaryKeyLong(),
                purchasing.CancelReason ?? string.Empty
            );

            throw new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
        }

        IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(
            (int)this.GetPrimaryKeyLong()
        );

        // Opened before anything durable happens, so a crash one instruction later still leaves a
        // row saying what was owed to whom. Preflight is finished by this point: the offer resolved,
        // the club gate passed, the price computed — nothing below fails for a reason that was
        // knowable in advance.
        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.CatalogPurchase,
                (int)this.GetPrimaryKeyLong(),
                $"catalog={catalogType} offer={offerId} quantity={quantity}",
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

                    // Ordered least-to-most reversible: a failure tracking a stat is harmless to
                    // compensate, but a failure *after* the inventory grant would leave the item
                    // granted for free once the wallet refunds. See SEC-06.
                    if (creditCost > 0)
                    {
                        await _grainFactory
                            .GetPlayerGrain(PlayerId.Parse((int)this.GetPrimaryKeyLong()))
                            .TrackCreditSpendAsync(creditCost, innerCt)
                            .ConfigureAwait(true);
                    }

                    // The pivot is inside this call: the inventory grain commits every family the
                    // offer carries in one transaction, and past that commit the goods are the
                    // player's. Nothing after it refunds.
                    await _grainFactory
                        .GetInventoryGrain((int)this.GetPrimaryKeyLong())
                        .GrantCatalogOfferAsync(offer, extraParam, quantity, innerCt)
                        .ConfigureAwait(true);

                    await _journal
                        .TransitionAsync(
                            operation,
                            CommerceOperationState.Pivoted,
                            CommerceStepKeys.LOCAL_GRANT,
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

        // Written with the terminal transition rather than published and hoped for. A crash between
        // the commit and the publish used to lose the event outright, and with it the quest progress
        // and the daily task that read it.
        CatalogPurchasedEvent purchased = new(
            (int)this.GetPrimaryKeyLong(),
            catalogType.ToString(),
            offerId,
            quantity,
            creditCost,
            operation.ToString()
        );

        await _journal.CompleteWithRelayAsync(operation, purchased, ct).ConfigureAwait(true);

        // Published after the purchase has fully succeeded and is out of the compensated scope: it
        // is a notification, not a purchase step, so a failing subscriber must never be able to
        // trigger a refund of an already-completed sale (SEC-06). The journal holds the same event,
        // so a failure here is a delay rather than a loss — the relay sweep publishes it.
        try
        {
            await _events.PublishAsync(purchased, ct).ConfigureAwait(true);

            await _journal.MarkRelayedAsync(operation, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Catalog purchase {OperationId} completed but its event could not be published; "
                    + "the relay will publish it.",
                operation
            );
        }

        return result.Reward!;
    }

    /// <summary>
    /// Enforces the offer's club gate against the buyer and returns the credit discount they qualify
    /// for. Shared by every purchase entry point: gifting an offer is still the buyer's purchase, so
    /// a club-only offer has to stay club-only however the furniture is routed.
    /// </summary>
    private async Task<int> ResolveMembershipPricingAsync(
        CatalogOfferSnapshot offer,
        CancellationToken ct
    )
    {
        // Resolve membership once, and only when the offer either gates on club level or carries an
        // HC discount.
        if (offer.MembershipLevel <= 0 && offer.DiscountPercent <= 0)
        {
            return 0;
        }

        MembershipSnapshot sub = await _grainFactory
            .GetPlayerGrain(PlayerId.Parse((int)this.GetPrimaryKeyLong()))
            .GetMembershipAsync(ct)
            .ConfigureAwait(true);

        int playerMembershipLevel = sub.IsActive ? (sub.IsVip ? 2 : 1) : 0;

        if (playerMembershipLevel < offer.MembershipLevel)
        {
            throw new CatalogPurchaseException(CatalogPurchaseErrorType.RequiresMembership);
        }

        // HC members get the offer's configured discount off the credit cost (0 = no discount).
        return sub.IsActive ? Math.Clamp(offer.DiscountPercent, 0, 100) : 0;
    }

    private bool TryGetDebitRequests(
        CatalogOfferSnapshot offer,
        int quantity,
        int discountPercent,
        out List<WalletDebitRequest> requests
    )
    {
        requests = [];

        if (offer.CostCredits > 0)
        {
            int creditAmount = Total(offer.CostCredits, quantity);

            if (discountPercent > 0)
            {
                creditAmount -= (int)(creditAmount * (discountPercent / 100.0));
            }

            if (creditAmount > 0)
            {
                requests.Add(
                    new WalletDebitRequest
                    {
                        CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                        Amount = creditAmount,
                    }
                );
            }
        }

        if (offer.CostSilver > 0)
        {
            requests.Add(
                new WalletDebitRequest
                {
                    CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Silver },
                    Amount = Total(offer.CostSilver, quantity),
                }
            );
        }

        if (offer.CostCurrency > 0)
        {
            requests.Add(
                new WalletDebitRequest
                {
                    CurrencyKind = new CurrencyKind
                    {
                        CurrencyType = CurrencyType.ActivityPoints,
                        ActivityPointType = offer.CurrencyTypeId,
                    },
                    Amount = Total(offer.CostCurrency, quantity),
                }
            );
        }

        return true;
    }

    /// <summary>
    /// Multiplies a unit price by the purchase quantity without letting the product wrap. Quantity
    /// is bounded above, so this can only trip on an offer priced past what any allowed quantity can
    /// multiply -- a mistyped price rather than a hostile packet. It still has to be caught: a
    /// wrapped total goes zero-or-negative, which drops the debit request and hands the goods over
    /// for free, and nothing downstream would ever report it.
    /// </summary>
    private static int Total(int unitCost, int quantity)
    {
        long total = (long)unitCost * quantity;

        return total <= int.MaxValue
            ? (int)total
            : throw new CatalogPurchaseException(CatalogPurchaseErrorType.OfferMisconfigured);
    }

    private static CatalogPurchaseException CreateInsufficientBalanceException(
        WalletDebitFailure? failure
    )
    {
        if (failure is null)
        {
            return new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
        }

        if (failure.CurrencyKind.CurrencyType == CurrencyType.Credits)
        {
            return new CatalogPurchaseException(
                CatalogPurchaseErrorType.NotEnoughCredits,
                new CatalogBalanceFailure
                {
                    NotEnoughCredits = true,
                    NotEnoughActivityPoints = false,
                    ActivityPointType = 0,
                }
            );
        }

        if (failure.CurrencyKind.CurrencyType == CurrencyType.ActivityPoints)
        {
            return new CatalogPurchaseException(
                CatalogPurchaseErrorType.NotEnoughActivityPoints,
                new CatalogBalanceFailure
                {
                    NotEnoughCredits = false,
                    NotEnoughActivityPoints = true,
                    ActivityPointType = failure.CurrencyKind.ActivityPointType ?? -1,
                }
            );
        }

        return new CatalogPurchaseException(CatalogPurchaseErrorType.PurchaseFailed);
    }
}
