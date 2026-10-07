# Walkthrough: Add a furni that does something

> **Coupe au noyau (2026-10-07).** Les exemples de ce guide citent des messages, systèmes ou
> logiques retirés depuis (chat, expressions, dés…). La mécanique décrite reste juste ; les
> exemples seront réécrits sur le noyau lors de la passe de guidage de l'IA.

Most furniture needs no code. A sofa is a row in `furniture_definitions` with `can_sit` set, and the
room already knows what to do with it. This is about the other kind: a box that reacts — a die that
rolls, a gate that opens, a crackable egg, a teleport.

Two things exist and they are joined by one column:

| | |
|---|---|
| **The definition** | a row in `furniture_definitions`: sprite, size, `can_walk`, `total_states`, and **`logic`** |
| **The logic class** | a C# class carrying `[RoomObjectLogic("<that logic value>")]` |

> **`logic` is the binding, not the classname.** A definition whose `logic` column is empty or points
> at a name nobody registered gets the family default and silently does nothing at all — which looks
> exactly like a bug in the furni. `Vortex.furniture.logic.fallback` counts that, tagged by the name
> asked for, so the size of the unimplemented backlog is a query rather than a hunt.
>
> A classname is also **not** a key: `furniture_definitions.name` is non-unique by design and the
> client's own furnidata ships duplicates. Resolve by definition id, or through
> `FurnitureDefinitionLookup`.

---

## Step 1 — Pick the base and claim the name

```csharp
[RoomObjectLogic("dice")]
[RoomObjectLogic("furniture_dice")]
public class FurnitureDiceLogic(IStuffDataFactory stuffDataFactory, IRoomFloorItemContext ctx)
    : FurnitureFloorLogic(stuffDataFactory, ctx)
```

`FurnitureFloorLogic` or `FurnitureWallLogic`, depending on where the thing hangs.

More than one attribute is normal and is not a hack: the dumps in the wild disagree about what a box
is called — an Arcturus `interaction_type` here, the client's own asset name there — and claiming
both is how a catalogue imported from either works without editing rows.

Claiming a name **another class already claims is a build error**, not a last-one-wins (ADR-000). If
that fires, one of the two classes is a copy-paste of the other.

## Step 2 — Decide what the box remembers, and for how long

```csharp
protected override StuffPersistanceType _stuffPersistanceType => StuffPersistanceType.RoomActive;
```

- `Persistent` (the default) — the state is written back to the furniture row and survives the room
  unloading. A gate that was left open is still open tomorrow.
- `RoomActive` — the state lives as long as the room activation. A die showing a four does not need
  to still show a four next week, and writing it would put a database round trip on every roll.

`_stuffDataType` follows the definition unless the box overrides it. The type decides how the state
is encoded on the wire — `LegacyKey`, `MapKey`, `HighscoreKey`, `CrackableKey` and the rest — and the
client's dialog reads that encoding, so a box whose panel opens blank is usually a stuff-data type
that does not match what the client expects for that family.

## Step 3 — Implement the hooks the box actually needs

They are all virtual and all default to doing nothing. Override the ones that matter:

| Hook | Fires when |
|---|---|
| `OnUseAsync(ctx, param, ct)` | someone double-clicks it; `param` is what the client sent |
| `OnClickAsync(ctx, param, ct)` | a single click, where the family distinguishes the two |
| `OnWalkOnAsync` / `OnWalkOffAsync` | an avatar steps onto or off the tile |
| `OnPlaceAsync` / `OnPickupAsync` / `OnMoveAsync` | the box enters, leaves, or moves within the room |
| `OnStateChangedAsync` | after `SetStateAsync`, for whatever else must follow |
| `CanWalk` / `CanSit` / `CanLay` / `CanStack` | override to say something the definition cannot |

The die is the whole shape in one method:

```csharp
public override async Task OnUseAsync(ActionContext ctx, int param, CancellationToken ct)
{
    int totalStates = _ctx.Definition.TotalStates;

    if (param == FurnitureDiceAction.TurnOff || totalStates <= 1)
    {
        await SetStateAsync(OffState);
        return;
    }

    await SetStateAsync(Random.Shared.Next(1, totalStates));
}
```

Note what it does *not* do: it never takes the face from `param`. The outcome of anything a player
would want to influence is picked server-side, because the client asking for a six is a modified
client asking for a six.

`TotalStates` comes from the definition rather than a constant, so the same class drives a six-sided
die and a twenty-sided one without knowing there are two.

## Step 4 — Read the states from the assets, not from a guess

State numbering is the furni's own, and it is not always what it looks like:

- a dimmer's presets are **one-based**;
- a magic tile's height is the item's Z, not a state at all;
- `es_box` uses 0/1 for off/on and 2–7 and 12–17 for two different colour ranges.

`.nitro` is the authority for what a box's states mean. Getting this from another emulator's dump is
evidence, not authority, and the two disagree often enough to matter.

## Step 5 — Test the behaviour, not the plumbing

A logic class is constructible on its own — that is the point of it taking a context rather than a
room. Assert on the state it lands in and on what it refused to do:

```csharp
await logic.OnUseAsync(ctx, param: 0, CancellationToken.None);

logic.GetState().Should().BeInRange(1, 5);
```

The failure path first: a box asked to do something it cannot must leave the state alone rather than
throw, because a throwing logic takes the room's tick with it.

If the box changes what a room does — blocks a tile, moves an avatar, starts a game — it belongs in a
room test rather than a logic test. `Vortex.Rooms.Tests` has the harness.

---

## The checklist

1. `Specs analyze` for anything the client sends or reads; unknown official behaviour stays unknown.
2. The `logic` column of the definitions that should use it matches an attribute on the class.
3. `Persistent` or `RoomActive` chosen on purpose, not inherited by accident.
4. `TotalStates` read from the definition; no state count hardcoded.
5. Anything a player benefits from is decided server-side.
6. A test that asserts the resulting state, including the refusal.

## Anti-patterns this exists to prevent

- **Trusting `param`.** It is whatever the client felt like sending.
- **`ToDictionary` on `name`.** Definition names are not unique — it throws, and it has taken a whole
  page down before.
- **Hardcoding states.** The same class serves families that differ only in `total_states`.
- **A logic that throws.** It runs inside the room's turn; the room pays for it.
- **Persisting a state nobody needs tomorrow.** `RoomActive` exists so a die does not write a row per
  roll.
