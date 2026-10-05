# Economy overview

## Purpose

Every place money or item ownership changes hands, and the two-layer machinery that is supposed to
make those changes survive a crash.

## The pivot

Every value-moving flow has one irreversible moment — **the pivot** — where the goods are handed over.
Before it, failure is compensated by refunding. After it, failure is *retried*, never reversed.

```
open journal ──► debit wallet ──► [ THE PIVOT: grant the goods ] ──► complete + relay event
       │              │                        │
       │              └─ fails ─► refund, mark FailedBeforePivot
       └─ everything after the pivot is retried from the journal, never undone
```

Two layers implement this, and they are separable — several flows use only the first.

### Layer 1 — the compensated executor

`Vortex.Primitives/Players/Wallet/WalletPurchaseExtensions.cs` — `ExecutePurchaseAsync<TReward>`:

```
TryDebitAsync  →  caller's grantAsync(ct)  →  on throw: CreditBackAsync(…, CancellationToken.None)  →  rethrow
```

The `CancellationToken.None` on the refund is load-bearing: the comment records that cancellation is
the most common reason a grant throws, so a client disconnect must not strand a debit.

> **It is not a grain method.** It is a caller-side extension taking a `Func<CancellationToken, Task<TReward>>`,
> and it runs the grant in the *caller's* turn. The per-player serialization comes from the caller
> being player-keyed, not from the wallet. → [Lifecycle and concurrency](../03-orleans/lifecycle-concurrency.md)

Callers: `CatalogPurchaseGrain` (×3 partials), `LtdRaffleGrain`, `PlayerTargetedOfferGrain`,
`MarketplacePurchaseGrain`, `PlayerGrain.PurchaseClubAsync`, `RentableSpaceGrain`,
`GroupDirectoryGrain`. `WiredTradeSettlement` documents a deliberate opt-out.

### Layer 2 — the journal

`Vortex.Primitives/Commerce/` + `Vortex.Database/Commerce/CommerceJournal.cs`.

| Piece | Role |
|---|---|
| `CommerceOperationId` | UUIDv7, or **`Deterministic(kind, id)`** so a double-click is the same operation |
| `CommerceOperationState` | `Prepared → Debited → Pivoted → Completing → Completed`, or `FailedBeforePivot` / `NeedsIntervention` |
| `CommerceReceiptEntity` | one row per step, unique on **`(OperationId, StepKey)`** |
| Outbox | `RelayType` / `RelayPayload` / `RelayedAt` on the operation, swept by `CommerceRelayService` |
| `CommerceReplayGuard` | `relay:<consumer>` receipts so each event consumer advances once |

**Idempotency comes from co-committing the receipt with the mutation**, never from a separate write:

- `PlayerWalletGrain.TryDebitAsync` adds the receipt inside the same `BeginTransactionAsync` as the
  debit. The `DbUpdateException` on the unique index **is** the replay signal — the cache is rolled
  back and `Success()` is returned without republishing the event.
- `InventoryGrain.GrantFurnitureDefinitionCopiesAsync` puts the furniture rows and the receipt in one
  `SaveChangesAsync`.
- `PlayerWalletGrain.CreditOnceAsync` puts every credited currency row and the receipt in one save.

## Which flows are journalled

Not all of them, and the gap matters.

| Flow | Journal | Note |
|---|---|---|
| Catalog purchase | ✅ | the reference flow |
| Targeted offer | ✅ | |
| Marketplace list / buy / cancel / redeem | ✅ | deterministic ids on cancel and redeem |
| **Gift purchase** | ❌ | uses the journal-less overload |
| **Club subscription** | ❌ | `PlayerGrain.PurchaseClubAsync` |
| **Room ad** | ❌ | |
| **LTD raffle** | ❌ | atomic by a single `SaveChangesAsync` instead |
| **Voucher** | ❌ | claim row inserted before the grant |
| **NFT store** | ❌ | hand-rolled debit/refund, does not use the executor either |
| **Present opening** | ❌ | consume and grant are two grains with nothing between them |

## The grains

Keying **is** the concurrency model. No economy grain is `[Reentrant]`.

| Grain | Key | Serializes |
|---|---|---|
| `CatalogPurchaseGrain` | player id | one purchase per player at a time |
| `PlayerWalletGrain` | player id | balance mutation |
| `InventoryGrain` | player id | check-then-create for furni limits |
| `MarketplacePurchaseGrain` | acting player id | that player's listing/buying |
| `LtdRaffleGrain` | series id | concurrent buyers of one limited series |
| `VoucherGrain` | uppercased code | one redemption attempt per code |
| `NftStoreGrain` | `"global"` | shop stock |

Cross-player serialization in the marketplace is **not** a grain — it is a conditional
`ExecuteUpdateAsync` on `State == Active`. → [Marketplace](marketplace.md)

## Currencies

`PlayerWalletGrain` owns balances; `player_currencies` is authoritative and the in-memory dictionary
is a write-through cache rolled back on failure.

`currency_types` is the gate. A grant to a currency with no enabled row is a **no-op**:

```csharp
// PlayerWalletGrain.GrantCurrencyAsync
if (_currencyTypeProvider.GetCurrencyTypeByKind(kind) is not { Enabled: true })
{
    _logger.LogWarning(…);
    return false;
}
```

That is the fixed half. `CurrencyRewardRules.Validate` also refuses such a configuration at
admin-authoring time (`reward_currency_unknown` / `reward_currency_disabled`).

> **Every runtime grant call site discards the `bool`.** `PlayerAchievementGrain.ApplyLevelUpsAsync`,
> `PlayerQuestGrain.GrantRewardAsync`, `PlayerDailyTaskGrain.GrantAsync`, `VoucherGrain`,
> `PlayerVaultGrain`, `PlayerGrain.CheckAndGrantPaydayAsync` all ignore it — and
> `GrantCreditsAsync`/`GrantActivityPointsAsync` do not even return it. The silent no-op is now
> logged and admin-blocked, but for the player it is still silent.

A debit against a currency the player has no row for cannot silently succeed:
`ProcessDebitRequestAsync` reads `changedBy = 0`, which fails the `ChangedBy != Amount` invariant and
returns `InsufficientBalance`.

## The wallet composer

The grain sends it, not the caller:

```
wallet mutation → IPlayerPresenceGrain.OnCurrencyUpdateAsync → PlayerWalletModule
    → CreditBalanceEventMessageComposer ($"{amount}.0")
    | EmeraldBalanceMessageComposer | SilverBalanceMessageComposer
    | HabboActivityPointNotificationMessageComposer
```

## Domain pages

| Page | Covers |
|---|---|
| [Catalog](catalog.md) | purchase, club, gifts, LTD, vouchers, targeted offers |
| [Inventory](inventory.md) | **the view-only trap**, placement, pickup, the loader filter |
| [Marketplace](marketplace.md) | list / buy / cancel / redeem, and a known duplication issue |
| [Transactions](transactions.md) | the atomicity table and every named risk point |

## Configuration

| Key | Default | Where |
|---|---|---|
| `Vortex:Commerce:Recovery:SweepIntervalSeconds` | 30 | `CommerceRecoveryConfig` |
| `Vortex:Commerce:Recovery:RelayBatchSize` | 100 | |
| `Vortex:Commerce:Recovery:StuckAfterMinutes` | 10 | |
| `Vortex:Inventory:FurniPerFragment` | 100 | `InventoryConfig` |
| `marketplace_settings.commission_percent` | 1 | `MarketplaceSettingsProvider` |
| `marketplace_settings.offer_duration_seconds` | 259200 (3 d) | |
| `BundleDiscountRulesetSnapshot.DEFAULT_MAX_PURCHASE_SIZE` | 100 | compile-time, no override |
| `club.gift_cycle_days` / `club.streak_grace_days` / `club.kickback_percent` | 31 / 7 / 10 | `IServerConfigGrain` |

The split is deliberate: infrastructure timing in `IOptions<T>`, tunable business values in
`IServerConfigGrain` or an admin-editable table.

## Tests

Behaviour-named and unusually strong on the money paths:

- `Vortex.Rooms.Tests/Observability/WalletPurchaseExtensionsTests.cs` — 6 cases including
  `GrantCancelled_StillRefundsOnAnUncancelledToken`, `RefundFailing_DoesNotMaskTheOriginalFailure`
- `Vortex.Players.Tests/Wallet/WalletReceiptTests.cs` —
  `ADebitReplayedUnderTheSameOperation_ChargesOnce`,
  `ARefundReplayedUnderTheSameOperation_CreditsOnce`
- `Vortex.Database.Tests/Commerce/CommerceJournalTests.cs` — step idempotency,
  `ThePivotTime_IsStampedOnceAndNeverMoves`
- `Vortex.Database.Tests/Commerce/CommerceRelayTests.cs` —
  `ARedeliveredEvent_AdvancesEachConsumerOnce`
- `Vortex.Database.Tests/Catalog/CatalogPurchaseTests.cs` —
  `ThePriceOfAnUnboundedQuantity_WouldWrapNegative`
- `Vortex.Database.Tests/Commerce/CatalogGrantWindowTests.cs` —
  `AnEffectThatFailsLast_KeepsTheGoodsAndTheCharge`
- `Vortex.Database.Tests/Marketplace/MarketplaceClaimRaceTests.cs` —
  `TwoBuyersInsideTheSameWindow_LeaveOneCharged`

## Sources

- `Vortex.Primitives/Players/Wallet/{WalletPurchaseExtensions,CurrencyRewardRules}.cs`
- `Vortex.Primitives/Commerce/{CommerceOperation,ICommerceJournal,CommerceReplayGuard}.cs`
- `Vortex.Database/Commerce/{CommerceJournal,CommerceRelayService}.cs`
- `Vortex.Database/Entities/Commerce/CommerceReceiptEntity.cs` — the unique index
- `Vortex.Players/Grains/PlayerWalletGrain.cs`
- `Vortex.Players/Grains/Modules/PlayerWalletModule.cs`
