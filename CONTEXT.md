# Vortex Cloud Architecture Context

`Vortex.Cloud.sln` is the main emulator solution. `Vortex.Main` is the runtime host and composition root.

This is the RPG fork cut to its core (2026-10-07, `docs/superpowers/plans/2026-10-07-coupe-noyau.md`):
the network, sign-in, rooms as zones (heightmap, pathfinding, avatars, furniture) and nothing else.
Chat, economy, social, navigator, progression, moderation and the dashboard are gone; the RPG
systems come back on this base, one phase at a time (spec `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md`).

## Core structure and responsibilities
- `Vortex.Main/`
  - Host startup and wiring (`Program.cs`), configuration, lifetime, and console commands.
- `Vortex.Plugins/`
  - Runtime discovery, loading, start/stop, and unload lifecycle for plugins.
- `Vortex.PacketHandlers/`
  - Incoming message handling orchestration and domain dispatch.
- `Vortex.Events/`
  - Event behavior/handler pipeline registration and execution.
- `Vortex.Rooms/`, `Vortex.Players/`, `Vortex.Furniture/` — the Platform layer
  - Zones (map, heightmap, pathfinding, avatars, furniture), the player's card and presence,
    furniture definitions and stuff data.
- `Vortex.Permissions/`
  - Roles and capabilities.
- `Vortex.WebApi/`, `Vortex.WebApi.Authentication/` — the Web layer
  - Registration, login, avatar creation and the SSO ticket.
- `Vortex.Database/`
  - EF Core context and persistence infrastructure.
- `Vortex.Primitives/`
  - Cross-module contracts, identifiers, snapshots, and message types.

## Hard boundaries
Three of these are no longer prose: `scripts/hooks/check-architecture-walls.mjs` fails the build on a
packet handler that reaches for the database, a new wire-protocol import inside the contracts hub,
and a reference that points up the layers (Core → Platform/Web, Platform → Web; see `AGENTS.md`,
"Layers"). The rest still rely on review.

- Keep host composition and module registration in `Vortex.Main`; avoid leaking host concerns into domain modules.
- Keep packet handlers focused on request/response orchestration, not persistence infrastructure wiring.
- Keep database querying and persistence access out of packet handlers.
- Keep grain lifecycle/state logic within grain modules; do not bypass grain boundaries with direct cross-layer shortcuts.
- Keep plugin lifecycle operations inside `Vortex.Plugins`; do not duplicate plugin loading logic in unrelated modules.
- `Vortex.Revisions/Revision20260701/**` is the default revision embedded in core (so the emulator
  runs standalone without a plugin) — its `Parsers/` and `Serializers/` trees legitimately live in
  `vortex-cloud` and are edited there.
- Protocol revision parser/serializer trees for any **other/additional** revision (added via the
  plugin system) are owned by the plugin repo at:
  - `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`
  - Do not create new `Revision<id>/Parsers` or `Revision<id>/Serializers` trees in `vortex-cloud`
    for revisions other than the embedded `Revision20260701` default.
- **The deployment thesis is single-silo.** The clustering configuration can form a multi-silo
  cluster, but caches, metrics aggregation and room streams are silo-local, and none of them fail
  loudly on a second silo — they just go quietly stale. A second silo is refused at startup unless
  `Vortex:Orleans:MultiSiloReady` is set; that property's XML doc carries the current inventory of
  what has to become cluster-aware first, and is the one place it is kept.
- Player lookup boundary:
  - `Vortex.Players/Grains/PlayerDirectoryGrain.cs` owns username/id lookup semantics and cache coherence.
  - `Vortex.Players/Grains/PlayerGrain.cs` owns the player's card (name, motto, look) and its summary snapshot.

## Data and lookup semantics
- Username-to-id lookup behavior is case-insensitive.
- Directory cache updates must keep forward and reverse mappings consistent across set/invalidate paths.
- Avoid adding handler-level fallbacks that bypass directory-grain lookup responsibilities.

## Session and room runtime flow
- Connection accepted:
  - session is added to gateway/session tracking.
- After SSO success:
  - session is registered to `PlayerPresenceGrain` for that player id;
  - the burst is `AuthenticationOK`, `UserRights`, then `RoomForward` to `world.start_room_id`
    (server config; 0 = none). `scripts/sql/dev_zone.sql` creates the development zone and sets it.
- Player outbound targeting:
  - resolve target player's presence grain
  - call `SendComposerAsync`
  - fan-out to subscribed sessions happens inside presence flow.
- Room activity:
  - active room indexing/lookup and keepalive ping responsibilities stay in `RoomDirectoryGrain`.
- Lifecycle:
  - inactive grains are Orleans-managed and can deactivate automatically unless explicitly marked `[KeepAlive]`.

## Grain runtime patterns
- Every grain that does cross-grain calls or DB work must inject `ILogger<T>` and log caught exceptions. No bare `catch { }`.
- Independent grain calls (e.g. checking online status for N players) must use `Task.WhenAll`, not sequential `await` in a loop.
- Identical grain calls must not repeat inside loops — hoist before the loop.
- DB batch operations use single `WHERE ... IN (...)` queries, not per-entity `ExecuteDeleteAsync` loops.
- Housekeeping writes (e.g. delivered flags) follow the timer-flush pattern: queue dirty state, flush with `RegisterGrainTimer`, flush on `OnDeactivateAsync`. See `RoomPersistenceGrain` for reference.
- Do not hardcode limits (`Take(N)`, capacity constants) in grains. Pass them from handlers via `IConfiguration`.
- When a delete + insert must be atomic, use EF tracked operations (`Remove` + `SaveChangesAsync`), not `ExecuteDeleteAsync`.
- Replace `.Ignore()` on grain tasks with a `LogAndForget` helper that logs faulted continuations.
- In-memory collections that grow per-event (message history, queues) must have a configurable cap.
- One grain per responsibility: isolate heavy I/O (DB writes, persistence) into secondary grains so the primary domain grain stays responsive (e.g. `RoomGrain` → `RoomPersistenceGrain`).
- Use grain single-threading for concurrency safety: one `PlayerGrain` per player serializes changes to their card. Do not add manual locks inside grains.
- Grains orchestrate their own outbound: when state changes, the grain sends the composer via `PlayerPresenceGrain.SendComposerAsync`. Callers do not build or send composers after calling a grain method.
- All mutations to grain-owned data must go through grain methods. Do not update the database directly — grains may hold cached state that will not reflect raw DB changes.

## Placement rules
- New host startup/wiring behavior:
  - `Vortex.Main/` (usually `Program.cs`, `Extensions/`, or `Console/`)
- New incoming packet behavior:
  - `Vortex.PacketHandlers/<Domain>/<Name>MessageHandler.cs`
- New domain service/provider:
  - `Vortex.<Domain>/...` in the existing service/provider structure
- New grain behavior:
  - `Vortex.<Domain>/Grains/...`

## Pattern references
Use and adapt these examples before inventing new structure:
- `docs/patterns/ServicePattern.cs`
- `docs/patterns/HandlerPattern.cs`
- `docs/patterns/UnitTestPattern.cs`
