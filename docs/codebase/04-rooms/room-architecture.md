# Room architecture

## Purpose

How one grain holds an entire live room, how it is decomposed, and where the authorization rules live.

## One grain, 15 interfaces, 26 collaborators

`RoomGrain` is a single `sealed partial class` spread over **27 files**, implementing `IRoomGrain` and
its 14 facets. Every facet resolves to the **same activation** for a room id, so requesting a narrow
one is free — it is the same grain through a narrower contract.

Prefer the narrowest facet a call site needs:

```csharp
_grainFactory.GetRoomAvatars(roomId)     // not GetRoomGrain
_grainFactory.GetRoomFurni(roomId)
_grainFactory.GetRoomSecurity(roomId)
```

The accessors are 14 concrete methods in `GrainFactoryExtensions`, not a generic
`GetRoom<TFacet>(roomId)`. The facet table is on [Grain map](../03-orleans/grain-map.md).

`RoomService.EnterRoomAsync` takes the **aggregate** `GetRoomGrain`, with an in-code justification
that this is the one flow that legitimately spans the whole room.

### Composition

All 26 collaborators are constructed in the `RoomGrain` constructor with `this` passed in. **None is a
grain** — they are plain objects holding a `RoomGrain` field, so every call stays inside the single
turn.

- **8 modules** (`Grains/Modules/`) own state and the operations on it: `RoomPathingSystem`,
  `RoomEventModule`, `RoomSecurityModule`, `RoomMapModule`, `RoomObjectModule`, `RoomAvatarModule`,
  `RoomHandItemModule`, `RoomFurniModule`, `RoomActionModule`.
- **18 systems** (`Grains/Systems/`) run behaviour: avatar tick, pets, bots, rollers, wired, chat,
  game chrome, game coordinator, freeze, banzai, game timer, scoreboards, moderation, mystery box,
  crackable, trading, wired trading, pathing.

**Construction order is load-bearing and commented**: `GameChrome` before `GameSystem` before any
game, because `RoomFreezeSystem`'s field initialiser reads `GameSystem.TeamState`.

### Two registration seams, both in the constructor and nowhere else

```csharp
GameSystem.Register(FreezeSystem);        // IRoomMinigame
GameSystem.Register(BanzaiSystem);

EventModule.Register(RollerSystem);       // IRoomEventListener
EventModule.Register(WiredSystem);
EventModule.Register(ScoreboardSystem);
```

A game that is never registered builds clean, tests clean, and never runs. That is why
`RoomMinigameCoordinationTests` asserts `Freeze_Is_Registered_On_Every_Room` and the Banzai
equivalent.

`RoomEventModule.PublishAsync` awaits its three listeners **sequentially in registration order** —
see *Known unknowns*.

### The capability layer

`RoomGrain.Capabilities.cs` implements 8 **non-grain** interfaces as explicit implementations:
`IRoomLookup`, `IRoomMapAccess`, `IRoomGameAccess`, `IRoomFreezeAccess`, `IRoomChestAccess`,
`IRoomTransactionAccess`, `IRoomBanzaiAccess`, `IRoomFurniAccess`. None derives from a grain
interface, so Orleans generates no proxy — these are the in-turn capabilities handed to furniture
logic.

## RoomLiveState

`Grains/RoomLiveState.cs` — 103 lines holding the entire live room:

| Group | Fields |
|---|---|
| Identity | `RoomSnapshot`, `RoomProperties`, `OwnerNamesById` |
| Objects | `ItemsById`, `ItemIndex`, `AvatarsByObjectId`, `AvatarsByPlayerId`, `PetsById` |
| Map | `Model` + six parallel tile arrays (`TileHeights`, `TileEncodedHeights`, `TileFlags`, `TileHighestFloorItems`, `TileFloorStacks`, `TileAvatarStacks`) |
| Authorization | `PlayerIdsWithRights`, `GroupMemberRanks`, `GroupAdminOnlyDecoration` |
| Moderation | `MuteExpiresUtc` (room mutes), `HotelMuteExpiresUtc` (staff mutes) — **deliberately separate so a room owner cannot lift a staff sanction** |
| Sessions | `PendingDoorbellRingersMs`, `TradeSessionsByPlayerId`, `MysteryBoxSessionsByOwnerId` |
| Dirty sets | `DirtyHeightTileIds`, `DirtyItemIds`, `DirtyFloorItemIds`, `DirtyWallItemIds` |
| Wired | `AllVariablesHash`, `WiredErrorLogCounters` |
| Clocks | `EpochMs` + five per-subsystem boundaries |

All of it is lost on deactivation except what the dirty sets flush.
→ [Persistence](../03-orleans/persistence.md)

### RoomItemIndex

`Dictionary<Type, HashSet<IRoomItem>>` keyed by the attached logic's concrete type **and every base
class**, so `ItemsOf<FamilyBaseLogic>()` finds a whole family without a scan of `ItemsById`.

Its invariant — attach on `AttatchLogicAsync`, detach on removal — is maintained by convention at four
call sites: `RoomObjectModule.AttatchLogicAsync`, `RoomObjectModule.RemoveObjectAsync`,
`RoomGrain.Furni.Edit.cs`, `RoomPetSystem.Motion`.

## Discovery and lifecycle

`RoomDirectoryGrain` (`"global"`, `[KeepAlive]`) holds `_activeRooms`, `_roomPlayers` and
`_roomPopulations` — **pure memory, no DB at all**.

`CheckRoomsAsync` runs every `RoomCheckMs` (300 000 ms): populated rooms get
`DelayRoomDeactivationAsync()` (which calls `DelayDeactivation(30 min)`), empty rooms get
`DeactivateRoomAsync()`. All fanned out under one `Task.WhenAll`.

`RemoveActiveRoomAsync` drops all three maps together, with a comment naming `[KeepAlive]` as the
reason a leak there is permanent.

## Security and rights

`RoomSecurityPolicy.ResolveControllerLevel(origin, permissions, isExplicitOwner, hasExplicitRights, groupContext)`:

```
None = 0  <  Rights = 1  <  GroupRights = 2  <  GroupAdmin = 3  <  Owner = 4  <  Moderator = 5
```

- **Owner outranks `ModerateAny`** — a moderator entering their own room is the owner, not a
  moderator (commented).
- `Room.BuildAny` reaches `Owner`.
- The guild layer sits on top of but never below explicit rights.

### The two-part live-rights invariant

This is the codebase's canonical shipped bug, and it needs **both** halves:

**Half 1 — state and DB in the same method.** `RoomLiveState.PlayerIdsWithRights` is read by
`RoomSecurityModule.GetControllerLevelAsync`. It is hydrated on activation and updated in the same
method as the `SaveChangesAsync` at all four mutation sites: `AssignRightsAsync`, `RemoveRightsAsync`,
`RemoveAllRightsAsync`, `RemoveOwnRightsAsync`.

**Half 2 — re-stamp the avatar.** `RoomSecurityModule.RefreshControllerLevelForPlayerAsync` pushes
`IPlayerPresenceGrain.OnControllerLevelUpdatedAsync` **and** re-stamps the avatar's
`AvatarStatusType.FlatControl`. The comment records the follow-on bug: the presence notification only
redraws the subject's own UI, so without the status the rights star appeared only after a rejoin.

Pinned by `Vortex.Rooms.Tests/Permissions/RoomSecurityFlatControlTests.cs` (4 tests).

Guild rights get the same treatment: `RefreshGroupMembershipAsync` **re-reads `rooms.group_id` from
the DB** rather than trusting the cached snapshot — a dissolved guild would otherwise keep granting
build rights — then re-pushes the controller level for every affected present player.

Room and hotel mutes are written to live state in the same methods that persist them
(`RoomModerationSystem`), and read by `RoomChatSystem`.

## Configuration

`Vortex.Rooms/Configuration/RoomConfig.cs`, section `Vortex:Rooms`, also implementing `IWiredLimits`.

> **Neither `appsettings.json` nor `appsettings.Development.json` has a `Vortex:Rooms` section**, so
> every value below is the compiled default in production today.

`MaxStackHeight` 4000 · `MaxStepHeight` 200 · `RoomTickMs` 50 · `AvatarTickMs` 500 ·
`RollerTickMs` 2000 · `DirtyItemsTickMs` 2000 · `MaxDirtyItemsPerFlush` 100 ·
`MaxTileHeightsPerFlush` 200 · `MaxPathNodes` 4096 · `RoomCheckMs` 300 000 ·
`RoomDeactivationDelayMs` 1 800 000 · `DoorbellTimeoutMs` 20 000 · `MysteryBoxWaitTimeoutMs` 120 000 ·
`MaxTradeItemsPerSide` 1500 · `HandItemDurationMs` 30 000 · `ChatFloodIntervalSeconds` [4,2,1] ·
`ChatFloodAllowance` 5.

Wired limits are on Wireds (retirés au M0). `PetConfig.TickMs` = 500. **`BotTickMs` = 1000 is a private
const** in `RoomBotSystem.cs`, not configuration.

## Sub-pages

| Page | Covers |
|---|---|
| [Lifecycle](lifecycle.md) | enter → hydrate → membership → leave → deactivate, with the composer burst |
| [Movement and tick](movement-and-tick.md) | the 50 ms tick, its 11 steps, pathfinding |
| [Furniture](furniture.md) | definitions, logic binding, placement, pickup |
| Wireds (retirés au M0) | the five-component engine |

## Tests

`Vortex.Rooms.Tests` — 137 files, the largest suite in the repo. Notable:

- `Support/RoomHarness.cs` builds a **real `RoomGrain` outside a silo** via
  `GrainActivationContext.CreateWithIntegerKey<RoomGrain>`, swaps `_roomOutbound` for a recording
  stream proxy, registers an `EventRecorder` listener, and starts its simulated clock at
  `Grain.NowMs()` — the comment notes a test ticking from zero would be permanently in the room's past
- `Permissions/RoomSecurityFlatControlTests.cs` — the two-part rights invariant
- `Games/RoomMinigameCoordinationTests.cs` — 11 facts including the two registration guards
- `Games/RoomItemIndexTests.cs` — the index attach/detach invariant including base-class buckets
- `Grains/{RoomPersistenceLossWindowTests,TickBoundaryTests,GrainTurnIsolationTests}.cs`

## Known unknowns

- **Unknown:** whether `RoomEventModule.PublishAsync`'s sequential, un-caught listener loop is
  fail-fast by design.
  - Inspected: the method — a throwing listener aborts the publish for listeners registered after it.
  - Contrast: `RoomGameSystem.ForEachMinigameAsync` and `RoomGrain.RunTickStepAsync` both isolate
    deliberately and say so.
  - What would resolve it: a comment, or a test asserting either behaviour.
- **Cosmetic:** `RoomGrain.Facets.cs` lists 13 facets and omits `IRoomBots`, while `IRoomGrain`
  declares 14. Harmless to the compiler, wrong for that file's stated purpose.

## Sources

- `Vortex.Primitives/Rooms/Grains/IRoomGrain.cs` + the 14 facet files
- `Vortex.Rooms/Grains/RoomGrain.cs` (constructor, composition), `RoomGrain.Facets.cs`, `RoomGrain.Capabilities.cs`
- `Vortex.Rooms/Grains/{RoomLiveState,RoomItemIndex,RoomDirectoryGrain}.cs`
- `Vortex.Rooms/Grains/Modules/{RoomSecurityModule,RoomEventModule}.cs`
- `Vortex.Rooms/Grains/Systems/RoomModerationSystem.cs`
- `Vortex.Primitives/Permissions/RoomSecurityPolicy.cs`
- `Vortex.Rooms/Configuration/RoomConfig.cs`
- `Vortex.Rooms.Tests/Support/RoomHarness.cs`, `Permissions/RoomSecurityFlatControlTests.cs`
