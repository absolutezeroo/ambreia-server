using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Database.Context;
using Vortex.Database.Entities.Furniture;
using Vortex.Database.Entities.Marketplace;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Marketplace.Grains;
using Vortex.Primitives.Marketplace.Providers;
using Vortex.Primitives.Marketplace.Snapshots;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Rooms.Object;

namespace Vortex.Marketplace.Grains;

public sealed class MarketplacePurchaseGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IMarketplaceSettingsProvider settingsProvider,
    IEventPublisher events,
    ICommerceJournal journal,
    ILogger<MarketplacePurchaseGrain> logger
) : Grain, IMarketplacePurchaseGrain
{
    private readonly IDbContextFactory<VortexDbContext> _dbCtxFactory = dbCtxFactory;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IMarketplaceSettingsProvider _settingsProvider = settingsProvider;
    private readonly IEventPublisher _events = events;
    private readonly ICommerceJournal _journal = journal;
    private readonly ILogger<MarketplacePurchaseGrain> _logger = logger;

    /// <summary>How many times a post-pivot delivery is retried before an operator is called. The
    /// receipt makes each attempt safe; the point of retrying at all is that most failures here are
    /// a moment of unavailability rather than a lasting one.</summary>
    private const int POST_PIVOT_ATTEMPTS = 3;

    public async Task<(int Result, int OfferId)> MakeOfferAsync(
        int furnitureItemId,
        int price,
        CancellationToken ct
    )
    {
        if (price <= 0)
        {
            return (1, 0);
        }

        IInventoryGrain inventoryGrain = _grainFactory.GetInventoryGrain(this.GetPrimaryKeyLong());

        FurnitureItemSnapshot? snapshot = await inventoryGrain
            .GetItemSnapshotAsync(new RoomObjectId(furnitureItemId), ct)
            .ConfigureAwait(true);

        if (snapshot is null)
        {
            return (1, 0);
        }

        if (snapshot.RoomId.Value > 0)
        {
            return (1, 0);
        }

        if (!snapshot.Definition.CanSell)
        {
            return (1, 0);
        }

        int furniType = snapshot.Definition.ProductType == ProductType.Wall ? 2 : 1;

        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.MarketplaceList,
                (int)this.GetPrimaryKeyLong(),
                $"item={furnitureItemId} price={price}",
                ct
            )
            .ConfigureAwait(true);

        MarketplaceOfferEntity offer = new()
        {
            SellerEntityId = (int)this.GetPrimaryKeyLong(),
            FurnitureDefinitionEntityId = snapshot.Definition.Id,
            SpriteId = snapshot.SpriteId,
            FurnitureType = furniType,
            ExtraData = snapshot.ExtraData,
            Price = price,
            // Invisible to buyers until the item has actually left the inventory. The old order —
            // remove first, insert second — destroyed the furniture if anything went wrong in
            // between: gone from the inventory and held by no offer.
            State = MarketplaceOfferState.PendingRemoval,
            CreditsOwed = 0,
            ExpiresAt = DateTime.UtcNow.AddSeconds(
                _settingsProvider.GetSettings().OfferDurationSeconds
            ),
        };

        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);
        dbCtx.MarketplaceOffers.Add(offer);
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        bool removed;

        try
        {
            // The seller's client stops showing the item here. This is a cache and a notification,
            // not the pivot -- InventoryGrain.RemoveFurnitureAsync writes nothing durable, which is
            // correct for its other five callers because each of them persists the item's new home
            // itself before calling it. This one had no such half, so the row stayed exactly as the
            // inventory loader selects it and came back on the next reload.
            removed = await inventoryGrain
                .RemoveFurnitureAsync(new RoomObjectId(furnitureItemId), ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await AbandonPendingOfferAsync(offer.Id, operation, ex.Message).ConfigureAwait(true);

            throw;
        }

        if (!removed)
        {
            await AbandonPendingOfferAsync(offer.Id, operation, "the item could not be removed")
                .ConfigureAwait(true);

            return (1, 0);
        }

        // THE PIVOT: the row leaves the seller's inventory for good. Soft-deleted rather than moved,
        // because there is nowhere to move it to -- an offer holds no furniture id, and every exit
        // the offer has (sold, cancelled, expired) hands out a *fresh* row through DeliverAsync. The
        // original is therefore a second copy however the listing ends, not a thing to give back.
        //
        // The four conditions are the inventory loader's own predicate. Matching nothing means the
        // item stopped being the seller's free-standing property between the snapshot read above and
        // here -- placed in a room, pledged to a chest, already minted -- and the listing must not
        // proceed. DeletedAt is absent from the test on purpose: the global soft-delete query filter
        // supplies it.
        //
        // The check and the write are one statement. Reading the row and soft-deleting it after left
        // a window between the two: two listings of the same chair could both read it as the
        // seller's, both delete it, and both go Active -- and every exit an offer has hands out a
        // *fresh* row through DeliverAsync, so two offers over one row is two chairs where there was
        // one. Claimed, the second update matches nothing and its count comes back zero
        // (ECON-ITM-004). This is the shape BuyOfferAsync and the mint already use.
        int sellerId = (int)this.GetPrimaryKeyLong();

        int claimed = await dbCtx
            .Furnitures.Where(f =>
                f.Id == furnitureItemId
                && f.PlayerEntityId == sellerId
                && f.RoomEntityId == null
                && f.WiredChestEntityId == null
                && f.JukeboxEntityId == null
            )
            .ExecuteUpdateAsync(row => row.SetProperty(f => f.DeletedAt, DateTime.UtcNow), ct)
            .ConfigureAwait(true);

        if (claimed == 0)
        {
            await AbandonPendingOfferAsync(
                    offer.Id,
                    operation,
                    "the item was no longer the seller's to list"
                )
                .ConfigureAwait(true);

            return (1, 0);
        }

        await _journal
            .TransitionAsync(
                operation,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.MARKETPLACE_WITHDRAW,
                null,
                ct
            )
            .ConfigureAwait(true);

        offer.State = MarketplaceOfferState.Active;
        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        await _journal
            .TransitionAsync(operation, CommerceOperationState.Completed, null, null, ct)
            .ConfigureAwait(true);

        await _events
            .PublishAsync(
                new MarketplaceOfferListedEvent(
                    (int)this.GetPrimaryKeyLong(),
                    offer.Id,
                    offer.FurnitureDefinitionEntityId,
                    price
                ),
                ct
            )
            .ConfigureAwait(true);

        return (0, offer.Id);
    }

    public async Task<bool> CancelOrRedeemOfferAsync(int offerId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        MarketplaceOfferEntity? offer = await dbCtx
            .MarketplaceOffers.FirstOrDefaultAsync(
                o => o.Id == offerId && o.SellerEntityId == (int)this.GetPrimaryKeyLong(),
                ct
            )
            .ConfigureAwait(true);

        if (offer is null)
        {
            return false;
        }

        if (offer.State == MarketplaceOfferState.Sold)
        {
            return false;
        }

        // The operation id is derived from the offer, not minted fresh: a seller who clicks cancel
        // twice, or retries after a timeout, is the same operation asking again — and the receipt on
        // the restitution is what stops the second one handing the item back a second time.
        CommerceOperationId operation = OperationForOffer(
            CommerceOperationKind.MarketplaceCancel,
            offer.Id
        );

        await _journal
            .OpenIfNewAsync(
                operation,
                CommerceOperationKind.MarketplaceCancel,
                (int)this.GetPrimaryKeyLong(),
                $"offer={offer.Id}",
                ct
            )
            .ConfigureAwait(true);

        // THE PIVOT. The offer is off the market; the item is owed back.
        //
        // Conditional for the same reason BuyOfferAsync is: the buyer runs in another player's
        // grain, so nothing serialises it against this one, and the Sold check above is only true
        // as of the read. An unconditional SaveChanges here overwrote the buyer's Sold with
        // Cancelled and handed the seller a second copy of an item somebody had already paid for.
        //
        // Cancelled is in the set so a seller whose restitution failed can still retry through
        // here; PendingRemoval is not, because the listing has not taken the item out of the
        // seller's inventory yet and giving it "back" would mint a copy.
        int claimed = await dbCtx
            .MarketplaceOffers.Where(o =>
                o.Id == offer.Id
                && (
                    o.State == MarketplaceOfferState.Active
                    || o.State == MarketplaceOfferState.Expired
                    || o.State == MarketplaceOfferState.Cancelled
                )
            )
            .ExecuteUpdateAsync(
                up => up.SetProperty(p => p.State, MarketplaceOfferState.Cancelled),
                ct
            )
            .ConfigureAwait(true);

        if (claimed == 0)
        {
            return false;
        }

        await _journal
            .TransitionAsync(
                operation,
                CommerceOperationState.Pivoted,
                CommerceStepKeys.MARKETPLACE_RESTORE,
                null,
                ct
            )
            .ConfigureAwait(true);

        IInventoryGrain inventoryGrain = _grainFactory.GetInventoryGrain(this.GetPrimaryKeyLong());

        await DeliverAsync(
                inventoryGrain,
                offer,
                operation,
                CommerceStepKeys.MARKETPLACE_RESTORE,
                ct
            )
            .ConfigureAwait(true);

        await _events
            .PublishAsync(
                new MarketplaceOfferCancelledEvent(
                    (int)this.GetPrimaryKeyLong(),
                    offer.Id,
                    offer.FurnitureDefinitionEntityId,
                    offer.Price
                ),
                ct
            )
            .ConfigureAwait(true);

        return true;
    }

    public async Task<int> BuyOfferAsync(int offerId, CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;
        MarketplaceOfferEntity? offer = await dbCtx
            .MarketplaceOffers.FirstOrDefaultAsync(
                o =>
                    o.Id == offerId && o.State == MarketplaceOfferState.Active && o.ExpiresAt > now,
                ct
            )
            .ConfigureAwait(true);

        if (offer is null)
        {
            return 1;
        }

        IPlayerWalletGrain walletGrain = _grainFactory.GetPlayerWalletGrain(
            this.GetPrimaryKeyLong()
        );

        List<WalletDebitRequest> debitRequests =
        [
            new WalletDebitRequest
            {
                CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                Amount = offer.Price,
            },
        ];

        CommerceOperationId operation = CommerceOperationId.New();

        await _journal
            .OpenAsync(
                operation,
                CommerceOperationKind.MarketplaceBuy,
                (int)this.GetPrimaryKeyLong(),
                $"offer={offer.Id} price={offer.Price} seller={offer.SellerEntityId}",
                ct
            )
            .ConfigureAwait(true);

        WalletPurchaseResult<bool> result;

        try
        {
            result = await walletGrain
                .ExecutePurchaseAsync(
                    debitRequests,
                    operation,
                    async innerCt =>
                    {
                        int commission = Math.Max(
                            1,
                            offer.Price * _settingsProvider.GetSettings().CommissionPercent / 100
                        );
                        int creditsOwed = offer.Price - commission;

                        // THE PIVOT, and a correct one: the conditional update is what stops two
                        // buyers owning the same offer, and it was right before any of this. What
                        // was missing is everything after it.
                        int claimed = await dbCtx
                            .MarketplaceOffers.Where(o =>
                                o.Id == offer.Id && o.State == MarketplaceOfferState.Active
                            )
                            .ExecuteUpdateAsync(
                                up =>
                                    up.SetProperty(p => p.State, MarketplaceOfferState.Sold)
                                        .SetProperty(p => p.CreditsOwed, creditsOwed),
                                innerCt
                            )
                            .ConfigureAwait(true);

                        if (claimed == 0)
                        {
                            throw new OfferNoLongerActiveException(offer.Id);
                        }

                        await _journal
                            .TransitionAsync(
                                operation,
                                CommerceOperationState.Pivoted,
                                CommerceStepKeys.MARKETPLACE_DELIVER,
                                null,
                                innerCt
                            )
                            .ConfigureAwait(true);

                        IInventoryGrain inventoryGrain = _grainFactory.GetInventoryGrain(
                            this.GetPrimaryKeyLong()
                        );

                        try
                        {
                            // Retried rather than compensated on the first stumble: the receipt makes
                            // each attempt safe, and most failures at this point are a moment of
                            // unavailability rather than a lasting one.
                            await DeliverAsync(
                                    inventoryGrain,
                                    offer,
                                    operation,
                                    CommerceStepKeys.MARKETPLACE_DELIVER,
                                    innerCt
                                )
                                .ConfigureAwait(true);
                        }
                        catch (Exception)
                        {
                            // Only then, the compensation — and it is a complete one here, unlike
                            // anywhere else past a pivot. A marketplace offer holds no furniture row
                            // of its own between listing and delivery: the item exists as the offer.
                            // Putting the offer back Active therefore restores exactly the state
                            // before the claim, and the buyer is refunded by the shared primitive as
                            // this leaves. (ADR-002 records why this one keeps a post-pivot revert.)
                            await RelistAfterFailedDeliveryAsync(offer.Id, operation)
                                .ConfigureAwait(true);

                            throw;
                        }

                        return true;
                    },
                    _logger,
                    ct,
                    _journal
                )
                .ConfigureAwait(true);
        }
        catch (OfferNoLongerActiveException)
        {
            // The buyer was already refunded by the purchase helper. Report the offer as gone
            // rather than letting this escape: the handler does not catch, so an exception here
            // means the client is never answered at all and its marketplace dialog simply hangs.
            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.FailedBeforePivot,
                    CommerceStepKeys.MARKETPLACE_DELIVER,
                    "the offer was claimed by another buyer",
                    ct
                )
                .ConfigureAwait(true);

            return 1;
        }

        if (result.Succeeded)
        {
            await _journal
                .TransitionAsync(operation, CommerceOperationState.Completed, null, null, ct)
                .ConfigureAwait(true);

            await _events
                .PublishAsync(
                    new MarketplaceOfferBoughtEvent(
                        (int)this.GetPrimaryKeyLong(),
                        offer.SellerEntityId,
                        offer.Id,
                        offer.FurnitureDefinitionEntityId,
                        offer.Price
                    ),
                    ct
                )
                .ConfigureAwait(true);
        }
        else
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
        }

        return result.Succeeded ? 0 : 2;
    }

    /// <summary>
    /// Losing the claim is ordinary traffic, not a fault: two people clicked Buy on the same offer
    /// and one of them was second. It is thrown rather than returned because the claim happens
    /// inside the shared purchase helper's work step, which uses the exception to trigger the
    /// refund; <see cref="BuyOfferAsync"/> turns it back into a result code so the buyer actually
    /// hears that the offer is gone.
    /// </summary>
    private sealed class OfferNoLongerActiveException(int offerId)
        : InvalidOperationException($"Marketplace offer {offerId} is no longer active.");

    public async Task<int> RedeemCreditsAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        List<MarketplaceOfferEntity> soldOffers = await dbCtx
            .MarketplaceOffers.Where(o =>
                o.SellerEntityId == (int)this.GetPrimaryKeyLong()
                && o.State == MarketplaceOfferState.Sold
                && o.CreditsOwed > 0
            )
            .ToListAsync(ct)
            .ConfigureAwait(true);

        int totalCredits = soldOffers.Sum(o => o.CreditsOwed);

        if (totalCredits <= 0)
        {
            return 0;
        }

        // One operation per offer, not one for the batch.
        //
        // Pay first and clear second is right, and stays. What was wrong was the identity: it came
        // from the smallest id in the batch, and the batch is not fixed. Pay a batch whose smallest
        // id is 5, crash before the clearing, and a new sale can join the next attempt while 5 is
        // still the smallest. The retry recomputes the same operation id, the receipt answers
        // "already paid" for a total that no longer matches, and the code then clears every offer
        // in the new batch: the new sale's credits are gone, with a receipt saying they were paid.
        // A sale arriving with a *lower* id inverts it — a different identity over debts already
        // settled, paid a second time.
        //
        // An offer is the thing that is owed, so an offer is what the payment is identified by. The
        // cost is a wallet call and a commit per offer rather than one of each; redeeming is a rare
        // deliberate act, not a hot path, and a seller with a long list would rather wait than be
        // short.
        int paid = 0;
        int settled = 0;

        foreach (MarketplaceOfferEntity offer in soldOffers)
        {
            int owed = offer.CreditsOwed;

            CommerceOperationId operation = OperationForOffer(
                CommerceOperationKind.MarketplaceRedeem,
                offer.Id
            );

            await _journal
                .OpenIfNewAsync(
                    operation,
                    CommerceOperationKind.MarketplaceRedeem,
                    (int)this.GetPrimaryKeyLong(),
                    $"offer={offer.Id} credits={owed}",
                    ct
                )
                .ConfigureAwait(true);

            await _grainFactory
                .GetPlayerWalletGrain(this.GetPrimaryKeyLong())
                .CreditOnceAsync(
                    [
                        new WalletDebitRequest
                        {
                            CurrencyKind = new CurrencyKind { CurrencyType = CurrencyType.Credits },
                            Amount = owed,
                        },
                    ],
                    operation,
                    CommerceStepKeys.MARKETPLACE_CREDIT,
                    ct
                )
                .ConfigureAwait(true);

            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.Pivoted,
                    CommerceStepKeys.MARKETPLACE_CREDIT,
                    null,
                    ct
                )
                .ConfigureAwait(true);

            // This offer's debt only. A failure here leaves the others owed and retryable, each
            // answered by its own receipt.
            offer.CreditsOwed = 0;
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

            await _journal
                .TransitionAsync(operation, CommerceOperationState.Completed, null, null, ct)
                .ConfigureAwait(true);

            paid += owed;
            settled++;
        }

        await _events
            .PublishAsync(
                new MarketplaceCreditsRedeemedEvent((int)this.GetPrimaryKeyLong(), paid, settled),
                ct
            )
            .ConfigureAwait(true);

        return paid;
    }

    /// <summary>
    /// Hands a definition over as a named step, retrying a bounded number of times. Every attempt
    /// carries the same receipt, so a call that timed out after committing cannot deliver twice.
    /// </summary>
    private async Task DeliverAsync(
        IInventoryGrain inventory,
        MarketplaceOfferEntity offer,
        CommerceOperationId operation,
        string stepKey,
        CancellationToken ct
    )
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await inventory
                    .GrantFurnitureDefinitionCopiesAsync(
                        offer.FurnitureDefinitionEntityId,
                        offer.ExtraData,
                        1,
                        operation,
                        stepKey,
                        ct
                    )
                    .ConfigureAwait(true);

                return;
            }
            catch (Exception ex) when (attempt < POST_PIVOT_ATTEMPTS)
            {
                await _journal
                    .TransitionAsync(
                        operation,
                        CommerceOperationState.Completing,
                        stepKey,
                        ex.Message,
                        CancellationToken.None
                    )
                    .ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Puts a claimed offer back on the market after a delivery that would not go through, and
    /// escalates if even that fails — which is the state nobody could see before: the offer Sold, the
    /// item undelivered, and a log line about it.
    /// </summary>
    private async Task RelistAfterFailedDeliveryAsync(int offerId, CommerceOperationId operation)
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(true);

            await dbCtx
                .MarketplaceOffers.Where(o => o.Id == offerId)
                .ExecuteUpdateAsync(
                    up =>
                        up.SetProperty(p => p.State, MarketplaceOfferState.Active)
                            .SetProperty(p => p.CreditsOwed, 0),
                    CancellationToken.None
                )
                .ConfigureAwait(true);

            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.FailedBeforePivot,
                    CommerceStepKeys.MARKETPLACE_DELIVER,
                    "delivery failed; the offer was re-listed and the buyer refunded",
                    CancellationToken.None
                )
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Marketplace offer {OfferId} is Sold, undelivered and could not be re-listed.",
                offerId
            );

            await _journal
                .TransitionAsync(
                    operation,
                    CommerceOperationState.NeedsIntervention,
                    CommerceStepKeys.MARKETPLACE_DELIVER,
                    ex.Message,
                    CancellationToken.None
                )
                .ConfigureAwait(true);
        }
    }

    /// <summary>Abandons an offer that never took its item, before anything irreversible.</summary>
    private async Task AbandonPendingOfferAsync(
        int offerId,
        CommerceOperationId operation,
        string reason
    )
    {
        try
        {
            await using VortexDbContext dbCtx = await _dbCtxFactory
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(true);

            // Removed rather than marked cancelled: as far as the seller is concerned this listing
            // never happened, and a cancelled offer in their history for a listing that never took
            // their item would be a lie about what they did.
            MarketplaceOfferEntity? pending = await dbCtx
                .MarketplaceOffers.FirstOrDefaultAsync(o => o.Id == offerId, CancellationToken.None)
                .ConfigureAwait(true);

            if (pending is not null)
            {
                dbCtx.MarketplaceOffers.Remove(pending);
                await dbCtx.SaveChangesAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            // Harmless as state goes — a PendingRemoval offer is invisible to buyers and holds
            // nothing — but it is a row that will sit there, so it is said out loud.
            _logger.LogError(
                ex,
                "Marketplace offer {OfferId} could not be abandoned after {Reason}.",
                offerId,
                reason
            );
        }

        await _journal
            .TransitionAsync(
                operation,
                CommerceOperationState.FailedBeforePivot,
                CommerceStepKeys.MARKETPLACE_WITHDRAW,
                reason,
                CancellationToken.None
            )
            .ConfigureAwait(true);
    }

    private static CommerceOperationId OperationForOffer(CommerceOperationKind kind, int offerId) =>
        CommerceOperationId.Deterministic(kind, offerId);

    public async Task<(int CreditsOwed, List<MarketplaceOfferSnapshot> Offers)> GetOwnOffersAsync(
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await _dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;
        List<MarketplaceOfferEntity> offers = await dbCtx
            .MarketplaceOffers.Where(o =>
                o.SellerEntityId == (int)this.GetPrimaryKeyLong()
                && o.State != MarketplaceOfferState.Cancelled
                // A listing that has not taken its item yet is not an offer the seller has; it is a
                // moment. One that gets stuck there is recovery's problem, and the client has no
                // status to render it with anyway.
                && o.State != MarketplaceOfferState.PendingRemoval
            )
            .ToListAsync(ct)
            .ConfigureAwait(true);

        int creditsOwed = offers.Sum(o => o.CreditsOwed);

        List<MarketplaceOfferSnapshot> snapshots = offers
            .Select(o => new MarketplaceOfferSnapshot
            {
                OfferId = o.Id,
                SpriteId = o.SpriteId,
                FurnitureType = o.FurnitureType,
                ExtraData = o.ExtraData,
                Price = o.Price,
                AvgPrice = o.Price,
                OfferCount = 1,
                ExpiresIn =
                    o.State == MarketplaceOfferState.Active
                        ? (int)Math.Max(0, (o.ExpiresAt - now).TotalSeconds)
                        : 0,
                Status = (int)o.State,
                CreditsOwed = o.CreditsOwed,
            })
            .ToList();

        return (creditsOwed, snapshots);
    }
}
