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

## Design

### 1. Vault grain (`Vortex.Collectibles/Grains/PlayerVaultGrain.cs`)

- **One row per deposit.** `AddIncomeRewardAsync` stops merging into an existing row; every deposit
  inserts an immutable row. The client already sums rows per category
  (`EarningsView.onIncomeRewardDataReceived`).
- **Idempotent per-row claim.** For each claimed row: `IPlayerWalletGrain.CreditOnceAsync(...)` with
  a step key `vault-row-{id}`, then delete that row. A crash between credit and delete is recovered
  by the next claim: the credit is a no-op, the row is deleted. No loss, no double payment.
- **Claim All.** The parser reads the byte signed (`(sbyte)`); `VaultRewardCategoryType.All = -1`
  claims every category. The response echoes `-1`, which the client handles (all rows zeroed, all
  buttons disabled).
- **Product rows.** None are produced in this slice. If one exists, the claim leaves it in place
  instead of deleting it unpaid.
- **Notification.** After a successful deposit, send `IncomeRewardNotificationMessageComposer
  { RewardCategory }` via `PlayerPresenceGrain.SendComposerAsync`. Offline player: nothing is sent;
  the status is read on the next window open.
- **Enum.** Add `Games = 11`, `WiredChest = 12`, `Agency = 13` (from `EarningsView.windowProcedure`).

### 2. Achievements (`Vortex.Progression/Grains/PlayerAchievementGrain.cs`, level-up payout)

- A level-up reward in **credits or duckets** is deposited with
  `AddIncomeRewardAsync(Achievements, Credits|Duckets, amount)` instead of `GrantCurrencyAsync`.
- Any other currency (diamonds, seasonal points…) stays a direct grant: the vault wire only knows
  reward types `0 = duckets`, `1 = credits`.
- The deposit stays inside the existing `RecordOwedPayoutAsync` operation, so the commerce journal
  still covers it.

### 3. HC payday (`Vortex.Players/Grains/PlayerGrain.cs`, `CheckAndGrantPaydayAsync`)

- `GrantCreditsAsync(totalReward)` becomes `AddIncomeRewardAsync(HabboClub, Credits, totalReward)`.
- `ClubPaydayEvent` and `KickbackTotalRewarded` are unchanged: the payday is earned when deposited,
  and cashed when claimed.

## Error handling

- The deposit replaces the direct grant at the same point in each caller, so each caller keeps its
  current failure semantics: achievements through the commerce journal, payday as before (a deposit
  followed by a failed `UpsertKickbackAsync` re-deposits on the next check — the same exposure the
  direct grant has today, not widened by this change).
- A claim that fails mid-way leaves the unpaid rows in place; the response is `false` for that
  category and the client re-enables its button.

## Testing

- Wire: Claim All round trip — parser yields `All` from `0xFF`, response serializes `-1`.
- Grain: a replayed claim after a simulated failure credits once.
- Grain: product row survives a claim.
- Achievements: a duckets reward lands in the vault; a diamonds reward stays direct.
- Payday: deposits into the vault instead of the wallet.
- Notification is sent after a deposit.
