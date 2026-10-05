# State ownership and persistence

## Purpose

The most consequential page in this documentation. **A table in MySQL is not the owner of the value
in it.** This page says who owns what, and what a raw SQL edit actually does.

## The rule

> Grains hydrate from MySQL on activation and hold authoritative mutable state in plain in-memory
> fields. There is no Orleans store to inspect and no `[PersistentState]` anywhere. A raw `UPDATE`
> against grain-owned data while its grain is activated is **overwritten by the grain's in-memory
> snapshot**, usually within seconds and silently.

`Vortex:Orleans:GrainCollectionAge` is **2 minutes**. That is the window in which a player who "just
logged off" still has an activated grain holding their row hostage.

## Three persistence shapes

| Shape | Meaning | Grains |
|---|---|---|
| **Serialization point** | owns no state; opens a short-lived `DbContext` per call | `GroupGrain`, `CatalogPurchaseGrain`, `MarketplacePurchaseGrain`, `PlayerBadgeGrain`, ~20 more |
| **Hydrate + write-through** | live cache authoritative while active, written per mutation | `PlayerGrain`, `PlayerWalletGrain`, `ServerConfigGrain`, `PlayerNavigatorGrain`, `LtdRaffleGrain`, `RentableSpaceGrain` |
| **Queue + timer flush** | memory-first, batched to DB, drained on deactivate | `RoomPersistenceGrain`, `PlayerAchievementGrain`, `MessengerGrain` (delivered flags), `RoomPetSystem` |

Only the first is safe to edit in SQL behind the grain's back — and even then the *client* may be
holding a stale view.

## The ownership table

**Hazard** = what a raw `UPDATE`/`DELETE` does while the owner is activated.

| State | Live owner | Table(s) | Correct path | Hazard |
|---|---|---|---|---|
| Furniture position, rotation, `extra_data` (incl. all wired config), room/owner assignment | `RoomGrain` → `RoomPersistenceGrain` | `furniture` | `RoomGrain` place/move/pickup | 🔴 **silently lost** — the flush force-marks 8 columns modified every ≤2 s and on deactivate |
| Player profile: name, motto, figure, gender, achievement score, respect counters | `PlayerGrain._state` | `players` | `PlayerGrain` methods | 🔴 **silently lost** — `WriteToDatabaseAsync` `ExecuteUpdate`s all 8 columns from memory on every mutation and on deactivate |
| Pet stats and position | `RoomPetSystem` | `pets` | pet system APIs | 🔴 **silently lost** — overwrites 15 columns every ≤60 s |
| Wallet balances | DB authoritative; grain caches | `player_currencies` | `PlayerWalletGrain` | 🟡 honoured (the row is re-read inside the transaction), but the client shows the cached number until reactivation |
| Player inventory (unplaced furni) | `InventoryGrain` load-once cache | `furniture` where `room_id IS NULL` | `InventoryGrain` | 🟡 survives, invisible until `ReloadAsync` or deactivation |
| Room settings, tags, rating | `RoomGrain._state.RoomSnapshot` | `rooms` | `RoomGrain.*Settings*` (write-through) | 🟡 survives; room and navigator serve old values until reactivation |
| Room rights, bans, mutes | `RoomLiveState.PlayerIdsWithRights`, `MuteExpiresUtc` | `room_rights`, `room_bans`, `room_mutes` | `RoomGrain.AssignRightsAsync`, `RoomModerationStore` | 🟡 **grants nothing live** until reactivation — `AGENTS.md` records this exact class as a shipped bug |
| Guild ranks, `admin_only_decoration` | `RoomLiveState.GroupMemberRanks` + `GroupGrain` | `group_members`, `groups` | `GroupGrain` | 🟡 live authorization does not see it |
| Server config KV | `ServerConfigGrain._cache` (write-through, single activation) | `server_config` | `SetValueAsync` | 🟡 invisible until `ReloadAsync()` or restart |
| Catalog, navigator, furniture definitions, currency types, room models, pet data, marketplace settings, badge parts, account levels | 14 `IReferenceDataProvider` singletons | respective tables | admin service → write → `ReloadAsync` | 🟡 survives, invisible until a reload |
| Commerce operations and receipts | none — the table **is** the state | `commerce_operations`, `commerce_receipts` | `CommerceJournal` | 🔴 editing breaks idempotence: `(operation_id, step_key)` uniqueness *is* the replay guard |
| Marketplace offer lifecycle | none cached; the conditional `ExecuteUpdate` on `state` is the pivot | `marketplace_offers` | `MarketplacePurchaseGrain` | 🔴 flipping `state` by hand can double-sell or strand an item |
| Permanent wired variables | `RoomGrain` — **write-through, zero loss window** | `wired_permanent_variables` | grain | 🟢 survives |
| Tile heights | `RoomGrain`, derived from the item stack | **no table** | n/a | 🟢 nothing to reconcile |
| Audit / economy ledger / item events | `AuditWriterService` channel | `audit_events`, `economy_ledger`, `item_events` | never mutate | append-only by contract |
| Live room population | `RoomDirectoryGrain` — pure memory, `[KeepAlive]`, **no DB at all** | `rooms.users_now` | none | ⚫ **the column is dead** (below) |
| Online/offline | `PlayerPresenceGrain` memory | `players.status` | none | ⚫ **dead column** (below) |
| Orleans stream pub/sub | Orleans runtime | none by default | n/a | `"memory"` loses in-flight stream messages on restart |

### Two dead columns

Both are written once at creation and **never updated at runtime**, yet both are read:

- **`rooms.users_now`** — written as `0` at room creation and never again. `NavigatorProvider`'s
  `OrderByPopularity` sorts on it (so rooms sort by a constant, and `score` then `id` is the effective
  order) and the dashboard directory displays it. `NavigatorService.ToSearchResults` overlays the
  real population from `RoomDirectoryGrain`, so the client sees the truth — the sort does not.
- **`players.status`** — never written after creation, and `DashboardApiService.Directory.cs` reads it
  verbatim, so a dashboard profile always says "Offline".

Whether the popularity ordering is acceptable is a product call, not a code reading. Recorded here so
nobody debugs it twice.

## Loss windows

Memory-first persistence buys responsiveness and costs a window. All four are deliberate.

| Domain | Window | Knob |
|---|---|---|
| Furniture position/rotation/extra data | ≤ 2000 ms, plus `ceil(N / 100) × 2 s` backlog tail | `RoomConfig.DirtyItemsTickMs`, `MaxDirtyItemsPerFlush` |
| Pet stats and position | ≤ 60 s | `PetConfig.StatFlushIntervalMs` |
| Permanent wired variables | zero (write-through) | — |
| Commerce post-pivot | zero (journalled + receipted) | — |

## The reference flush pattern

`Vortex.Rooms/Grains/RoomPersistenceGrain.cs` is the pattern `AGENTS.md` points at. Worth reading in
full before writing another one.

**Queue.** `_dirtyItems: Dictionary<long, RoomItemSnapshot>` keyed by object id — last write per item
wins, so a sofa dragged ten times is one row — plus `_removedItemIds: HashSet<RoomObjectId>`.
`EnqueueDirtyItemAsync` and `EnqueueDirtyItemsAsync` both return `Task.CompletedTask` synchronously:
**enqueueing never blocks the caller's grain turn on I/O.**

**Timer.** One `RegisterGrainTimer` at `DirtyItemsTickMs` (2000 ms) with a `static` callback closing
over `this` as the state object.

**Flush.** `FlushDirtyItemsAsync`:

- early-out on an empty queue
- take at most `MaxDirtyItemsPerFlush` (100)
- write via `dbCtx.Attach(stub)` + explicit `e.Property(x => …).IsModified = true` on exactly the
  columns that changed — a targeted `UPDATE`, no read-modify-write
- a removed item gets `RoomEntityId = null` (back to inventory)
- **entries leave the queue only after `SaveChangesAsync` succeeds**

That last point is the whole lesson. The comment records the bug it fixed: taking the batch off the
queue up front meant one connection blip lost a room's layout permanently.

A correctness precondition is stated in the same comment: *"The grain is not reentrant, so nothing can
enqueue during the await — the queue this returns to is the one it left."*

**Drain on deactivate.** `OnDeactivateAsync` loops rather than flushing once, bounded by **progress,
not by a count**:

```csharp
while (…) { int before = _dirtyItems.Count; await FlushDirtyItemsAsync(); if (_dirtyItems.Count >= before) break; }
```

Against a dead database that stops after one unproductive pass instead of spinning.

Pinned by `Vortex.Rooms.Tests/Grains/RoomPersistenceLossWindowTests.cs` —
`AFailedFlushKeepsItsBatchForTheNextOne`, `DeactivationDrainsEveryPendingBatch`,
`DeactivationAgainstADeadDatabaseStopsAfterOnePass`.

**The room's half.** `RoomGrain.FlushDirtyItemsAsync` runs as the last tick step and on deactivate: it
materialises snapshots from `_state.DirtyItemIds`, **clears the set before the cross-grain call**, and
hands the batch over.

> `RoomGrain.FlushDirtyTilesAsync` is not persistence at all despite the name — it batches height-map
> deltas into composers. Tile heights are derived and never stored.

## Keeping live authorization in sync

A DB write for a permission change is not the feature. If in-memory grain state is read for an
authorization decision, it must be updated in the **same method** that persists the change, and
hydrated on activation.

The canonical past bug: `RoomLiveState.PlayerIdsWithRights` was checked by `GetControllerLevelAsync`
but never populated on hydration nor updated by `AssignRightsAsync`. Rights persisted, showed in the
UI list, and granted nothing. Build and tests stayed green.

It is closed at all five sites today — hydration plus four mutation methods — and there is a second
half most people miss: `RoomSecurityModule.RefreshControllerLevelForPlayerAsync` also **re-stamps the
avatar's `AvatarStatusType.FlatControl`**, because the presence notification only redraws the
subject's own UI. Without it the rights star appeared only after a rejoin.

→ [Rooms: security and rights](../04-rooms/room-architecture.md)

## Data fixes

`scripts/sql/` holds six one-off scripts. The convention, readable from the files:

- a comment header stating intent
- idempotent and additive (`NOT EXISTS` guards, `INSERT … SELECT`)
- **reference tables only** — `catalog_pages`, `catalog_offers`, `furniture_definitions`,
  `cfh_topics`, `role_permissions`

That last constraint is what makes them safe: those are exactly the tables no grain holds live state
for, only singleton snapshots. **A script against them still needs its `ReloadAsync`** — otherwise the
row changes and the hotel does not.

Never point a script at anything marked 🔴 in the table above.

## Known unknowns

- **Unknown:** whether `players.status` and `rooms.users_now` should be maintained or dropped.
  - Inspected: every write site (creation only) and every read site (navigator ordering, dashboard).
  - Why unresolved: it is a product decision about whether popularity ordering matters.
  - What would resolve it: a decision, then either a writer or a migration.

## Sources

- `Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs` — the `PLAYER_STORE`/`ROOM_STORE` comment
- `Vortex.Rooms/Grains/RoomPersistenceGrain.cs`, `RoomGrain.Furni.cs`, `RoomGrain.Map.cs`
- `Vortex.Rooms.Tests/Grains/RoomPersistenceLossWindowTests.cs`
- `Vortex.Players/Grains/PlayerGrain.cs` — `WriteToDatabaseAsync`, `ApplyHotelMuteAsync`
- `Vortex.Players/Grains/PlayerWalletGrain.cs` — `ProcessDebitRequestAsync`, `RollbackUpdatesAsync`
- `Vortex.Players/Grains/ServerConfigGrain.cs`
- `Vortex.Inventory/Grains/Modules/InventoryFurniModule.cs`
- `Vortex.Rooms/Grains/RoomDirectoryGrain.cs`
- `Vortex.Navigator/NavigatorProvider.cs`, `NavigatorService.cs`
- `Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml`
- `scripts/sql/*.sql`
- `AGENTS.md` — "Keep live authorization state synced with DB writes"
