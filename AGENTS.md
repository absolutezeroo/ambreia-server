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
  - Trigger: editing `Vortex.Primitives/Messages/Incoming/**` or outgoing composer payload mappings.
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
- For revision compatibility work, prefer restoring/adding missing incoming message contracts in `Vortex.Primitives/Messages/Incoming/**` before mutating serializer/composer payload behavior.
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
When checking status on N grains (e.g. online status for a friend list), do not `await` each one in a `foreach`.
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
Handlers already read configuration values (e.g. `Vortex:FriendList:UserFriendLimit`) from `IConfiguration` and pass them to grains.
Magic numbers like `Take(50)`, `Take(20)`, or `maxIgnoreCapacity = 100` must come from configuration parameters on the grain interface method.
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
- Example: each player gets a `PurchaseGrain` so catalog purchases are serialized per-player with no locks needed.
- Example: limited-edition items should use a dedicated grain (e.g. `LimitedItemGrain`) so concurrent buyers are safely serialized.
- Do not add manual locking (`lock`, `SemaphoreSlim`) inside grains — that fights the actor model.

### Grains orchestrate their own outbound communication
When grain state changes (e.g. wallet balance updates), the grain itself sends the snapshot to `PlayerPresenceGrain.SendComposerAsync`. The caller that triggered the change does not pass or send the composer — the grain owns that responsibility.
- **Correct**: handler calls `grain.UpdateWalletAsync(...)` → grain updates state → grain calls `PlayerPresenceGrain.SendComposerAsync(...)`.
- **Wrong**: handler calls `grain.UpdateWalletAsync(...)` → handler builds composer → handler sends composer to player.

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
- Applies to: room rights, moderation flags/mutes/bans, club/subscription tiers, or any permission-like
  state cached on a grain.
- A DB write + a UI-notification composer is not proof the feature works. Verify the corresponding live
  check (e.g. `GetControllerLevelAsync`) reflects the change within the same session, not just after a
  grain reactivation.

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
- For incoming message records in `Vortex.Primitives/Messages/Incoming/**`, keep mandatory fields explicit (use `required` where appropriate) instead of default-fallback contracts.

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
4. Register mappings in:
   - `Vortex.Revisions/Revision20260701/Revision20260701.cs`
   - incoming: `Parsers` dictionary with `MessageEvent` key
   - outgoing: `Serializers` dictionary with composer type + `MessageComposer` id
5. Ensure the required `using` directives are present in `Revision20260701.cs` for new parser/serializer namespaces.

## Task recipes

### Add packet handler
- Required context files:
  - `AGENTS.md`
  - `CONTEXT.md`
  - `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs`
- Required references:
  - one handler in same domain under `Vortex.PacketHandlers/<Domain>/`
  - related incoming message type under `Vortex.Primitives/Messages/Incoming/**`
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
  - incoming message under `Vortex.Primitives/Messages/Incoming/**`
  - outgoing composer under `Vortex.Primitives/Messages/Outgoing/**`
  - handler using same message family
- Forbidden changes:
  - no placeholder payload collections when source snapshot data exists
  - no implicit default-fallback contracts for mandatory incoming fields
- Validation:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`

### Add dashboard capability or admin page
A dashboard capability string is duplicated across **four** files. The server half is now
self-checking: `Capabilities.Dashboard.All` is the one list, the policy builder and the login gate
both read it, and `CapabilityDeclarationTests` fails the build if a declared `dashboard.*` constant
is missing from it. The client half is checked by `scripts/hooks/check-dashboard-capabilities.mjs`
— nothing in `dotnet build`, `dotnet test` or `npm run build` looks at it, so that script is the only
thing standing between a missed edit and a page that is silently hidden from every operator. It runs
automatically after any edit to one of the four files (PostToolUse hook in `.claude/settings.json`),
inside `VortexCloudFastCheck` so a commit from any author or tool is covered, and by hand at any
time.
- Required edits for every new capability:
  1. `Vortex.Primitives/Permissions/Capabilities.cs` — the `const string` **and**
     `Capabilities.Dashboard.All` (the test catches a miss; `Capabilities.All` picks it up from
     there, and both `DashboardWebHost` and `DashboardAuthService` read it — do **not** reintroduce
     a per-file copy, that duplication is what used to throw
     `InvalidOperationException: The AuthorizationPolicy named '<capability>' was not found`)
  2. `Vortex.Dashboard.Web/src/lib/dashboardPermissions.js` — `CAPABILITIES` **and**
     `ROUTE_PERMISSIONS`
  3. `Vortex.Dashboard.Web/src/lib/routes.js` — route row with a `load: () => import(...)` thunk
     (pages are code-split; only the overview and access-denied fallback are imported eagerly, and
     the import path must be a literal or Vite cannot split it)
  4. `Vortex.Dashboard.Web/src/lib/locales/en.js` **and** `fr.js` — page block **and** `nav.*` labels
     (the two files must stay structurally identical; `en.js` is every other locale's fallback)
- Required context files:
  - `docs/architecture/dashboard-architecture.md` — the owner's architecture contract for
    `Vortex.Dashboard.API`, and the shape to preserve for every new feature. It outranks
    case-by-case judgement: a change that breaks one of its rules is to be redone even if it
    compiles and the tests pass. Read it before moving anything in that project. §11-13 in
    particular: what decides how a mutation is written is who owns the mutable state, not which
    assembly the call sits in.
  - `docs/architecture/dashboard-architecture-rules.md` — the 41 rules that governed dismantling the
    god services. That migration is **complete**; the file is kept for the reasoning behind rules
    still cited by name (§13, §24, §33-35). Where the two disagree, the file above wins.
  - `docs/architecture/dashboard-admin-coverage.md` — what an operator can actually act on, and the
    slices that would close the gap. Read it before adding an admin operation: the surface is strong
    on authoring content and thin on acting for a player, and the entity you are about to touch is
    probably already listed as read-only or absent. Numbers regenerate with
    `node scripts/dashboard-admin-coverage.mjs`.
  - `docs/walkthroughs/add-a-dashboard-page.md` (full walkthrough, server + front end)
  - an existing pair such as `DashboardEndpoints.Quests.cs` + `DashboardApiService.Quests.cs`
- Any page that shows or selects furniture, a player, or a group MUST use the existing surfaces --
  this has been missed on every new page so far and is not a detail, it is the difference between a
  usable page and an unusable one:
  - **Show the real artwork**, never a bare id: the read API adds
    `furnitureIconUrl = BuildFurniIconUrl(name)` (see `DashboardApiService.Catalog.cs`) and the page
    renders it with `<AssetImage src={...} />`. Same for avatars and guild badges via
    `DashboardAssetUrls`.
  - **Never make an operator type an id**: use `<PickerModal kind="furniture" />` (or `kind="user"`),
    backed by `/api/v1/directory/furniture`. A number input alone means looking the id up elsewhere
    first.
- **Every create and every edit goes in `<Drawer>`. No exceptions, and never a form panel spliced
  into the page.** This one is missed on almost every new page, including by people who had just
  read the component: an inline form under the table pushes the list off screen, so you lose the
  thing you were looking at when you decided to change a number, and a row's inline editor breaks
  the grid around it. The drawer keeps the list beside the form and pins Save where it cannot scroll
  away. Pattern: `let drawer = $state(null)`, a `New …` button in the `panel-head` that sets
  `{ mode: 'create', form: empty() }`, a row action that sets `{ mode: 'edit', id, form: {...row} }`,
  and `{#snippet actions()}` for the buttons. Modals stay for short confirmations
  (`ConfirmReasonModal`) and pickers — those are answers, not editing.
- **Filters get their own row above the table**, never the `panel-head` and never mixed in with the
  page's actions. `<TableFilter bind:query shown total />` for a table already loaded in full; a
  plain `<div class="filters">` with the inputs when the search is the server's. The heading says
  what the table is; the filter row says what you are narrowing it to.
- **Buttons: an add is `class="success"` (green), a destructive one `class="danger"`, a secondary one
  `class="ghost-button"`, a refresh `class="warning"` — and none of them carry an icon.** Label only.
  The colour is the affordance; a lucide glyph beside the word is noise this dashboard does not use.
- Forbidden changes:
  - no direct DB writes from `DashboardOperationsService`; route through an `I<Domain>AdminService`
  - no admin write that skips reloading the manager-grain cache it feeds
  - no new endpoint left unregistered in `DashboardEndpoints.cs`
- Validation:
  - `node scripts/hooks/check-dashboard-capabilities.mjs` — cross-checks `Capabilities.cs` against
    `dashboardPermissions.js`, every `ROUTE_PERMISSIONS` key and page import path named by
    `routes.js`, and `en.js`/`fr.js` structural parity including the `nav.*` keys
  - `grep -rn "<an existing capability string>" --include=*.cs --include=*.js .` and confirm the new
    capability appears in the same set of files
  - `cd Vortex.Dashboard.Web && npm run lint` — `npm run build` does not catch an undefined
    identifier used in markup
  - the SPA is built into `Vortex.Dashboard.API/Assets/`, which is **generated and gitignored**:
    `dotnet build` runs Vite itself, so there is nothing to commit and nothing to hand-build
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`
  - restart the emulator — a running `Vortex.Main` holds its own DLLs, so a rebuilt binary is not
    live until it is restarted

### Add a room game (Freeze, Battle Banzai, football, and the next one)
Every room game is rules and nothing else. The framework in `Vortex.Rooms/Games/` owns the
lifecycle, the teams, the scores, the arena index, the client IO and the match identity, so adding a
game is **a new folder and a `[RoomGame]` attribute** — no file outside that folder changes, and
nothing that starts or stops a round learns the game's name.
- Required context files:
  - `docs/walkthroughs/add-a-room-game.md` (full checklist and the non-obvious rules)
  - `Vortex.Rooms/Games/Abstractions/IRoomGame.cs` and `IRoomGameContext.cs`
  - `Vortex.Rooms/Games/Freeze/FreezeGame.cs` as the worked example
- Required edits:
  1. `Vortex.Rooms/Games/<Game>/<Game>Game.cs` — the module, `[RoomGame]`, `: RoomGameModule`,
     a `GameProfile`, and only the hooks it uses
  2. `Vortex.Rooms/Games/<Game>/Components/*.cs` — one `[RoomObjectLogic]` class per arena furni
     family, `: GameFurnitureLogic` plus the capability interface it plays
     (`IArenaTileComponent`, `ITeamGateComponent`, `IGoalComponent`, `IBallComponent`, …)
  3. `Vortex.Primitives/Server/ConfigKeyCatalog.cs` — the balance keys, so an operator can edit them
  4. `tools/catalog_converter/data/vortex_logics.json` — the logic keys, so furniture can bind
  5. `Vortex.Rooms.Tests/<Game>/` — rules tests on the pure parts, plus one integration test driven
     through `RoomGameRuntime.SignalAsync` with real components (`GameFurni.Place`)
- Use the shared bricks, never a private copy:
  - lifecycle: `GamePhase` + `GameStateMachine`. A module has NO `IsRunning` of its own —
    `RoomGameModule.IsLive` / `.HasMatch` read the runtime's phase
  - teams and scores: the arena's `TeamBook`, keyed by the game's own `TeamId`s over the `TeamSet`
    its profile declares — two teams, four, seven, or teams with no Habbo colour at all. Written
    through `_context.ScoreAsync` with a `GameScore(team, player, amount, reason, source)`; gates go
    through `TeamGateRules.Toggle`
  - Habbo colours: `_context.Palette` (`HabboTeamPalette`) is the ONLY place a colour becomes a team.
    A gate/goal/board reports the colour it is painted, the module asks the palette which of its own
    teams that is. `GameTeamColor` never appears in a rule, a score or an event
  - arena lookup: `_context.Arena` (`ComponentsOf<T>` / `CountOf<T>` / `OnTile<T>` / `TilesOf<T>`),
    a filtered view over the room's ONE item index — never a scan of `ItemsById`
  - client IO: `_context.Chrome` (`IGameChrome`). From a participant-left hook, only
    `SetPlayingModeAndForget` — an awaited call back into the leaver's own presence grain deadlocks
    the room's turn
  - balance: `_context.GetConfigAsync` in ONE grain round trip at prepare
  - randomness: `_context.Random`, seeded per match — never `Random.Shared`
  - colour-family furni (gates, scoreboards, goals) is ONE logic class with one `[RoomObjectLogic]`
    attribute per colour key and `GameColorKey.FromKeySuffix` — never a shell subclass per colour
- Forbidden changes:
  - no per-game member on `IRoomObjectContext`, and no per-game access interface. Arena furniture
    reports a `GameSignal`; the runtime routes it. This is the rule the old
    `IRoomFreezeAccess`/`IRoomBanzaiAccess` pair broke
  - no `GameTeamColor` in the domain — not as a team identity, not as a score key, not on an event.
    A colour is presentation; teams are `TeamId` over the game's own `TeamSet`
  - no team book captured in a FIELD INITIALISER; the arena's book is bound after the module is
    constructed (which book depends on the teams the module declares), so read `_context.Teams`
    where you need it. `ArenaHost.Teams` throws rather than hand out a null
  - no per-game teams or scores kept privately alongside the book; a second store is invisible to
    the scoreboards and every other reader of the book
  - no direct `TeamBook` score write; go through `_context.ScoreAsync` or `SCORE_ACHIEVED`
    never fires
  - no clearing scores in `OnPreparingAsync`; the runtime already did it before GAME_STARTS was
    published, and doing it again silently wipes what that event just awarded
  - no assuming a start reaches you. A start resolves to ONE arena (`GameTargetResolver`: the named
    game, else the requesting furni's own arena, else the arena it stands nearest to, else the only
    candidate) and refuses an ambiguous room outright. A game that wants several playfields in one
    room sets `GameProfile.ArenaSeparation`; the default of 0 is one arena per room, which is all the
    client can address
  - no ending your own match; call `_context.RequestMatchEndAsync`, which ends the room's round,
    fires GAME_ENDS and resets the timer furni
  - no registration line in `RoomGrain`, and no new hardcoded game call in `FurnitureGameTimerLogic`
  - no deferred work without `_context.Match` on it; that stamp is what stops a callback from match
    N mutating match N+1

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
| `node scripts/hooks/check-dashboard-capabilities.mjs` | FastCheck | a hidden page, an undefined route guard, a chunk that 404s, a raw i18n key in the sidebar |
| `node scripts/hooks/check-logic-groups.mjs` | FastCheck | a `[RoomObjectLogic("...")]` key the admin's furniture dropdown does not offer, so no operator can assign the behaviour. `--write` regenerates the block |
| `cd Vortex.Dashboard.Web && npm run lint` | QualityGate | an undefined identifier in Svelte markup, which `npm run build` compiles and ships |
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
