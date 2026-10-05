# Vortex Cloud Architecture Context

`Vortex.Cloud.sln` is the main emulator solution. `Vortex.Main` is the runtime host and composition root.

## Core structure and responsibilities
- `Vortex.Main/`
  - Host startup and wiring (`Program.cs`), configuration, lifetime, and console commands.
- `Vortex.Plugins/`
  - Runtime discovery, loading, start/stop, and unload lifecycle for plugins.
- `Vortex.PacketHandlers/`
  - Incoming message handling orchestration and domain dispatch.
- `Vortex.Events/`
  - Event behavior/handler pipeline registration and execution.
- `Vortex.*` domain modules (`Rooms`, `Players`, `Catalog`, `Inventory`, etc.)
  - Domain services, snapshot providers, and Orleans grain orchestration.
- `Vortex.Database/`
  - EF Core context and persistence infrastructure.
- `Vortex.Primitives/`
  - Cross-module contracts, identifiers, snapshots, and message types.

## Hard boundaries
Three of these are no longer prose: `scripts/hooks/check-architecture-walls.mjs` fails the build on a
packet handler that reaches for the database, a dashboard `SaveChanges` that bypasses the owning
grain, and a new wire-protocol import inside the contracts hub. The rest still rely on review.

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
- Extended profile flow boundary:
  - `Vortex.PacketHandlers/Users/*ExtendedProfile*Handler.cs` orchestrates lookup + response mapping only.
  - `Vortex.Players/Grains/PlayerDirectoryGrain.cs` owns username/id lookup semantics and cache coherence.
  - `Vortex.Players/Grains/PlayerGrain.cs` exposes profile snapshots consumed by handlers.

## Data and lookup semantics
- Username-to-id lookup behavior is case-insensitive.
- Directory cache updates must keep forward and reverse mappings consistent across set/invalidate paths.
- Avoid adding handler-level fallbacks that bypass directory-grain lookup responsibilities.

## Session and room runtime flow
- Connection accepted:
  - session is added to gateway/session tracking.
- After SSO success:
  - session is registered to `PlayerPresenceGrain` for that player id.
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
- Independent grain calls (e.g. checking online status for N friends) must use `Task.WhenAll`, not sequential `await` in a loop.
- Identical grain calls must not repeat inside loops — hoist before the loop.
- DB batch operations use single `WHERE ... IN (...)` queries, not per-entity `ExecuteDeleteAsync` loops.
- Housekeeping writes (e.g. delivered flags) follow the timer-flush pattern: queue dirty state, flush with `RegisterGrainTimer`, flush on `OnDeactivateAsync`. See `RoomPersistenceGrain` for reference.
- Do not hardcode limits (`Take(N)`, capacity constants) in grains. Pass them from handlers via `IConfiguration`.
- When a delete + insert must be atomic, use EF tracked operations (`Remove` + `SaveChangesAsync`), not `ExecuteDeleteAsync`.
- Replace `.Ignore()` on grain tasks with a `LogAndForget` helper that logs faulted continuations.
- In-memory collections that grow per-event (message history, queues) must have a configurable cap.
- One grain per responsibility: isolate heavy I/O (DB writes, persistence) into secondary grains so the primary domain grain stays responsive (e.g. `RoomGrain` → `RoomPersistenceGrain`).
- Use grain single-threading for concurrency safety: per-player `PurchaseGrain` for catalog buys, dedicated grains for limited-edition items. Do not add manual locks inside grains.
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
- New dashboard admin surface:
  - read API `Vortex.Dashboard.API/Api/DashboardApiService.<Domain>.cs`, writes
    `Vortex.Dashboard.API/Operations/DashboardOperationsService.<Domain>.cs`, endpoints
    `Vortex.Dashboard.API/Hosting/DashboardEndpoints.<Domain>.cs`, page
    `Vortex.Dashboard.Web/src/pages/<Domain>Page.svelte`
  - the capability string must be added to **six** files — see the checklist in `AGENTS.md`
    ("Add dashboard capability or admin page") and `docs/walkthroughs/add-a-dashboard-page.md`.
    Omitting the policy list throws `AuthorizationPolicy named '<capability>' was not found` at
    runtime; nothing in the build or tests catches it.

## Pattern references
Use and adapt these examples before inventing new structure:
- `docs/patterns/ServicePattern.cs`
- `docs/patterns/HandlerPattern.cs`
- `docs/patterns/UnitTestPattern.cs`
