# Wireds

## Purpose

The in-room programming system: how a trigger fires, how a pile of boxes is resolved, and why the
engine is five separate components rather than one.

## Five components, deliberately distinct

`RoomWiredSystem` is the orchestrator. It holds five components, all constructed over an
`IWiredRoomHost` rather than the grain itself — so none of them can reach into `RoomLiveState`
directly.

| Component | Owns |
|---|---|
| `WiredTriggerIndex` | which boxes listen for which event type, plus a `Timed` list. **Not** a pile cache — `MarkDirty` / `RebuildAsync`, rebuilt at most once per tick |
| `WiredStackResolver` | resolves a tile's pile **live at fire time**, classifying co-located boxes into Triggers / Selectors / Conditions / Addons / Actions. Variable boxes are deliberately excluded from pile membership |
| `WiredExecutionPolicy` | the per-pile rolling allowance window, and which effects run (`All` / `FirstOnly` / `Random` / `Unseen`). Takes an injected `Random` so a draw is pinnable in tests |
| `WiredExecutionScheduler` | `PriorityQueue<(key, version), deadline>` of pending chains; a version bump on reschedule means a stale queue entry is recognised on the way out |
| `WiredCallChainGuard` | `HashSet<int>` of tiles held for the running chain (cycle prevention), a depth limit, and monotonic execution ids stamped on every room-log line |

The host boundary is `Vortex.Rooms/Wired/Engine/IWiredRoomHost.cs` (`IWiredRoomHost` / `IWiredRoomView`
/ `IWiredDiagnostics` / `IWiredRoomActions`), implemented by `RoomGrainWiredHost`. **No mutable room
collection crosses it** — `EnumerateTileFloorStack` materialises inside the turn and applies object-id
ordering there.

## Piles resolve live

There is no invalidatable stack index. `WiredStackResolver.BuildFromTileAsync` reads the tile **at
fire time**, and a delayed action is re-checked at execution time.

`WiredTriggerIndex`'s doc explains why an invalidatable index (Daybreak's approach) is unnecessary
under a single grain turn: nothing can change the room between the read and the use.

**The same-pile rule** — a trigger, its selectors, conditions, addons and actions must be on the same
tile — is therefore enforced by construction, not by cache invalidation.

## Ingress

`RoomWiredSystem.OnRoomEventAsync` is registered on `EventModule` and switches on event type:

| Event | Effect |
|---|---|
| `RoomWiredStackChangedEvent` | `_triggers.MarkDirty()` only — piles resolve live, nothing else to invalidate |
| `WiredVariableBoxChangedEvent` | add box ids to `_dirtyVariableBoxIds` |
| `PlayerLeftEvent` | drop that player's variable store, then enqueue |
| `RoomItemDetachedEvent` | drop that furni's variable store, **do not** enqueue |
| everything else | `EnqueueRoomEvent` |

`EnqueueRoomEvent` drops an event as `IGNORED` when the index is clean and nothing listens for that
type, and past `WiredMaxQueuedEvents` (512) it **refuses the newcomer** rather than evicting — which
is what preserves ordering for what was already accepted. Refusals count `QUEUE_DROP`.

### The click-user round trip

`wf_trg_click_user` is the one trigger whose behaviour the client has to be told about, because the
client changes what it sends and what it draws depending on the answer.

```text
room entry   WiredEnvironment{HasClickUserWired}      <- IRoomWired.GetClickUserStateAsync
             (false: the client opens the avatar menu itself and stops here)

click        WiredClickUser{ObjectId}    1953
             ClickCharacter{ObjectId}    3244  -> PlayerClickedPlayerEvent -> the trigger fires
             WiredClickUserResponse{Index = ObjectId, OpenMenu = !BlocksMenu}   309
```

Both messages arrive for the same click. **`WiredClickUser` deliberately raises no event** — the
trigger is already driven by `ClickCharacter`, and publishing on both would fire every click-user
box twice. It is a query: it decides the menu (`intParams[0]`, `blockMenuOpen`) and answers.
`intParams[1]` (`doNotRotate`) belongs to the look-at the client suppresses on its own side and is
still not honoured here. What Habbo's own server does with the two messages is not known.

`GetClickUserStateAsync` rebuilds the trigger registry when it is dirty rather than waiting for a
tick: it is asked on room entry and on a click, and a room whose box was never indexed would answer
"absent" — which the client caches for the whole visit.

## The tick

Only past `NextWiredBoundaryMs` (period `WiredTickMs` = 50 ms):

1. `_currentTickMs = now`
2. `ProcessFlashRevertsAsync` — revert boxes lit longer than `WiredFlashDurationMs` (500 ms)
3. one aggregated warning line if events were dropped since the last tick
4. **first run only**: `ProcessInternalVariablesAsync`
5. `ProcessVariableBoxesAsync` for dirty variable boxes; invalidate the variables snapshot
6. `_triggers.RebuildAsync` if dirty — hydrating every trigger as it indexes, so a timed one is
   pollable this same tick
7. `RunDueScheduledStackExecutionsAsync(now)` — drain chains scheduled on earlier ticks
8. **if `_triggers.IsEmpty`** — drop the whole queue as `IGNORED` and return
9. `ProcessTimedTriggersAsync` — index-based loop over `_triggers.Timed`, skipping (and marking dirty)
   any box no longer in the room; `IWiredTimedTrigger.TryConsumeDue(now)`; pile resolved live
10. `while (budget-- > 0 && queue.Count > 0)` with `budget = WiredMaxEventsPerTick` (64)
11. `RunDueScheduledStackExecutionsAsync(now)` **again** — so a zero-delay chain scheduled by this
    tick's fires runs in the same tick rather than 50 ms later

## One firing, in order

`FireTriggerWithEventAsync`. The order is the specification:

1. `trigger.MatchesEventAsync(evt)` — else return
2. build `WiredProcessingContext{Event, Stack, Trigger, Signal}`
3. if the event was player-caused, seed its player id into `ctx.Selected.SelectedPlayerIds`
4. `ctx.GetWiredSelectionSetAsync(trigger)` — resolves `TriggeredItem` / `SelectedItems` /
   `SignalItems` / `AllRoomItems` and `TriggeredUser` / `SignalUsers`
5. every `IWiredSelector` runs; results union into `ctx.SelectorPool`
6. every `IWiredAddon.MutatePolicyAsync` runs — **each in its own try/catch**, so a failing add-on
   costs only itself
7. every `IWiredCondition.PrepareAsync` runs, same isolation
8. `trigger.CanTriggerAsync(ctx)` — **before** conditions, deliberately, so the negative branch means
   "trigger fired, conditions did not hold", and so a trigger that seals the triggering user into the
   selection has done so before the conditions read it
9. `_policy.TryConsumeAllowance(stackId, ctx.Policy, now)` — **after** the add-ons have set the limit,
   **before** any branch runs. A refusal counts `EXECUTION_LIMIT`
10. `EvaluateConditions` — **every** condition is evaluated, never short-circuited, because the
    counting modes need the exact number that passed. Then reduced by `ctx.Policy.ConditionMode`:
    `All`, `AtLeastOne`, `NotAll`, `None`, `CountLessThan`, `CountExactly`, `CountMoreThan`
11. `trigger.FlashActivationStateAsync` fire-and-forget
12. `ScheduleStackExecution` — `WiredActionBranch.Select(actions, conditionsPassed)` picks the
    positive or negative branch, `_policy.ChooseActions` applies the effect mode

## Chain execution

`ExecuteStackChainAsync`: `BeforeEffects` add-on hooks once, then per action —

- honour `GetDelayMs()` by rescheduling at `now + delayMs` and returning `false` (the chain stays
  pending)
- **re-validate co-location**: `Room.HasItem(box)` and `IsOnTile(box, key.StackId)`. A failure counts
  `REVALIDATION`, writes a Warning room-log line naming the box, and skips **only that action**
- run the action inside try/catch with `RecordWiredErrorLog` + an Error room-log line on throw
- `FlushWiredContextAsync` after each action

`AfterEffects` hooks when the chain finishes.

### Outbound batching

`FlushWiredContextAsync` emits **at most three** composers from the accumulated
`WiredExecutionContext`:

| Composer | Carries |
|---|---|
| `WiredMovementsMessageComposer` | users, floor items, wall items, user directions |
| `ObjectsDataUpdateMessageComposer` | floor stuff data |
| `ItemsStateUpdateMessageComposer` | wall legacy strings |

### Pile calls pile

`ExecuteStacksAtAsync`:

1. `_callChain.HasRoomToDescend()` — counts `DEPTH` on refusal. The limit is read through a lambda so
   `RoomConfig.WiredMaxDepth` is live
2. the caller's own tile is **held for the duration**, so a pile cannot execute itself
3. per target furni, `_callChain.Enter(tileIdx)` — counts `CYCLE` on a repeat

`ExecuteCalledStackAsync` inherits the caller's selection **before** the called pile's own selectors
run, has no trigger and no event, and **deliberately does not evaluate conditions** — the positive
branch is what runs.

## Configuration vs runtime state

`Vortex.Rooms/Wired/WiredData.cs`'s class doc draws the line: everything in `WiredData` is **durable
player configuration**, round-tripped through `extra_data`'s `wired` section. Ephemeral per-tick state
must not be added there.

The worked consequence: `FurnitureWiredLogic.SetFlashStateAsync` deliberately bypasses
`FurnitureLogic.SetStateAsync` so the activation blink never touches `extra_data`. A 0.5 s periodic
would otherwise write several times a second, forever — and a room unloading mid-flash would leave the
box stuck lit.

## Configuring a box, end to end

```
1  player uses the box
   FurnitureWiredLogic.OnUseAsync → OpenEventMessageComposer{ItemId} to that player's presence grain

2  client sends Open
   OpenMessageHandler → IRoomFurni.GetWiredDataSnapshotByFloorItemIdAsync
     → LoadWiredAsync   (the editor can be opened before the wired tick has ever seen this box)
     → one of six composers picked off WiredDataSnapshot.WiredType:
       WiredFurniActionEvent / …Addon… / …Condition… / …Selector… / …Trigger… / …Variable…

3  client saves
   Update{Action|Addon|Condition|Selector|Trigger|Variable} → IRoomFurni.ApplyWiredUpdateAsync
     → FurnitureWiredLogic.ApplyWiredUpdateAsync
         normalise int params against IWiredParamRule
         prune stale stuff/variable ids
         clamp furni/player sources to the allowed sets
         rehydrate definition/type specifics
         MarkDirty(); null the cached snapshot
         publish RoomWiredStackChangedEvent for its tile

4  persistence (indirect)
   MarkDirty → ExtraData.UpdateSection(WIRED, json)
             → the item's SetAction callback → _state.DirtyItemIds
             → next room tick → RoomPersistenceGrain

5  reply  WiredSaveSuccessEventMessageComposer
```

### One message, two header ids

`WiredSetObjectVariableValue{EntityType, EntityId, VariableId, Value, Action}` is sent by the client
from **two** Wired-menu surfaces on **two** ids, byte for byte identical:

| id | client surface | modes used |
|---|---|---|
| `625` | `VariableManagementDetailView` | 0 set · 1 create · 2 delete |
| `689` | `WiredMenuInspectionTab` | 0 set · 2 delete |

Both are mapped to the same parser and land on `WiredSetObjectVariableValueMessageHandler`. Binding
only `625` — which was the state until 2026-08-26 — leaves every edit made from the Inspection tab
falling on the floor with no error anywhere.

## Limits

`RoomConfig` also implements `IWiredLimits`. **No `Vortex:Rooms` section exists in appsettings**, so
these are the compiled defaults in production.

| Limit | Value |
|---|---|
| `WiredTickMs` | 50 |
| `WiredMaxDepth` | 8 |
| `WiredMaxScheduledPerTick` | 64 |
| `WiredMaxEventsPerTick` | 64 |
| `WiredMaxQueuedEvents` | 512 |
| `WiredSelectorMaxAreaSize` | 100 |
| `WiredSelectedItemsLimit` | 20 |
| `WiredMaxIntParams` | 16 |
| `WiredNeighborhoodRadius` | 5 |
| `WiredFlashDurationMs` | 500 |
| `WiredAllowWallFurni` | true |

`WiredMaxDepth`'s XML doc is a model of how to record an unknown: it read 20 while nothing read the
property (`RoomWiredSystem` enforced a private const 8, ticket RFW-101); it was lowered to 8 so that
wiring the knob up is a non-behaviour-change; and **Habbo's own limit is recorded as `UNKNOWN`**, to be
settled by watching `Vortex.wired.chain.stopped{reason=depth}`.

## Diagnostics

Stop reasons counted on `Vortex.wired.chain.stopped`: `DEPTH`, `CYCLE`, `REVALIDATION`,
`EXECUTION_LIMIT`, `QUEUE_DROP`, `IGNORED`. Plus `Vortex.wired.event` and
`Vortex.wired.index.rebuilt`.

Room logs go through `RoomWiredLogChannel` → `RoomWiredLogWriterService`, a bounded channel drained by
a background consumer in batches of 100, with a final drain on shutdown — so logging never sits on the
wired hot path.

## Coverage

**169 unique `[RoomObjectLogic]` keys** exist under `Object/Logic/Furniture/Floor/Wired/`:
28 trigger attributes over 31 files, 48 condition over 43, 49 action over 48, 20 selector over 21,
17 addon over 20, 7 variable over 8. (Attribute counts differ from file counts because the attribute
is `AllowMultiple` and some files are base classes.)

> There is **no test or registry in-repo that compares this list against the client's box-type list**,
> so "169 of N" cannot be stated from the repository alone.

## Tests

`Vortex.Rooms.Tests/Wired/` — driven on `FakeWiredRoomHost`, no grain required.

| File | Proves |
|---|---|
| `WiredParityTests.cs` | the eight behavioural rules end to end: zero-delay chains run in the same drain; a pile's zero-delay actions run in object-id order; **a delay is measured on the room clock, not counted in ticks**; a late tick runs a delayed action once and only once; a delayed action that leaves the pile does not fire *and says so* (asserts both `REVALIDATION` and a room-log line); a ghost trigger is skipped and the index self-repairs; past the queue cap the newcomer is refused and ordering holds |
| `WiredExecutionPolicyTests.cs` | 11 cases — the rolling allowance window (explicitly pinning Vortex's choice where Habbo is `UNKNOWN`, OQ-6), per-pile isolation, all four effect modes including the random draw's avoidance window |
| `WiredCallChainGuardTests.cs` | 9 cases — cycle refusal, hold release including on a refused entry, depth read live, execution-id parent/child stamping |
| `WiredStackResolverTests.cs` | 8 cases — bucket classification, variable boxes excluded, object-id ordering, one unhydratable box costing only itself |
| `WiredPersistenceRoundTripTests.cs` | configure → unload → reload preserves config; persisting wired does not clobber other `extra_data` sections |
| `WiredEngineCostTests.cs` | a steady tick costs the same regardless of room size |

## Known unknowns

> ### Server-side wired authorization gap
>
> `RoomActionModule.ApplyWiredUpdateAsync` performs **no controller-level check** — unlike its two
> neighbours in the same file, `PlaceFloorItemAsync` and `MoveFloorItemByIdAsync`, which both call
> `SecurityModule.CanManipulateFurniAsync(ctx)`. Neither `RoomGrain.ApplyWiredUpdateAsync` nor
> `UpdateActionMessageHandler` (or its five siblings) checks either. The same is true of
> `GetWiredDataSnapshotByFloorItemIdAsync` behind `OpenMessageHandler`.
>
> The only gate found is **client-side**: `WiredPermissionsEventMessageComposer{CanModify, CanRead}`,
> computed from `controllerLevel >= Rights` at room entry.
>
> Separately, `rooms.wired_modify_permission_mask` and `wired_read_permission_mask` are persisted and
> echoed back by `RoomGrain.Wired.RoomSettings.cs` but are read by **no authorization decision
> anywhere** (verified by repo-wide grep — only the entity and that one file mention them).
>
> - **Inspected:** `RoomActionModule.Floor.cs` and its neighbours, `RoomGrain.ApplyWiredUpdateAsync`,
>   all six update handlers, `OpenMessageHandler`, the two mask columns and every reference to them.
> - **Why unresolved:** nothing records this as deliberate, and it was not verified against a running
>   server. `ctx.RoomId` is set for any occupant, so a non-rights player standing in a room can reach
>   `IRoomFurni`.
> - **What would resolve it:** a decision on intent, then either a `CanManipulateFurniAsync` call or a
>   recorded note that the client gate is considered sufficient.

Also open, and recorded as unknown in the code itself: the wired chain depth (OQ-1) and the
rolling-vs-fixed execution window (OQ-6). Both are Vortex choices where Habbo's behaviour is unknown.

## Sources

- `Vortex.Rooms/Grains/Systems/RoomWiredSystem.cs`
- `Vortex.Rooms/Wired/Engine/{WiredTriggerIndex,WiredStackResolver,WiredExecutionPolicy,WiredExecutionScheduler,WiredCallChainGuard,IWiredRoomHost,RoomGrainWiredHost}.cs`
- `Vortex.Rooms/Wired/WiredData.cs`, `WiredContext.cs`
- `Vortex.Rooms/Object/Logic/Furniture/Floor/Wired/FurnitureWiredLogic.cs`
- `Vortex.Rooms/Grains/Modules/RoomActionModule.Floor.cs`
- `Vortex.PacketHandlers/UserDefinedRoomEvents/*.cs`
- `Vortex.Primitives/Rooms/Wired/*.cs`
- `Vortex.Rooms/Configuration/RoomConfig.cs`
- `Vortex.Rooms.Tests/Wired/*.cs`
- `docs/walkthroughs/add-a-wired-box.md`
