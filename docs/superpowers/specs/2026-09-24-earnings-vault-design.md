# Earnings vault — slice 1 design

Date: 2026-09-24

## Problem

The client's Earnings window (`habboUI/open/vault`, `EarningsController.as` / `EarningsView.as`) is
fully wired on our side — status, claim, claim response and notification serializers are mapped and
verified — but it always shows 0: nothing in the solution ever calls
`PlayerVaultGrain.AddIncomeRewardAsync`. On top of that, three defects sit in the existing path:

1. **Claim All never works.** The client sends category `-1` as one byte (`_SafeCls_2320`,
   `new Byte(param1)`). `IncomeRewardClaimMessageParser` reads it with `PopByte()` (unsigned) as enum
   value `255`, the grain finds no rows for it and answers `false`; the client (which reads the
   response byte signed, so `-1`) re-enables the button. Nothing is ever claimed.
2. **Claim is not idempotent.** `ClaimCategoryAsync` pays with plain `GrantCreditsAsync` /
   `GrantActivityPointsAsync`, then deletes rows. A failure between the two pays twice on retry.
   Rows with a `ProductCode` are deleted without anything being granted.
3. **The notification is never sent**, and the enum lacks the client's categories 11–13.

## Scope

In:

- Make the vault reliable (defects 1–3).
- Route **Achievements** (category 2) and **HC payday** (category 4) payouts through the vault.

Out (separate slices): Daily gift, Donations, Bonus bag, Games, Agency (the features do not exist);
routing Marketplace (already has its own claim via `CreditsOwed` — a second claim path risks double
payment), Surprise boxes (products), Wired chest withdrawals, Level progression.

## Design principle

A payout source knows **what** it pays and **why** — never how the Earnings window works. Adding a
future category (daily gift, donations, marketplace…) must be a one-line change in that source, with
no knowledge of the wire, of vault reward types, or of which currencies the vault can hold. Every
vault-specific rule lives in exactly one place:

| Rule | Lives in |
|---|---|
| Which currencies the Earnings window can hold, and their wire type | `VaultRewardRouting` |
| Whether a category goes through the vault or is paid directly (operator's choice) | `earnings.<category>.to_vault` config keys, read by `EarningsRoutingPolicy` |
| A currency it cannot hold, or a category routed direct, is paid straight to the wallet | `PlayerVaultGrain.EarnAsync` |
| A deposit is recorded once per source event | `source_key` unique index |
| How a claimed row turns into a payout | one `IVaultRewardSettler` per reward type |
| Telling the client something changed | `PlayerVaultGrain`, after a deposit |

## Design

### 1. The single entry point — `IPlayerVaultGrain.EarnAsync`

```csharp
Task EarnAsync(IncomeEarning earning, CancellationToken ct);

[GenerateSerializer, Immutable]
public sealed record IncomeEarning
{
    [Id(0)] public required VaultRewardCategoryType Category { get; init; }
    [Id(1)] public required CurrencyKind Currency { get; init; }
    [Id(2)] public required int Amount { get; init; }
    [Id(3)] public required string SourceKey { get; init; }   // e.g. "club-payday:2026-09-01"
}
```

It replaces `AddIncomeRewardAsync` (which has no caller). Sources speak `CurrencyKind`, the wallet's
own vocabulary — not `VaultRewardType`.

- **Routing.** `VaultRewardRouting.TryGetRewardType(CurrencyKind, out VaultRewardType)` — a pure
  static mapping: credits → `Credits (1)`, activity points type 0 → `Duckets (0)`, anything else →
  none. When there is none, `EarnAsync` pays the currency straight to the wallet through
  `IPlayerWalletGrain.CreditOnceAsync`, keyed on the same `SourceKey`, so the caller never branches
  on currency and the direct path is exactly as idempotent as the vault path.
- **Operation ids.** Every wallet credit the vault makes uses a deterministic `CommerceOperationId`
  (name-based UUID of `vault:{playerId}:{key}`), so a retry maps onto the same receipt.
- **Idempotent deposits.** New column `source_key` with a unique index on `(player_id, source_key)`.
  A second deposit with the same key is a no-op. This closes the payday re-deposit exposure (a
  deposit followed by a failed `UpsertKickbackAsync` no longer pays twice) and makes every future
  source safely retryable. Migration adds `source_key` and `claimed_at`, backfilling existing rows with `legacy:{id}`.
- **One row per deposit.** No merging into an existing row: rows are immutable once written. The
  client already sums rows per category (`EarningsView.onIncomeRewardDataReceived`).
- **Notification.** After an inserted (not deduplicated) deposit, send
  `IncomeRewardNotificationMessageComposer { RewardCategory }` via
  `PlayerPresenceGrain.SendComposerAsync`. Offline: nothing is sent; the status is read on the next
  window open.

### 1b. Routing policy — operator-editable, per category

Whether a category's earnings wait in the vault or land in the wallet immediately is a hotel choice,
not a code constant: an operator may want paydays auto-credited again, or a future category (daily
gift) paid directly during an event. So the choice is a runtime setting, the same mechanism as every
other tunable (`IServerConfigGrain` + `ConfigKeyCatalog`, which already feeds the dashboard's
server-config editor — no new dashboard page).

- One `Bool` key per category: `earnings.achievements.to_vault`, `earnings.habbo_club.to_vault`
  (group **Earnings**), defaults `true`. Constants live in `EarningsConfig` (one class, like
  `ClubConfig`).
- `EarningsRoutingPolicy` maps a category to its key and reads it live from `IServerConfigGrain`.
  `EarnAsync` asks it first: `false` → the direct wallet path (same `CreditOnceAsync` + `SourceKey`
  as the non-vault-currency path), no row, no notification.
- **A key is declared by the slice that wires its source, never ahead of it.** A switch for a
  category nothing pays into (daily gift, donations today) would be offered in the dashboard, flipped,
  saved — and do nothing. The mechanism covers every category now; the catalogue lists only the live
  ones. Wiring a new source = one `EarnAsync` call + one catalogue entry + one `EarningsConfig`
  constant.
- Flipping a key to `false` affects new earnings only. Rows already in the vault stay claimable —
  nothing already earned is moved or lost.

### 2. Claim — settlers per reward type

```csharp
public interface IVaultRewardSettler
{
    IReadOnlySet<VaultRewardType> Handles { get; }
    // Returns = paid (now or by an earlier crashed claim); throws = not paid, row stays.
    Task SettleAsync(int playerId, VaultIncomeRow row, CancellationToken ct);
}
```

- `ClaimAsync(category)` loads the matching rows (`All = -1` → every category), finds the settler for
  each row's reward type, settles, and marks the row claimed (`claimed_at`) **only if it settled**.
- **Rows are never deleted.** A deleted row takes its `source_key` with it, and a replayed deposit
  (a retried payday) would then insert it again and pay twice. Marking keeps the unique index
  authoritative forever and leaves a per-player earnings history. The status packet reads unclaimed
  rows only.
- `CurrencyVaultRewardSettler` handles `Credits` and `Duckets`, paying with
  `IPlayerWalletGrain.CreditOnceAsync` under operation `Deterministic(VaultIncome, rowId)` and step
  `vault-claim`. A crash between credit and mark is recovered by the next claim: the credit is a
  no-op, the row is marked. No loss, no double payment.
- A row with no registered settler (`Product`, in this slice) stays in place. The slice that first
  deposits products (surprise boxes, bonus bag) adds a `ProductVaultRewardSettler` and registers it —
  the grain does not change.
- Settlers are registered in DI and injected as `IEnumerable<IVaultRewardSettler>`; a startup check
  fails if two settlers claim the same reward type.
- Result: `true` if at least one row settled and none failed; the response echoes the requested
  category (including `-1`), which the client already handles.

### 3. Wire fixes

- `IncomeRewardClaimMessageParser` reads the category byte **signed** (`(sbyte)packet.PopByte()`), so
  Claim All arrives as `VaultRewardCategoryType.All = -1`.
- `IncomeRewardClaimResponseMessageComposerSerializer` writes `(byte)(sbyte)category`, i.e. `0xFF`
  for `All`, which the client reads back as `-1`.
- Enum gains `All = -1`, `Games = 11`, `WiredChest = 12`, `Agency = 13`
  (`EarningsView.windowProcedure`).

### 4. Sources

**Achievements** (`PlayerAchievementGrain`, level-up payout inside `RecordOwedPayoutAsync`):

```csharp
await vault.EarnAsync(new IncomeEarning {
    Category = VaultRewardCategoryType.Achievements,
    Currency = CurrencyRewardRules.KindFor(levelUp.RewardType),
    Amount = levelUp.RewardAmount,
    SourceKey = $"achievement:{definition.Id}:level:{levelUp.Level}",
}, innerCt);
```

No currency branch: diamonds and other currencies are paid directly by `EarnAsync`. The commerce
journal still wraps the call.

**HC payday** (`PlayerGrain.CheckAndGrantPaydayAsync`): `GrantCreditsAsync(totalReward)` becomes
`EarnAsync(HabboClub, credits, totalReward, $"club-payday:{payday:yyyy-MM-dd}")`. `ClubPaydayEvent`
and `KickbackTotalRewarded` are unchanged: the payday is earned when deposited, cashed when claimed.

### 5. Files (one type per file)

| New | Project |
|---|---|
| `IncomeEarning` | `Vortex.Primitives/Vault` |
| `VaultRewardRouting` | `Vortex.Primitives/Vault` |
| `IVaultRewardSettler`, `VaultIncomeRow` | `Vortex.Collectibles/Vault` |
| `CurrencyVaultRewardSettler` | `Vortex.Collectibles/Vault` |
| `EarningsConfig` (keys + defaults) | `Vortex.Collectibles/Configuration` |
| `EarningsRoutingPolicy` | `Vortex.Collectibles/Vault` |
| EF migration `AddVaultIncomeSourceKey` | `Vortex.Database` |

Changed: `IPlayerVaultGrain`, `PlayerVaultGrain`, `PlayerVaultIncomeRewardEntity`,
`VaultRewardCategoryType`, `ConfigKeyCatalog` (two entries, group Earnings), the claim parser and response serializer, `PlayerAchievementGrain`,
`PlayerGrain`, the Collectibles DI registration.

## Error handling

- Deposit failure throws to the source, which keeps its current failure semantics (achievements:
  commerce journal; payday: retried on the next check, now safe thanks to `source_key`).
- A settler failure leaves its row in place; other rows in the same claim still settle; the response
  is `false` and the client re-enables the button.

## Testing

- `VaultRewardRouting`: credits → 1, duckets → 0, diamonds → none.
- Wire: Claim All round trip — parser yields `All` from `0xFF`, response serializes `0xFF`.
- Grain: same `SourceKey` twice → one row, one notification.
- Grain: non-vault currency → direct wallet grant, no row.
- Grain: category routed `to_vault = false` → direct wallet grant, no row, no notification; rows
  deposited before the flip are still claimable.
- Catalogue: every `EarningsRoutingPolicy` key is declared in `ConfigKeyCatalog` with the same
  default. `EarningsRoutingPolicy` throws for a category that has no key, so a source wired without
  its switch fails its own test instead of silently defaulting.
- Grain: replayed claim after a failure between credit and mark → credited once.
- Grain: a deposit replayed AFTER its row was claimed → no new row.
- Grain: a row with no settler survives a claim.
- Achievements: a duckets level-up deposits with the expected source key.
- Payday: deposits into the vault; a repeated check for the same payday does not deposit twice.

