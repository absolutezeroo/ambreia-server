---
name: adding-a-room-object-logic
description: Gives a furniture (or room object) a server-side behaviour in the Vortex emulator — a [RoomObjectLogic] class bound to furniture_definitions.logic, its state persistence, hooks (use, walk on/off, place, pickup), walkability, and tests. Use when the user wants a furni that reacts, blocks or opens a tile, toggles, or when a furni "does nothing" in game.
---

# Adding a room object logic

Most furniture needs no code: a row in `furniture_definitions` with `can_sit`/`can_walk` is enough.
A logic is for a box that reacts. Two things are joined by one column:

| | |
|---|---|
| definition | `furniture_definitions.logic` (imported from `turbo`, bound by `Vortex.Database/Seeds/furni_logic_bindings.sql`) |
| behaviour | a class carrying `[RoomObjectLogic("<that logic value>")]` in `Vortex.Rooms/Object/Logic` |

A `logic` value nobody registered loads the family default (`default_floor`/`default_wall`) and does
nothing — no error. `RoomObjectLogicKeyTests` pins that for the names removed by the core cut.

## 1. Claim the name, pick the base

```csharp
[RoomObjectLogic("gate")]
public class FurnitureGateLogic(IStuffDataFactory stuffDataFactory, IRoomFloorItemContext ctx)
    : FurnitureFloorLogic(stuffDataFactory, ctx)
```

`FurnitureFloorLogic` or `FurnitureWallLogic` (`FurnitureRoomDimmerLogic` for a wall example). Several
attributes are fine (an Arcturus `interaction_type` and the client's asset name); a name two classes
claim fails at registration (`RoomObjectLogicUniquenessTests`). The admin dropdown that used to list
logics left with the dashboard; nothing else needs updating.

## 2. Decide what the state survives

```csharp
protected override StuffPersistanceType _stuffPersistanceType => StuffPersistanceType.RoomActive;
```

`Persistent` (default) writes the state back to the furniture row through `RoomPersistenceGrain`;
`RoomActive` keeps it for the room activation only (no DB write per change). The stuff-data type
follows the definition and decides the wire encoding the client's dialog reads.

## 3. Override only the hooks the box needs

| Hook | Fires when |
|---|---|
| `OnUseAsync(ctx, param, ct)` | a player uses it — **needs a use handler**: the core cut removed `UseFurniture`/`UseWallItem`; re-add one with the `adding-a-packet` skill |
| `OnWalkOnAsync` / `OnWalkOffAsync` | an avatar steps on or off its tile (works today, from the avatar tick) |
| `OnPlaceAsync` / `OnPickupAsync` / `OnMoveAsync` | it enters, leaves or moves in the room |
| `CanWalk` / `CanSit` / `CanLay` / `CanStack` | to say something the definition cannot (the gate is walkable only when open) |

- Never trust `param`: anything a player would want to influence is decided server-side.
- Read `_ctx.Definition.TotalStates` instead of hardcoding a state count.
- A logic runs inside the room's turn: refuse (leave the state alone) rather than throw — a throw
  costs the room its tick.

## 4. Test the behaviour

A logic is constructible on its own; for room effects (a tile blocked, an avatar moved) use
`RoomHarness.CreateAsync()` in `Vortex.Rooms.Tests`. Assert the state it lands in **and** the refusal
path. Examples: `Vortex.Rooms.Tests/Furniture/RoomDimmerLogicTests.cs`,
`Vortex.Rooms.Tests/Furniture/CustomStackHeightMultiWalkTests.cs`.

## 5. Verify

```bash
dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~<YourLogic>"
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```

Then place a definition using that `logic` in the dev zone (`/sql-fix` script on the fork database)
and walk to it with `driver.mjs play`.
