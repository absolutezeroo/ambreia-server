# Vortex Cloud — codebase documentation

Reconstructed from the source at commit **`e57f0be7`** (branch `main`) by `/document-vortex full`.

This documentation explains **how the emulator actually works**: what owns which state, what happens
between a byte arriving on a socket and a composer going back out, and where the contract the
repository writes down and the code that ships disagree. It is not a tour of folders.

## How to use this

| If you need to… | Start at |
|---|---|
| Understand the whole system in one sitting | [System overview](00-overview/system-overview.md) |
| Find which project owns something | [Solution map](00-overview/solution-map.md) · [Project index](generated/project-index.md) |
| Know what you are not allowed to do | [Architecture boundaries](00-overview/architecture-boundaries.md) |
| Follow a packet end to end | [Packet round trip](flows/packet-roundtrip.md) |
| Understand a login | [Authentication](flows/authentication.md) |
| Add or change a grain | [Grain map](03-orleans/grain-map.md) · [State ownership](03-orleans/persistence.md) |
| Work on a room, furni or wired | [Room architecture](04-rooms/room-architecture.md) · [Wireds](04-rooms/wireds.md) |
| Touch money or items | [Economy overview](06-economy/overview.md) · [Transactions](06-economy/transactions.md) |
| Change a packet's wire format | [Revisions](02-network-protocol/revisions.md) |
| Add an admin surface | [Dashboard capabilities](08-dashboard/capabilities.md) |
| Ship a plugin | [Plugins](09-extensibility/plugins.md) |
| Run, watch or benchmark the hotel | [Operations](10-operations/observability.md) |

## The eight things most likely to surprise you

Each is proven on its own page; they are collected here because each one has already cost someone a
day.

1. **No grain uses `[PersistentState]`.** All grain state persists through EF Core. The only Orleans
   grain store configured is `PubSubStore`, required by the stream providers.
   → [Persistence](03-orleans/persistence.md)
2. **`IInventoryGrain.AddFurnitureAsync` / `RemoveFurnitureAsync` are view-only.** They mutate a
   dictionary. The durable ownership write is the *caller's* job, and not every caller does it.
   → [Inventory ownership](06-economy/inventory.md)
3. **A player has exactly one session.** `PlayerPresenceGrain` holds a single observer field and a
   second login closes the first. "Fan-out to subscribed sessions" describes a design that is not
   built. → [Presence routing](03-orleans/presence-routing.md)
4. **A handler replying to its own requester through `ctx.SendComposerAsync` is normal and correct**
   (306 call sites). The presence grain is for reaching *other* players. The written rule does not
   make that distinction. → [Outbound routing](03-orleans/presence-routing.md)
5. **104 serializers are written but never mapped**, so they can never run — whole families, like all
   32 `Game2*`. A composer with no serializer is dropped silently at the encoder.
   → [Revisions](02-network-protocol/revisions.md)
6. **A logic class binds to furniture by the `logic` column, never by classname** — classname is not
   unique (3533 duplicates). → [Furniture](04-rooms/furniture.md)
7. **A second silo is refused at startup** unless `Vortex:Orleans:MultiSiloReady` is set, because
   caches, stream fan-out and metrics are silo-local. → [Orleans overview](03-orleans/overview.md)
8. **The dashboard runs in its own DI container** and an endpoint injecting an unforwarded service
   takes the whole dashboard down at startup. → [API architecture](08-dashboard/api-architecture.md)

## Evidence policy

Every material claim on these pages names the file and symbol that proves it. Four classes of
evidence are kept apart, and the distinction is not cosmetic:

| Class | Source | What it proves |
|---|---|---|
| **Implementation** | current source, registrations, tests, migrations | what the code does |
| **Contract** | `AGENTS.md`, `CONTEXT.md`, `CLAUDE.md`, build targets | what the code is *supposed* to do |
| **Orientation** | READMEs, audits, roadmaps, older docs | where to look; never proof on its own |

Where implementation and contract disagree, **both are documented** and the divergence is flagged.
History is not rewritten to match either one. Claims that could not be settled from the source appear
under a page's *Known unknowns* with what was inspected and what would resolve them.


## Relationship to the rest of `docs/`

| Tree | What it is | Standing |
|---|---|---|
| `docs/codebase/` | this — current state reconstructed from code | authoritative for *what the code does today* |
| `docs/walkthroughs/` | task recipes ("add a wired box", "add a dashboard page") | current and linked from these pages |
| `docs/patterns/` | reference-only code samples | current |
| `docs/architecture/`, `docs/audits/`, `CONSOLIDATION.md`, `AUDIT-ARCHITECTURE-GLOBALE.md` | dated snapshots | historical; several findings are already fixed |
| `docs/client-server-architecture.md`, `docs/orleans.md` | older narrative docs | **partly stale** — see below |

### Known-stale claims in older docs

Verified against the code at this commit:

| Document | Claim | Actual |
|---|---|---|
| `client-server-architecture.md` §13 | Trading "Does Not Exist", handlers are empty stubs | 10 implemented handlers under `Vortex.PacketHandlers/Inventory/Trading/`, plus `IRoomTrading` and `RoomTradingSystem` |
| `client-server-architecture.md` §18 | Bots "Do Not Exist", no entity, no migration | `RoomBotSystem` + 4 partials, `Vortex.Database/Entities/Room/BotEntity.cs`, migrations `20260808140912_AddBots`, `20260808182526_AddBotSkills` |
| `client-server-architecture.md` §2 | `VortexEmulator.StartAsync` registers revisions | `Vortex.Revisions/RevisionRegistrationService.cs` does |
| `orleans.md` §Storage | grain stores `PlayerStore` / `RoomStore`; `PlayerPresenceGrain` uses `[PersistentState]` | only `PubSubStore` exists; `PlayerPresenceGrain` is a plain `Grain`; `OrleansStateNames` is dead code |
| `README.md` | ".NET SDK 9.x pinned via `global.json`" | `global.json` pins `10.0` |
| `CONTEXT.md` | three architecture walls; capability string in "six" files | six walls; four files (`AGENTS.md` is right) |
| `AGENTS.md` | `Vortex:FriendList:UserFriendLimit` as the configured-limit example | that key does not exist; limits moved to `IServerConfigGrain` |

These are listed so a reader who arrives from an older page knows which half to trust. Correcting
them is a separate change, not part of this documentation run.

## Regenerating

```bash
/document-vortex update            # re-scan, re-research only what changed since the recorded SHA
/document-vortex topic rooms       # one domain
/document-vortex validate          # check references, links and stale symbols
```

State lives in [`.documentation-state.json`](.documentation-state.json). The
[generated indexes](generated/) are produced by a static scan and are labelled as such — they
inventory symbols and must not be read as runtime semantics.
