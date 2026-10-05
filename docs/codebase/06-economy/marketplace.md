# Marketplace

## Purpose

Asynchronous item resale: list, buy, cancel, redeem. Not to be confused with **trading**, which is the
synchronous two-player exchange in a room ([Rooms](../04-rooms/room-architecture.md)).

## Ownership

`Vortex.Marketplace/Grains/MarketplacePurchaseGrain.cs`, keyed by the **acting** player, is
authoritative. The handlers under `Vortex.PacketHandlers/Marketplace/` do nothing but call the grain
and map its result code to the AS3 code.

`MarketplaceSearchGrain` (`"global"`) serves queries. `MarketplaceSettingsProvider` holds the
singleton settings row — `CommissionPercent` 1, `OfferDurationSeconds` 259200 (3 days).

## List

`MakeOfferAsync(furnitureItemId, price)`:

1. `price <= 0` → fail.
2. `IInventoryGrain.GetItemSnapshotAsync` must return non-null; `!snapshot.Definition.CanSell` → fail.
3. Journal opened (`MarketplaceList`).
4. **The offer row is inserted first**, in state `PendingRemoval` — invisible to buyers. The comment
   explains the ordering:

   > *"Invisible to buyers until the item has actually left the inventory. The old order — remove
   > first, insert second — destroyed the furniture if anything went wrong in between: gone from the
   > inventory and held by no offer."*

5. `inventoryGrain.RemoveFurnitureAsync(...)` — annotated **"THE PIVOT: the item leaves the seller's
   inventory here"**.
6. On success: `Pivoted`, `State = Active`, save, `Completed`, `MarketplaceOfferListedEvent`.
   On throw or `false`: `AbandonPendingOfferAsync` deletes the pending row, `FailedBeforePivot`.

> ### Known issue — listing does not remove the durable row
>
> Step 5 is not a pivot. `InventoryGrain.RemoveFurnitureAsync` delegates to
> `InventoryFurniModule.RemoveFurnitureAsync`, which is `_state.FurnitureById.Remove(itemId, out _)`
> and nothing else — a dictionary removal. → [Inventory](inventory.md)
>
> **Verified independently for this documentation:**
> - `Vortex.Marketplace`'s only `DbContext` use is against `MarketplaceOffers` (two sites). It never
>   touches the `furniture` table.
> - `FurnitureEntity` has **no** marketplace column and no marketplace FK.
> - `InventoryFurnitureLoader.LoadByPlayerIdAsync` filters on
>   `PlayerEntityId && RoomEntityId == null && WiredChestEntityId == null && DeletedAt == null` —
>   a listed row satisfies all four.
>
> So the row keeps `player_id = seller`, `room_id = NULL`, and returns to the seller's inventory on
> the next `ReloadFurnitureAsync` or grain deactivation. Meanwhile a buyer's delivery **inserts a new
> row**, and a cancellation restores a **new row** too.
>
> The telling contrast: the *wired chest* case has exactly this problem and was given a column
> (`wired_chest_id`) plus a loader predicate plus a comment about "the same row existing twice on
> screen". The marketplace was never given the equivalent.
>
> **Scope of the negative claim:** searched `Vortex.Marketplace` for any `furniture`-table access, all
> callers of `RemoveFurnitureAsync`, every consumer of `MarketplaceOfferListedEvent` (only
> `Vortex.Observability/Events/MarketplaceAuditHandlers.cs`), and `FurnitureEntity` for a marketplace
> foreign key. All negative. This is a **static-analysis conclusion** — the emulator was not run.
>
> **One query settles it.** After listing an item:
> ```sql
> SELECT id, player_id, room_id, wired_chest_id, deleted_at FROM furniture WHERE id = <listed item>;
> ```
> If `player_id` is still the seller and the other three are `NULL`, the finding holds.

## Buy

`BuyOfferAsync(offerId)` — the offer must be `Active && ExpiresAt > now`. Journal opened, then
`ExecutePurchaseAsync([Credits offer.Price], operation, grant, …)`. Inside the grant:

```csharp
commission   = Math.Max(1, offer.Price * settings.CommissionPercent / 100);
creditsOwed  = offer.Price - commission;
```

**The claim** is the only cross-player serialization in the whole marketplace, and it is a database
conditional update, not a grain:

```csharp
int claimed = await dbCtx.MarketplaceOffers
    .Where(o => o.Id == offer.Id && o.State == MarketplaceOfferState.Active)
    .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, MarketplaceOfferState.Sold)
                              .SetProperty(o => o.CreditsOwed, creditsOwed), ct);

if (claimed == 0) throw new OfferNoLongerActiveException();   // → executor refunds → return 1
```

Delivery then runs with a **bounded** retry — `POST_PIVOT_ATTEMPTS = 3`, the third rethrows:

```csharp
for (int attempt = 1; ; attempt++)
    try { await GrantFurnitureDefinitionCopiesAsync(defId, extraData, 1, operation, MARKETPLACE_DELIVER, ct); break; }
    catch when (attempt < POST_PIVOT_ATTEMPTS) { … }
```

On final failure, `RelistAfterFailedDeliveryAsync` sets the offer back to `Active` with
`CreditsOwed = 0` and rethrows so the executor refunds. **This is the one deliberate post-pivot revert
in the codebase**, and ADR-002 records why.

Result codes: 0 ok / 1 gone / 2 no credits, remapped to AS3 0/2/4 in the handler.

## Cancel and redeem-item

`CancelOrRedeemOfferAsync(offerId)` — scoped to `SellerEntityId == self`, refuses `Sold`.

```csharp
var operation = CommerceOperationId.Deterministic(CommerceOperationKind.MarketplaceCancel, offer.Id);
```

A deterministic id means double-clicking cancel is the *same* operation, so the receipt dedupes it.

`State = Cancelled` is the pivot, then `DeliverAsync(…, MARKETPLACE_RESTORE)`. Restitution is a **new
furniture row** of the same definition and `ExtraData` — not the original row.

## Redeem credits

`RedeemCreditsAsync` sums `CreditsOwed` over `Sold` offers, mints
`Deterministic(MarketplaceRedeem, soldOffers.Min(o => o.Id))`, calls `CreditOnceAsync`, **then** zeroes
`CreditsOwed`. Pay-then-clear is deliberate: a failed clear leaves the debt standing rather than the
money gone.

> ### Known issue — `RedeemCreditsAsync` discards the `CreditOnceAsync` result
>
> `CreditOnceAsync` returns `false` (it does **not** throw) when the receipt insert loses the unique
> index. The operation id is derived from `Min(Id)` of the sold set.
>
> If the clearing save fails after a successful credit, and a *further* offer sells before the seller
> retries, the retry recomputes `Min(Id)` to the same value, is answered "already credited", and then
> unconditionally zeroes `CreditsOwed` on the newer offer too — returning `totalCredits` to the client
> as if paid. **The seller loses the newer offer's proceeds.**
>
> `MarketplaceWindowTests.ACreditThatFails_LeavesTheDebtStandingRatherThanTheMoneyGone` does not catch
> it because its fake *throws* instead of returning `false`.

## Search and expiry

`MarketplaceSearchGrain` filters `State == Active && ExpiresAt > now`, groups by `SpriteId`, sorts by
`sortOrder` (1 = price asc, 2 = price desc, else sprite).

> **`MarketplaceOfferState.Expired` is never written or read anywhere** — verified by grep across the
> tree excluding migrations. There is no expiry sweep; expiry is a query predicate only, and an
> expired offer's item is recovered only when the seller clicks cancel.

## Persistence

| Table | Notable |
|---|---|
| `marketplace_offers` | indexed `(state, expires_at)` for the sweep that does not exist, and `(seller_id, state)` for the seller's list |
| `marketplace_settings` | singleton row |
| `commerce_operations` / `commerce_receipts` | the journal |

## Tests

- `Vortex.Database.Tests/Marketplace/MarketplaceClaimRaceTests.cs` — runs two buyers against one offer
  through the **real** `ExecuteUpdate` guard. On **SQLite**, not EF InMemory, and the file says why:
  *"the in-memory provider does not implement ExecuteUpdate at all — it would throw, and a test that
  cannot run the guard cannot vouch for it."*
- `Vortex.Database.Tests/Commerce/MarketplaceWindowTests.cs` — four characterisation tests naming what
  today's code leaves behind when the second half fails.
- `Vortex.Database.Tests/Commerce/MarketplaceListingTests.cs` — **the gap**: it fakes `IInventoryGrain`
  through `FakeProxy` and asserts only that `RemoveFurnitureAsync` *was called*. Nothing anywhere
  asserts that the seller's `furniture` row stops belonging to them — which is exactly the hole the
  first known issue falls through.

## Known unknowns

- **Unknown:** whether the missing `Expired` transition is intentional (offers simply stop matching)
  or an unfinished sweep. The enum member's existence suggests the latter; no code or comment says.

## Sources

- `Vortex.Marketplace/Grains/MarketplacePurchaseGrain.cs`, `MarketplaceSearchGrain.cs`
- `Vortex.Marketplace/Providers/MarketplaceSettingsProvider.cs`
- `Vortex.Inventory/Grains/Modules/InventoryFurniModule.cs`
- `Vortex.Inventory/Factories/InventoryFurnitureLoader.cs`
- `Vortex.Database/Entities/Marketplace/{MarketplaceOfferEntity,MarketplaceOfferState}.cs`
- `Vortex.Database/Entities/Furniture/FurnitureEntity.cs`
- `Vortex.PacketHandlers/Marketplace/*.cs`
