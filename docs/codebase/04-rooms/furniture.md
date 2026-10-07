# Furniture

## Purpose

Definitions vs items, how behaviour binds to a piece of furni, and why classname is not a key.

## Definitions vs items

| | Definition | Item |
|---|---|---|
| What | the catalogue entry — sprite, dimensions, logic name, stuff-data type | one placed or owned instance |
| Table | `furniture_definitions` | `furniture` |
| Loaded by | `FurnitureDefinitionProvider` (singleton, `LoadStage = 0`) | `RoomItemsProvider` / `InventoryFurnitureLoader` |
| Lifetime | reference data, reloaded on admin edit | per room activation / inventory read |

`FurnitureDefinitionProvider` publishes `ById`, `ByName` and `Version` as **one immutable object under
`Volatile.Write`**, so a reload can never be half-observed.

> `RoomModelProvider`'s comment records a real bug worth remembering: `Altitude.FromValue` vs
> `FromInt` — getting it wrong flattened every room model by 100×.

## A classname is not a key

`FurnitureDefinitionEntity` carries `[Index(SpriteId, ProductType, FurniCategory, IsUnique = true)]`.
**`name` has no unique index**, by design — the client's own furnidata ships duplicates.

`FurnitureDefinitionProvider` builds `ByName` with `defs.DistinctBy(p => p.Name)` and comments that
`tile_stackmagic` and `roomdimmer` each appear twice.

`FurnitureDefinitionLookup` documents the real figures: **3533 duplicated classnames across 7463 live
rows**, e.g. `clothing_nftshoulderdragon1` at ids 4197734 and 9745384. It collapses on the lowest id
for determinism.

> A `ToDictionary` on `furniture_definitions.name` **throws**. Use `FurnitureDefinitionLookup` for any
> classname → definition lookup.
>
> (It lives in `Vortex.Collectibles`, not `Vortex.Furniture` — see the caveat on
> [Solution map](../00-overview/solution-map.md).)

## Logic binds by the `logic` column

Not by classname. Verified end to end:

```
FurnitureDefinitionEntity.Logic          [Column("logic")] [MaxLength(50)]
   → FurnitureDefinitionSnapshot.LogicName
   → RoomObjectModule.AttatchLogicAsync uses floor.Definition.LogicName / wall.Definition.LogicName
```

Classname is **never** consulted for logic binding.

### Registration and collisions

`[RoomObjectLogic(string key)]` — `AllowMultiple = true`, `Inherited = false`.

Discovery is the same assembly-scan seam handlers use:
`Vortex.Rooms/Object/Logic/RoomObjectLogicFeatureProcessor` is an `IAssemblyFeatureProcessor` that
walks `AssemblyExplorer.FindAssignees(asm, typeof(IRoomObjectLogic))`, reads every attribute, and
registers `(key, type, ActivatorUtilities.CreateInstance)`. **Plugin assemblies contribute logic
classes the same way core does.**
→ [Packet pipeline](../02-network-protocol/packet-pipeline.md)

`RoomObjectLogicProvider` keys registrations on **`(logicName, LogicFamily{Any|Floor|Wall})`**, because
the client reuses `furniture_multistate` and `furniture_basic` for both families.

A collision **fails** the registration with `VortexErrorCodeEnum.InvalidLogic` rather than
overwriting. The doc records the prior bug: a plugin silently took over a core furni, and unloading it
dropped that furni to `default_floor` **permanently**. A duplicate registration of the *same* type is
tolerated and returns an inert disposable.

**238 unique logic keys across 199 classes**, of which 169 are wired (Wireds (retirés au M0)).

Missing bindings are counted: `Vortex.furniture.logic.fallback`.

## The item index

`RoomItemIndex` is a `Dictionary<Type, HashSet<IRoomItem>>` keyed by the attached logic's concrete
type **and every base class** — so `ItemsOf<FamilyBaseLogic>()` finds a whole family without scanning
`ItemsById`.

Its invariant is maintained by convention at four sites: `RoomObjectModule.AttatchLogicAsync`
(attach), `RoomObjectModule.RemoveObjectAsync`, `RoomGrain.Furni.Edit.cs`, `RoomPetSystem.Motion`
(detach).

Room-game code is required to go through it (`ItemsOf<T>` / `LogicsOf<T>`) and through
`RoomMapModule.FirstLogicOnTile<T>`, never a scan.

Pinned by `Vortex.Rooms.Tests/Games/RoomItemIndexTests.cs`.

## Stuff data

Per-item state lives in `furniture.extra_data`, which is **sectioned**:

| Section | Written by |
|---|---|
| `STUFF` | `FurnitureLogic.PersistStuffDataAsync`, only when `_stuffPersistanceType == Persistent` |
| `WIRED` | `WiredData.MarkDirty` → `ExtraData.UpdateSection(WIRED, …)` |

Sections are independent — `WiredPersistenceRoundTripTests` asserts that persisting wired does not
clobber the others.

`IStuffDataFactory` builds the typed representation. The `stuff_data_type` column is backfilled
per-logic; find gaps with a "partially typed family" query rather than by counting zeroes.

## Placement and pickup

Both mutate live state immediately and enqueue the row change for `RoomPersistenceGrain`'s 2 s timer.
The full sequence, and the ownership-transfer subtlety in `FurniturePickupType.SendToCtx`, is on
[Inventory](../06-economy/inventory.md).

Authorization: `PlaceFloorItemAsync` and `MoveFloorItemByIdAsync` both call
`SecurityModule.CanManipulateFurniAsync(ctx)`. `ApplyWiredUpdateAsync`, in the same file, does
**not** — see the open item on Wireds (retirés au M0).

## Interaction

`FurnitureLogic.OnUseAsync` is the click path. Logic classes reach room state through the explicit
capability interfaces on `RoomGrain.Capabilities.cs` (`IRoomLookup`, `IRoomMapAccess`,
`IRoomFurniAccess`, …) — never through the grain interface, so nothing a logic does leaves the turn.

`OnWalkOnAsync` / `OnWalkOffAsync` fire from the avatar tick's per-step validation
([Movement and tick](movement-and-tick.md)). `OnAttachAsync` / `OnDetachAsync` bracket the item's life
in the room.

## Persistence

| What | Written by | When |
|---|---|---|
| position, rotation, z, owner, `extra_data`, wall offset | `RoomPersistenceGrain.FlushDirtyItemsAsync` | timer every 2 s (≤100 rows) + drain on deactivate |
| removal (`RoomEntityId = null`) | same, via `EnqueueDirtyItemAsync(…, remove: true)` | queued immediately |
| `definition_id` (furni-editor swap) | `RoomGrain.PersistFurniDefinitionAsync` | synchronously |

> `definition_id` is **not** in the flush set, so a raw SQL edit to it survives — but the live room
> keeps the old definition until reactivation.

## Tests

Four reflection-driven suites walk every `[RoomObjectLogic]` attribute in the assembly:

- `RoomObjectLogicCollisionTests` — no two implementations claim one key in one family
- `RoomObjectLogicUniquenessTests`
- `RoomObjectLogicKeyTests` — keys are well-formed
- `RoomObjectLogicFamilyTests` — a floor logic is never registered for a wall context

Plus `Vortex.Database.Tests` for the definition model and `RoomItemIndexTests` for the index.

## Known unknowns

- **Unknown:** whether an active `RoomGrain` ever re-reads furniture definitions.
  - Inspected: `RoomItemsProvider` copies the `FurnitureDefinitionSnapshot` **into each `RoomItem` at
    materialization**, and `RoomGrain` holds the provider. No periodic refresh or targeted
    invalidation was found — but not all 28 `RoomGrain` partials were read.
  - Consequence if confirmed: an admin furniture-definition edit does not reach rooms that are already
    active; it lands on new placements and on rooms activated afterwards.
  - What would resolve it: a grep for `IFurnitureDefinitionProvider` uses inside `RoomGrain`, or an
    admin edit against a room with the item already placed.
  - → [Dashboard operations](../08-dashboard/operations.md), where this is the largest listed
    DB/live gap.

## Sources

- `Vortex.Furniture/Providers/FurnitureDefinitionProvider.cs`, `Vortex.Furniture/FurnitureAdminService.cs`
- `Vortex.Database/Entities/Furniture/FurnitureDefinitionEntity.cs` — the unique index
- `Vortex.Collectibles/Grains/FurnitureDefinitionLookup.cs` — the duplicate-classname figures
- `Vortex.Rooms/Providers/RoomObjectLogicProvider.cs`, `Vortex.Rooms/Object/Logic/RoomObjectLogicFeatureProcessor.cs`
- `Vortex.Rooms/Object/Logic/Furniture/FurnitureLogic.cs`
- `Vortex.Rooms/Grains/Modules/{RoomObjectModule,RoomActionModule}.cs`, `RoomActionModule.Floor.cs`
- `Vortex.Rooms/Grains/RoomItemIndex.cs`, `RoomGrain.Furni.Edit.cs`
- `Vortex.Rooms/Providers/{RoomItemsProvider,RoomModelProvider}.cs`
- `Vortex.Rooms.Tests/Furniture/*.cs`, `Games/RoomItemIndexTests.cs`
- `docs/walkthroughs/add-a-furni.md`
