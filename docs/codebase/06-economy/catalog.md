# Catalog

## Purpose

Buying from the shop: the reference commerce flow, plus the four sibling purchase paths that do not
use the journal.

## The reference flow

`Vortex.Catalog/Grains/CatalogPurchaseGrain.cs` — `PurchaseOfferFromCatalogAsync`, keyed by player id.
The handler (`PurchaseFromCatalogMessageHandler`) contributes only the composer choice.

```
1  quantity = Math.Max(1, quantity)
   refuse above DEFAULT_MAX_PURCHASE_SIZE (100)          ← hostile-packet ceiling
2  offer resolved from the in-memory CatalogSnapshot     ← never from the DB
3  ResolveClubPricingAsync                                ← club gate + discount
4  TryGetDebitRequests → Total() computed in long        ← overflow guard
5  _journal.OpenAsync(CommerceOperationId.New(), CatalogPurchase, …)
6  wallet.ExecutePurchaseAsync(debits, operation, grant, logger, ct)
       ├─ TransitionAsync(Debited, DEBIT)
       ├─ IPlayerGrain.TrackCreditSpendAsync(creditCost)
       ├─ IInventoryGrain.GrantCatalogOfferAsync(…)      ← THE PIVOT
       └─ TransitionAsync(Pivoted, LOCAL_GRANT)
7  _journal.CompleteWithRelayAsync(operation, CatalogPurchasedEvent(…))
8  best-effort _events.PublishAsync + MarkRelayedAsync   ← the relay sweep is the retry
```

Two ceilings worth knowing:

- **Quantity is capped at 100** (`BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE`, a
  compile-time constant with no config override). The comment names the reason: unchecked `int` price
  multiplication wraps negative and drops the debit.
- **`Total(unitCost, quantity)` computes in `long`** and throws `OfferMisconfigured` above
  `int.MaxValue`.

Pinned by `Vortex.Database.Tests/Catalog/CatalogPurchaseTests.cs` —
`ThePriceOfAnUnboundedQuantity_WouldWrapNegative`.

### Club pricing

`ResolveClubPricingAsync` reads `IPlayerGrain.GetClubSubscriptionAsync` **only** when
`ClubLevel > 0 || DiscountPercent > 0`, throws `RequiresHabboClub` if under-levelled, and returns
`Math.Clamp(offer.DiscountPercent, 0, 100)` when active.

### Failure

`FailedBeforePivot` + `CreateInsufficientBalanceException`, which produces `NotEnoughCredits` or
`NotEnoughActivityPoints` with the offending `ActivityPointType`. The handler maps it through
`CatalogPurchaseErrorResponder.SendAsync` to `NotEnoughBalanceMessageComposer` or
`PurchaseNotAllowedMessageComposer`; success sends `PurchaseOKMessageComposer`.

The grant side is on [Inventory](inventory.md); atomicity is on [Transactions](transactions.md).

## Catalog data

`CatalogSnapshotProvider<TTag>` is a singleton `IReferenceDataProvider` publishing an immutable
snapshot via a volatile reference swap. **`LoadStage = 1`** — it runs after
`FurnitureDefinitionProvider` (stage 0), because it reads furniture definitions during its own reload.

Two trees exist, tagged: `NormalCatalog` and `BuildersClubCatalog`. `CatalogAdminService` reloads
**both** on every write, deliberately over-reloading rather than inferring which tree an edit touched.

Purchases read the snapshot, never the database. An admin edit is therefore invisible until a reload —
which is why every catalog admin write is followed by one. → [Dashboard operations](../08-dashboard/operations.md)

## The sibling purchase paths

Four flows debit a wallet without opening a journal. Each has its own atomicity story.

### Club subscription

`PurchaseFromCatalogMessageHandler.HandleClubPurchaseAsync` branches when the page layout is
`CatalogPageLayout.ClubBuy`, and calls `IPlayerGrain.PurchaseClubAsync(months, isVip, priceCredits)` —
`ExecutePurchaseAsync` **without** a `CommerceOperationId`.

`ApplyClubMonthsAsync` then handles streak and grace via `IServerConfigGrain`
(`club.gift_cycle_days` = 31, `club.streak_grace_days` = 7), upserts `PlayerSubscriptionEntity`,
grants club badges, upserts the kickback row, and publishes `ClubPurchasedEvent`.

The handler pre-checks the balance with `GetAmountForCurrencyAsync` before calling — a TOCTOU that is
harmless because the debit re-checks.

### Gift

`CatalogPurchaseGrain.Gift.cs` — `PurchaseOfferAsGiftAsync`, also journal-less. Two consequences, both
on [Transactions](transactions.md): the published event carries an empty `OperationId`, and a reload
throwing after the present is committed refunds the buyer while the recipient keeps the gift.

### LTD raffle

`LtdRaffleGrain`, keyed by series id. Not journalled, but atomic by construction: `DrawAsync` commits
**winner serial + stock decrement + loser state in one `SaveChangesAsync`** before any external
effect.

`RecoverAsync` rebuilds the oldest open batch from persisted `EnteredAt` on activation and either
re-arms or immediately fires the draw — so a restart mid-window does not strand a batch. Double-draw
is prevented by `_drawInProgress` (a plain field, not a lock) plus a `Result == PENDING` filter.

14 test cases in `Vortex.Database.Tests/Catalog/LtdRaffleGrainTests.cs`, including refund replay
across restarts and serial reservation.

### Voucher

`VoucherGrain`, keyed by `code.Trim().ToUpperInvariant()` — the normalisation lives in the
`GrainFactoryExtensions` accessor, not the grain. The **claim row is inserted before the grant** and
released on failure, and `catalog_voucher_redemptions (voucher_id, player_id)` is unique — so one
redemption per player per code is a database guarantee, not a read-then-write.

### Targeted offers

`PlayerTargetedOfferGrain` (per player) + `TargetedOfferManagerGrain` (`"global"` definition cache).
**Journalled.** An admin write reloads the manager grain.

## Persistence

| Table | Notable constraint |
|---|---|
| `catalog_pages` / `catalog_offers` / `catalog_products` | `CatalogPageEntity.Layout` is the one enum↔`varchar(50)` conversion in `OnModelCreating` |
| `catalog_vouchers` | `code` unique |
| `catalog_voucher_redemptions` | `(voucher_id, player_id)` unique |
| `catalog_ltd_series` / `catalog_ltd_raffle_entries` | |
| `targeted_offers` / `targeted_offer_products` / `player_targeted_offers` | products cascade from the offer, **restrict** against `furniture_definitions`; `(player_id, targeted_offer_id)` unique |
| `player_subscriptions` | `(player_id, subscription_type)` unique — what makes the club upsert correct |

## Tests

- `Vortex.Database.Tests/Catalog/CatalogPurchaseTests.cs` — quantity ceiling, price overflow,
  `AGrantThatFails_RefundsTheBuyer`
- `Vortex.Database.Tests/Commerce/CatalogGrantWindowTests.cs` —
  `AnEffectThatFailsLast_KeepsTheGoodsAndTheCharge`, `ABadgeAlreadyOwned_IsNotGrantedTwice`
- `Vortex.Database.Tests/Catalog/{VoucherRedemptionTests,TargetedOfferPurchaseTests,LtdRaffleGrainTests}.cs`
- `Vortex.Rooms.Tests/Wallet/ClubPurchaseRefundTests.cs`
- `Vortex.Database.Tests/Commerce/CatalogFulfillmentPlannerTests.cs`

ADR-001 names this suite as the proof that "goods delivered and purchase refunded" is unreachable for
catalog, gift and targeted offers.

## Sources

- `Vortex.Catalog/Grains/CatalogPurchaseGrain.cs`, `.Gift.cs`, `.RoomAd.cs`
- `Vortex.Catalog/Grains/{LtdRaffleGrain,VoucherGrain,PlayerTargetedOfferGrain,TargetedOfferManagerGrain}.cs`
- `Vortex.Catalog/CatalogService.cs`, `Providers/CatalogSnapshotProvider.cs`
- `Vortex.Catalog/CatalogAdminService.cs`
- `Vortex.Primitives/Snapshots/Catalog/BundleDiscountRulesetSnapshot.cs`
- `Vortex.Players/Grains/PlayerGrain.cs` — `PurchaseClubAsync`, `ApplyClubMonthsAsync`
- `Vortex.PacketHandlers/Catalog/{PurchaseFromCatalogMessageHandler,CatalogPurchaseErrorResponder}.cs`
