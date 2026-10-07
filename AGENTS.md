# Vortex Cloud AI Contract

This file is the canonical coding contract for AI-assisted changes in `vortex-cloud`.
Tool-specific instruction files should reference this file instead of duplicating rules.

## Foundational context
This repository targets the following core stack. When coding, prefer patterns compatible with these versions:
- .NET SDK `10.0` (from `global.json`, `rollForward: latestFeature`)
- C# / BCL `net10.0`
- Orleans `10.2.1`
- EF Core `9.0.8` (pinned — see note below)
- Pomelo MySQL provider `9.0.0` (pinned — see note below)
- SuperSocket `2.1.0`

**EF Core/Pomelo are intentionally held on the .NET 9 line.**
`Pomelo.EntityFrameworkCore.MySql` has no EF Core 10-compatible release yet (tracked upstream:
`PomeloFoundation/Pomelo.EntityFrameworkCore.MySql#2007`). EF Core 9 packages run fine on the
`net10.0` TFM/runtime — this is not a build error, just an external blocker. Bump
`Microsoft.EntityFrameworkCore*` and `Pomelo.EntityFrameworkCore.MySql` together once Pomelo ships
EF Core 10 support; do not bump one without the other.

## Skills activation
Activate the relevant skill checklist before editing code in that domain:
- `handler-development`
  - Trigger: editing files under `Vortex.PacketHandlers/**` or message-to-composer orchestration logic.
  - Enforce: orchestration-only handlers, no DB queries, canonical grain access, no silent catches.
- `grain-development`
  - Trigger: editing files under `Vortex.*\\Grains\\**` or `Vortex.Primitives/**/Grains/*.cs`.
  - Enforce: keep ownership boundaries, lifecycle rules, and snapshot/state coherence.
  - Enforce: all rules in the **Orleans grain development rules** section below.
- `session-presence-routing`
  - Trigger: touching session gateway, presence flow, room routing, outbound composer fan-out.
  - Enforce: player outbound via `PlayerPresenceGrain.SendComposerAsync`; no direct handler socket sends.
- `message-contracts`
  - Trigger: editing `Vortex.Protocol/Messages/**` or outgoing composer payload mappings.
  - Enforce: explicit mandatory fields, no placeholder payloads when source data exists.
- `revision-protocol` (cross-repo)
  - Trigger: changes referencing `Revision<id>` packet mappings for a revision other than the
    embedded default `Revision20260701`.
  - Enforce: edit plugin revision tree in `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`.
  - Note: `Revision20260701` itself is embedded in core and is edited directly under
    `Vortex.Revisions/Revision20260701/**` in `vortex-cloud` — this rule does not apply to it.

## Priority order
1. Build and quality checks in repo files (`Directory.Build.props`, `Directory.Build.targets`, `.editorconfig`)
2. `CONTEXT.md` architecture and placement boundaries
3. Existing neighboring code conventions in the target folder
4. Tool-specific adapters (for example `.github/copilot-instructions.md`)

## Portable prompt contract
Use this request shape with any AI tool:
1. Goal:
2. Target files:
3. Required context files:
4. Invariants to preserve:
5. Forbidden changes:
6. Validation commands:
7. Output format:

Default output format:
- concise rationale
- file-by-file diff summary
- risks/assumptions
- exact validation command results

## Required standards
- Target framework/tooling: `.NET 10` pinned via `global.json`.
- Keep C# formatting compatible with repo quality gates (`dotnet csharpier check`, `dotnet format`).
- Follow `.editorconfig` naming/style preferences.
- Keep diffs focused and minimal; avoid unrelated refactors.
- Avoid introducing new dependencies unless required by the task.

## Behavioral rules for generated code
- Match local conventions in the files you touch.
- Prefer deterministic handlers/services with clear guard clauses.
- Preserve cancellation and async flow where it already exists.
- Handle failure paths explicitly; do not ship happy-path-only changes.
- Avoid dead code, unused allocations, and broad catch blocks that hide errors (see **Orleans grain development rules** for specifics).
- For revision compatibility work, prefer restoring/adding missing incoming message contracts in `Vortex.Protocol/Messages/Incoming/**` before mutating serializer/composer payload behavior.
- Do not alter serializer/composer behavior by replacing real payload writes with placeholder constants (for example, unconditional `WriteInteger(0)`) unless explicitly requested.
- `Vortex.Revisions/Revision20260701/**` (including its `Parsers/` and `Serializers/` trees) is the
  **default revision embedded in core** so the emulator can run standalone without a plugin. Editing
  it in `vortex-cloud` is expected and correct — see the "Packet addition checklist" below.
- Any **additional/custom** protocol revision (a different client version added via the plugin
  system) belongs in the plugin repo instead:
  - `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`
  - Do not hallucinate new `Revision<id>/Parsers` or `Revision<id>/Serializers` trees into
    `vortex-cloud` for revisions other than the embedded `Revision20260701` default.

## Orleans grain development rules
These rules exist because every one of these mistakes has shipped and caused real issues.

### Never swallow exceptions silently
Every bare `catch { }` hides a real bug path. Always use `catch (Exception ex)` and log it.
If a cross-grain notification fails silently, state goes asymmetric and nobody knows why.
- **Required**: inject `ILogger<T>` into every grain that does cross-grain calls or DB work.
- **Forbidden**: bare `catch { }`, `catch (Exception) { }` without logging.

### Parallelize independent grain calls
When checking status on N grains (e.g. online status for every avatar in a zone), do not `await` each one in a `foreach`.
Grain calls to different grains can run concurrently with `Task.WhenAll`.
- Sequential = O(n) round-trips. Parallel = O(1) wall time.
- Apply everywhere: activation hydration, search results, batch accept/deny.

### Do not repeat identical grain calls in loops
If a grain method calls its own player's `GetSummaryAsync` inside a loop, hoist the call before the loop.
Same result every iteration = wasted round-trips.

### Batch DB operations
Do not loop `ExecuteDeleteAsync` per entity. Use a single `WHERE ... IN (...)` query.
Same for composer fan-out: collect all updates, send once.

### Use timer-based flush for housekeeping writes
Follow the `RoomPersistenceGrain` pattern: queue dirty state, flush with `RegisterGrainTimer` on interval, and flush on `OnDeactivateAsync`.
Do not issue per-event DB writes that block the grain turn.

### Do not hardcode limits in grains
Handlers read configuration values from `IConfiguration` (or the server config grain) and pass them to grains.
Magic numbers like `Take(50)` or `Take(20)` must come from configuration parameters on the grain interface method.
A 10,000 user hotel needs different tuning than a 10 player dev server.

### Use tracked deletes for atomicity
`ExecuteDeleteAsync` commits immediately and bypasses the EF change tracker.
If a delete + insert must succeed or fail together, use `FirstOrDefaultAsync` + `Remove` so both go through one `SaveChangesAsync`.

### Replace .Ignore() with a LogAndForget helper
Orleans `.Ignore()` makes cross-grain failures invisible. Use a `LogAndForget` extension that calls `ContinueWith(OnlyOnFaulted)` to log the exception.
Still fire-and-forget, but failures are visible in production logs.

### Bound session/history collections
Any in-memory collection that grows per-message (e.g. conversation history) must have a configurable cap.
Without a cap, long-running sessions leak memory.

### One grain per responsibility — isolate heavy I/O
Each major domain component should operate in its own grain. When a grain needs heavy I/O (DB writes, persistence flushes), delegate that work to a dedicated secondary grain so it does not block the primary grain's turn.
- Example: `RoomGrain` delegates furniture saves to `RoomPersistenceGrain`. The room grain stays responsive while persistence queues and flushes.
- Do not combine domain logic and persistence flushing in the same grain.

### Use grain boundaries for thread safety
Orleans grains are single-threaded by design. Use this for concurrency-sensitive operations by giving each user their own grain for the operation.
- Example: each player gets a `PlayerGrain` so changes to their card (name, motto, look) are serialized per-player with no locks needed.
- Do not add manual locking (`lock`, `SemaphoreSlim`) inside grains — that fights the actor model.

### Grains orchestrate their own outbound communication
When grain state changes (e.g. a new look), the grain itself sends the snapshot to `PlayerPresenceGrain.SendComposerAsync`. The caller that triggered the change does not pass or send the composer — the grain owns that responsibility.
- **Correct**: caller calls `grain.SetFigureAsync(...)` → grain updates state → grain notifies the presence grain, which sends the composer.
- **Wrong**: caller calls `grain.SetFigureAsync(...)` → caller builds composer → caller sends composer to player.

### Do not mutate the database directly for grain-owned state
Grains may hold cached or in-memory state that will not reflect direct DB changes. All mutations to grain-owned data must go through the grain's methods, even when the player is offline.
- If a grain uses `[PersistentState]`, state is hydrated from the configured store (not DB) on activation. Direct DB edits will be overwritten by stale store data.
- Admin tools and external systems must call grain methods, not issue raw SQL/DB updates, for data that grains own.

### Keep live authorization state synced with DB writes
A DB write for a permission/moderation/subscription change is not the whole feature. If any in-memory grain
state (a `HashSet`, cached flag, etc.) is read for authorization or gameplay decisions, that state must be
updated in the *same* method that persists the change, and hydrated from DB on grain activation.
- **Real bug**: `RoomLiveState.PlayerIdsWithRights` was checked by `GetControllerLevelAsync` but never
  populated on room hydration nor updated by `AssignRightsAsync`/`RemoveRightsAsync`. Rights were persisted
  and shown in the UI list, but never actually granted live permissions — build and tests stayed green.
- Applies to: room rights, roles, or any permission-like state cached on a grain.
- A DB write + a UI-notification composer is not proof the feature works. Verify the corresponding live
  check (e.g. `GetControllerLevelAsync`) reflects the change within the same session, not just after a
  grain reactivation.

## Layers (spec §3.4 bis)
The solution is cut into three layers, and references only point down:
- **Core** — `Networking`, `Pipeline`, `Protocol`, `Messages`, `Revisions`, `PacketHandlers`,
  `Runtime`, `Crypto`, `Database`, `Events`, `Logging`, `Observability`, `Plugins`, `Primitives`.
- **Platform** — the game's domains: `Rooms`, `Players`, `Furniture`, `Permissions`.
- **Web** — `WebApi`, `WebApi.Authentication` (registration, login, avatar, SSO ticket).

Core references no Platform or Web project; Platform references no Web project. `Vortex.Main` is the host and wires them all. A type two layers both need goes down
into `Vortex.Primitives` (interfaces, snapshots, config keys) — never a reference upward. Wall 8 of
`scripts/hooks/check-architecture-walls.mjs` enforces it, inside the FastCheck.

## Profile and grain flow constraints
- Keep packet handlers orchestration-only:
  - validate input
  - call grains through canonical grain-factory access patterns
  - map snapshot data to outgoing composers
- Do not query database contexts or repositories directly from packet handlers.
- Keep persistence access in grains/services/providers that own domain state.
- Do not use ad-hoc grain key strings in handlers when extension-based access exists.
- Do not add silent `catch` blocks in handlers.
- When snapshot fields are available, map them into composer payloads instead of TODO placeholders.
- For incoming message records in `Vortex.Protocol/Messages/Incoming/**`, keep mandatory fields explicit (use `required` where appropriate) instead of default-fallback contracts.

## Session and room routing constraints
- Connection/session lifecycle starts in gateway flow; do not duplicate session registration in handlers.
- Post-SSO session attachment goes through `PlayerPresenceGrain` (one active presence grain per player id).
- Player-targeted outbound flow must be:
  - resolve player presence grain
  - call `SendComposerAsync`
  - rely on presence fan-out to subscribed sessions
- Do not send directly to raw sockets/session transports from packet handlers.
- Active-room membership/discovery belongs to `RoomDirectoryGrain`; do not bypass it with ad-hoc room tracking.
- Grain lifetime remains Orleans-managed by default; use `[KeepAlive]` only for explicitly justified directory/manager grains.

## Packet addition checklist (revision work)
When adding packet mappings in `Vortex.Revisions/Revision20260701`:
1. Update `Vortex.Revisions/Revision20260701/Headers.cs`:
   - add/update incoming `MessageEvent` id constants
   - add/update outgoing `MessageComposer` id constants
2. Add parser class under:
   - `Vortex.Revisions/Revision20260701/Parsers/<Domain>/*MessageParser.cs`
3. Add serializer class under:
   - `Vortex.Revisions/Revision20260701/Serializers/<Domain>/*MessageComposerSerializer.cs`
4. Register mappings in the domain's map, `Vortex.Revisions/Revision20260701/Maps/<Domain>Map.cs`:
   - incoming: `builder.MapParser(MessageEvent.X, new XParser())`
   - outgoing: `builder.MapSerializer(typeof(XComposer), new XComposerSerializer(MessageComposer.X))`
5. A new map is listed in `Revision20260701.cs`; a packet with no parser is ignored with a warning,
   not a disconnect.

## Task recipes

### Add packet handler
- Required context files:
  - `AGENTS.md`
  - `CONTEXT.md`
  - `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs`
- Required references:
  - one handler in same domain under `Vortex.PacketHandlers/<Domain>/`
  - related incoming message type under `Vortex.Protocol/Messages/Incoming/**`
- Forbidden changes:
  - no direct DB access in handler
  - no direct session/socket sends
  - no ad-hoc grain key literals when extension methods exist
- Validation:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`

### Change grain behavior
- Required context files:
  - `AGENTS.md`
  - `CONTEXT.md`
  - target grain interface in `Vortex.Primitives/**/Grains/*.cs`
- Required references:
  - existing grain in same module
  - related snapshot/state types in `Vortex.Primitives/Orleans/Snapshots/**` or `States/**`
- Forbidden changes:
  - no handler-layer fallback logic that bypasses grain ownership
  - no lifecycle changes that abuse `[KeepAlive]` without infrastructure justification
- Validation:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`

### Add message/composer mapping
- Required context files:
  - `AGENTS.md`
  - `CONTEXT.md`
  - neighboring message/composer classes
- Required references:
  - incoming message under `Vortex.Protocol/Messages/Incoming/**`
  - outgoing composer under `Vortex.Protocol/Messages/Outgoing/**`
  - handler using same message family
- Forbidden changes:
  - no placeholder payload collections when source snapshot data exists
  - no implicit default-fallback contracts for mandatory incoming fields
- Validation:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`

### Refactor lookup/cache logic
- Required context files:
  - `AGENTS.md`
  - `CONTEXT.md`
  - current lookup owner grain/service
- Required references:
  - existing set/invalidate methods
  - all reverse-lookup call sites
- Forbidden changes:
  - no one-way cache updates; forward/reverse mappings must stay coherent
  - no loss of case-insensitive semantics for username lookups
- Validation:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`

## Required validation before completion
```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```

Beyond build, tests, csharpier and the analyzers, these two targets also run the checks that no
compiler sees. They are ordinary node scripts — run any of them alone while you work:

| Check | Target | Catches |
|---|---|---|
| `node scripts/hooks/check-architecture-walls.mjs` | FastCheck | a handler reaching for the database, a protocol import in the contracts hub, a protocol-free project that starts referencing the protocol, a reference that points up the layers |
| `node scripts/hooks/check-webapi-forwarding.mjs` | FastCheck | a `[FromServices]` type the web API's own container never receives — 500 per request, forever |
| `node scripts/hooks/__test/run.mjs` | QualityGate | a hook that stopped blocking what it was written to block |

## Definition of done for AI changes
- All modified files match nearby patterns and contract rules.
- Quality gates pass with no new warnings introduced by the change.
- Architecture invariants for touched areas are explicitly confirmed in PR.
- Edge/failure behavior is addressed for logic changes.
- Any context-rule updates needed by the change are included in the same PR.

## PR expectations for AI-assisted work
- Disclose AI usage and major generated sections.
- Be able to explain complex generated logic in your own words.
- Include verification of at least one edge/failure scenario when behavior changes.
