# Vortex Cloud — Beta readiness audit

- **Audited revision**: `04550328f8d696358d477cc835c7d86dffb2b8bc` (`main`, 2026-09-14 05:31 +0200), working branch `claude/vortex-cloud-beta-audit-apl5gn`.
- **Audit date**: 2026-09-14.
- **Nature**: read-only technical and architectural audit of the code, complemented by compilation, running the test suite, targeted checks and reading the continuous integration logs. No production file was modified.
- **Author**: Claude (audit session), at the request of the repository owner.

---

## 1. Overall verdict

**Not ready as it stands.** The project becomes "ready under conditions" once the five P1 defects are fixed, the quality gate is back to green and the P2 operational prerequisites are in place (sections 10 and 11).

This verdict rests on three established facts, not on an impression:

1. **Two room entry permission bypasses and one loss of player data are demonstrated by the code** (ROOM-01, ROOM-02, ROOM-03): a friend can be followed into a locked, password-protected or full room; a doorbell ringer can enter without the owner answering, using a forged packet; deleting a room permanently abandons every piece of furniture it contains.
2. **A player connected but silent for more than two minutes becomes a ghost** (SES-01): removed from their room, invisible to their friends, then mute to the server until reconnection. A backgrounded browser tab is the most common trigger.
3. **The quality gate has been red on `main` for at least eight commits and pushes are done with `--no-verify`** (QA-01). The safety net the repository built for itself no longer protects anything until that is repaired.

Conversely, the economic foundations are solid and were verified end to end: transactional conditional debit, commerce journal with an explicit pivot, conditional claims on furniture ownership (trade, marketplace, placement, pickup), moderation authorizations present in 23 handlers out of 23. The test suite (3,869 tests, 0 failures) and the build are healthy. The beta's problem is not the general quality of the code, which is high; it is a few precise holes in essential journeys and the absence of an operational base.

What the audit **could not validate** and which weighs on the verdict: no MySQL database was available (no run of the emulator, no migration applied, no load test), the official client's sources are not alongside the repository (the wire format drift check is blind), and the eight planned domain agents were interrupted by an API limit: the audit was carried out manually, with explicitly partial coverage on several domains (section 4).

---

## 2. Context, environment and assumptions

| Item | Observed value |
|---|---|
| Stack | .NET SDK 10.0 (`global.json`, `rollForward: latestFeature`), C# `net10.0`, Orleans 10.2.1, EF Core 9.0.8 + Pomelo MySQL 9.0.0 (deliberate pin, `AGENTS.md`), SuperSocket 2.1.0, ASP.NET Core minimal APIs, Svelte/Vite (dashboard) |
| Size | 63 projects in `Vortex.Cloud.sln`; ~480,000 lines of C# excluding migrations (Rooms 66k, Dashboard.API 50k, Primitives 33k, Revisions 28k, PacketHandlers 27k); 302 migration files; 557 packet handlers; 54 Orleans grains; 1,240 parsers and 1,143 protocol files |
| Tests | 18 test projects, 3,869 tests run locally, 0 failures (section 9) |
| Audit environment | Linux container with no preinstalled .NET SDK: SDK 10.0.401 installed in `/root/.dotnet`; Node 22.22; **no MySQL server**; Docker daemon unreachable; no AS3 sources and no reference emulators alongside the repository |
| Git history | 50 visible commits (12→14 September 2026), re-imported history: `docs/codebase/.documentation-state.json` references a commit absent from the local history |
| Deployment assumption | **Single-silo** thesis assumed by the repository (`CONTEXT.md`, `VortexEmulator.RefuseAnUndeclaredSecondSiloAsync`, `Vortex.Main/VortexEmulator.cs:36-70`): in-memory caches, aggregators and streams are process-local. The audit assesses the beta on a single node |
| Client assumption | Target client WIN63-202607011411 (`docs/completeness/STATE.yaml`), TCP (Flash) and WebSocket (browser) transports; RSA-1024/DH-384/RC4 encryption imposed by the client |
| Excluded scope | The sample plugin (`../turbo-sample-plugin`, outside the repository), the content of the Habbo specs (3,923 generated files, validated but not read), the room games (Freeze/Banzai/Football) and the pets beyond their structure |

Confidence conventions used throughout this document:

- **Confirmed**: demonstrated by the code read (file:line cited) or reproduced by a command.
- **To verify**: an argued risk one of whose conditions (runtime, database, client) could not be checked in this environment.
- **Improvement**: a design proposal with an explicit benefit, with no demonstrated defect.

Priorities: P0 critical, P1 beta-blocking, P2 important, P3 minor (definitions from the audit request).

---

## 3. Map

### 3.1 Components and responsibilities

| Layer | Projects | Role verified in the code |
|---|---|---|
| Host | `Vortex.Main` | Composition root (`Program.cs`): invariant culture, `VORTEX__` configuration, `AddOrleans` (`Extensions/HostApplicationBuilderExtensions.cs`), 21 `AddHostPlugin<T>` modules, optional migrations at startup (`Program.cs:198`), `VortexEmulator` (loading reference data in tiers then opening the network), console commands |
| Network | `Vortex.Networking`, `Vortex.Crypto` | Two SuperSocket hosts (TCP 30000, WS 30001) built by `NetworkManager`; `SessionGateway` (session↔player dictionaries, one Orleans observer per session); `ClientPacketDecoder` (RC4, 64 KB bound); `PackageHandler` (revision parser → `MessageSystem`); RSA/DH handshake → RC4 |
| Pipeline | `Vortex.Messages`, `Vortex.Pipeline`, `Vortex.Runtime` | `MessageSystem.PublishAsync` (trace scope, metrics, active room resolution by grain call) → `MessageRegistry` (`EnvelopeHost`, per-packet context, handlers in parallel); `RateLimitBehavior` (per-session token bucket); assembly/plugin loading |
| Protocol | `Vortex.Protocol`, `Vortex.Revisions`, `Vortex.Primitives/Messages` | Read primitives (`ClientPacket`), embedded revision `Revision20260701` (parsers, serializers, `Headers.cs`), message contracts |
| Handlers | `Vortex.PacketHandlers` (43 domains) | Orchestration: entry guard, grain call via `GrainFactoryExtensions`, response via `ctx.SendComposerAsync` or `PlayerPresenceGrain.SendComposerAsync` |
| Domains | `Vortex.Players`, `Rooms`, `Catalog`, `Inventory`, `Marketplace`, `Social`, `Progression`, `RewardTracks`, `Habbicons`, `Collectibles`, `Fishing`, `Navigator`, `Furniture`, `Authentication`, `Shop` | 54 grains + services; live state in memory in the grains, persistence through EF Core via `IDbContextFactory<VortexDbContext>` (no Orleans `[PersistentState]`) |
| Persistence | `Vortex.Database` | `VortexDbContext` (31 entity folders), 302 migrations, global soft-delete filter, `CommerceJournal` (operations/receipts), `mysqldump` backup, entity change audit (dashboard) |
| HTTP surfaces | `Vortex.WebApi` (site: registration, login, SSO ticket, avatars, articles, `/health`, `/metrics`), `Vortex.Dashboard.API` + `Vortex.Dashboard.Web` (operator control plane: server sessions, MFA, capabilities, audit), `Vortex.Supervisor` (token-protected supervisor process) | Separate Kestrel hosts (8080 / 9000 / 5250), separate DI containers |
| Observability | `Vortex.Observability`, `Vortex.Logging` | `System.Diagnostics.Metrics` metrics (28 Vortex instruments) exposed to Prometheus, correlation context, error grouping, live aggregators, incident detection, audit; console logging only |
| Tooling | `Vortex.Specs`, `Vortex.Specs.Cli`, `Vortex.Benchmark`, `Vortex.LoadGen`, `scripts/hooks/*` | Generated behavioural specs, micro-benchmarks, out-of-process load generator, seven non-compiler checks (capabilities, header registry, architecture walls, logic groups, wire conflicts, hooks) |

### 3.2 Orleans grains (54) and state ownership

| Family | Grains | Key | Lifetime |
|---|---|---|---|
| Player | `PlayerGrain` (profile, moderation, wardrobe, preferences), `PlayerPresenceGrain` (session ↔ room routing, outgoing queue), `PlayerWalletGrain`, `PlayerClothingGrain`, `PlayerEffectGrain`, `PlayerNavigatorGrain`, `PlayerMysteryBoxGrain`, `InventoryGrain`, `MessengerGrain`, `PlayerQuizGrain`, `Player*` (achievements, badges, quests, tasks, polls, prizes, reward tracks, habbicons, fishing, NFT, vault, mint) | player id | Orleans (`GrainCollectionAge` = 2 min, `OrleansHostConfig.cs:23`) |
| Room | `RoomGrain` (8,200 lines across 38 partials + 15 modules + 30 systems), `RoomPersistenceGrain` (2 s write-behind), `RoomDirectoryGrain` (`[KeepAlive]`, 5 min sweep) | room id | `DelayDeactivation` 30 min on every entry, `DeactivateOnIdle` by the sweep when the population is zero (`RoomGrain.cs:228-235`, `RoomDirectoryGrain.cs:177`) |
| Commerce | `CatalogPurchaseGrain` (per player), `LtdRaffleGrain` (per series), `VoucherGrain` (per code), `MarketplacePurchaseGrain`, `MarketplaceSearchGrain`, `PlayerTargetedOfferGrain`, `TargetedOfferManagerGrain` | player / series / code | Orleans |
| `[KeepAlive]` singletons (16) | `PlayerDirectoryGrain`, `RoomDirectoryGrain`, `ModerationQueueGrain`, `GuideDirectoryGrain`, reference managers (quests, achievements, polls, prizes, community, NFT, fishing, mystery box, targeted offers) | `global` | silo lifetime |
| Social | `GroupGrain`, `GroupDirectoryGrain`, `GroupForumGrain`, `MessengerGrain` | group / player id | Orleans |

Concurrency: no `[Reentrant]`, six interleaved methods recorded in `docs/architecture-v4/interleaving-manifest.yaml` (2 × `IPlayerPresenceGrain.SendComposerAsync`, 4 pure reads on `IRoomCore`), asserted by `Vortex.Hosting.Tests/Architecture/InterleavingManifestTests.cs`. No manual lock, no `.Result`/`.Wait()`, no `ConfigureAwait(false)` in the grains (verified by grep, section 9).

### 3.3 Main flows (as implemented)

1. **Connection and authentication**: socket accepted → `SessionGateway.AddSessionAsync` (Orleans observer created) → `ClientHello`/`InitDiffie`/`CompleteDiffie` (RC4 enabled) → `SSOTicket` → `AuthenticationService.GetPlayerIdFromTicketAsync` (ticket in DB, 30 s sliding TTL) → ban checked → `SessionGateway.AddSessionToPlayerAsync` (the player's old session closed, `PlayerPresenceGrain.RegisterSessionObserverAsync`) → 15 grain reads in parallel → login composer sequence. Throughout this handler `ctx.PlayerId` is −1 (the context is built before the binding).
2. **Packet dispatch**: `PackageHandler.HandleCoreAsync` → revision parser → `MessageSystem.PublishAsync` (**one `GetActiveRoomAsync` call to the presence grain per packet** for the metrics, `MessageSystem.cs:50,85`) → `MessageRegistry` (context `PlayerId` = −1 if unauthenticated, `MessageRegistry.cs:48`) → `RateLimitBehavior` (50 packets/s, burst 100, per session) → handler(s).
3. **Room entry**: `OpenFlatConnection` → `RoomService.OpenRoomForPlayerIdAsync` (`RoomService.cs:60-150`): room ban (DB) → `ClearActiveRoomAsync` → `SetPendingRoomAsync(roomId, approved: true)` (**before** the checks, line 93) → `EnsureRoomActiveAsync` → room full / password (for someone without rights) → locked door: `RegisterDoorbellRingAsync` and return → otherwise `CompleteRoomEntryAsync`: cancellable event, raid protection, `RoomReady` + heightmaps + furniture + avatars sent, then **last** `PlayerPresenceGrain.SetActiveRoomAsync` (directory, subscription to the `RoomStream/<id>` stream, `RoomGrain.CreateAvatarFromPlayerAsync`).
4. **Room output to the clients**: `RoomGrain.SendComposerToRoomAsync` → Orleans in-memory stream → each occupant's `PlayerPresenceGrain.OnNextAsync` → bounded queue (500) → observer → socket. A room never writes to a socket.
5. **Catalog purchase**: `PurchaseFromCatalog` → `CatalogPurchaseGrain.PurchaseOfferFromCatalogAsync` (`CatalogPurchaseGrain.cs:45-244`): quantity bounded (`:62`), offer resolved server-side, price recomputed, cancellable event, `Prepared` journal → `WalletPurchaseExtensions.ExecutePurchaseAsync` → `PlayerWalletGrain.TryDebitAsync` (execution strategy + transaction + `UPDATE ... WHERE Amount >= cost`, `PlayerWalletGrain.cs:66-135,335-400`) → `InventoryGrain.GrantCatalogOfferAsync` (**pivot** = a single `SaveChangesAsync`, `InventoryGrain.Furni.cs:161-260`, ADR-001) → `Pivoted` → `CompleteWithRelayAsync` (event written with the terminal transition, relayed by `CommerceRelayService` if publication fails).
6. **Furniture**: placement = `CanManipulateFurniAsync` or rented space → conditional claim `RoomFurnitureLocationStore.ClaimIntoRoomAsync` → room state → removal from the inventory view (`RoomActionModule.Floor.cs:26-130`); pickup = `GetFurniPickupTypeAsync` (≥ GroupAdmin, returns to the owner) → conditional `ReleaseFromRoomAsync` → inventory view (`RoomActionModule.cs:36-110`); positions/rotations/extra data write-behind 2 s by `RoomPersistenceGrain` (loss window documented in `docs/architecture-v4/persistence-loss-window.md`).
7. **Trading**: `RoomTradingSystem` → `TryPersistOwnershipSwapAsync` (`RoomTradingSystem.cs:566-660`): execution strategy + transaction, conditional `ClaimForTradeAsync` (owner, out of room, out of wired chest, out of jukebox, not deleted), counts verified, NFT Relics in the same transaction with a ledger row.
8. **Marketplace**: listing = offer row `PendingRemoval` → removal from the view → **conditional soft-delete claim** of the furniture row (`MarketplacePurchaseGrain.cs:147-200`) → `Active`. The "known issue" recorded in `docs/codebase/06-economy/marketplace.md` (row not durably removed) is **fixed at HEAD**.
9. **Shutdown**: `VortexEmulator.StopAsync` stops both network hosts (5 s each); Orleans then deactivates the grains (`RoomGrain.OnDeactivateAsync`: stopping games, draining furniture and pets, removal from the directory; `PlayerGrain.OnDeactivateAsync`: profile write). `HostOptions.ShutdownTimeout` is not configured (30 s default).

---

## 4. Audit coverage

The planned domain agents (the repository's eight specialized researchers) were all interrupted by an API session limit before reading a single file. The audit was therefore carried out manually, prioritizing the essential journeys and the economic invariants. The matrix below states precisely what was read.

| Domain | Status | What was analyzed | What was not |
|---|---|---|---|
| Orleans runtime | **Analyzed** (partial over the 54 grains) | Silo configuration, `PlayerPresenceGrain` (5 partials), `RoomGrain.cs` (activation, tick, deactivation, hydration), `RoomPersistenceGrain`, `RoomDirectoryGrain`, `PlayerGrain` (deactivation, write), `PlayerWalletGrain`, `CatalogPurchaseGrain`, `VoucherGrain` (redemption), `MarketplacePurchaseGrain` (listing, redeem), interleaving manifest, grep for locks/blocking/`.Ignore()` | `MessengerGrain` (bounds only), `GroupGrain`, `ModerationQueueGrain`, `LtdRaffleGrain`, progression/collectibles/fishing/habbicons grains (structure only) |
| Connections and sessions | **Analyzed** | `SessionGateway`, `SessionContext`/`WebSocketSessionContext`, `NetworkManager` (WS heartbeat), `PackageHandler`, `ClientPacketDecoder`, `MessageSystem`, `MessageRegistry`, `RateLimitBehavior`/`TokenBucketRateLimiter`, `EnvelopeHost` (dispatch), handshake (6 handlers), `SSOTicketMessageHandler`, `AuthenticationService`, WebApi ticket generation, `DiffieService` | SuperSocket options (defaults not overridden), WS Origin check (absent from the code: to be verified on the proxy side), `PackageEncoder` read but not tested |
| Rooms and gameplay | **Partial** | Entry/exit (service + grain + presence), doorbell, `FollowFriend`, furniture authorizations (place/move/pickup/wired), `RoomSecurityModule` (control level, pickup), room settings/deletion, `RoomChatSystem` (flood, length), `RoomGrain.FloorPlan` (bounds), `RoomConfig` (wired budgets, limits), `RoomTradingSystem` (settlement), `RoomFurnitureLocationStore` | Wired engine (`Vortex.Rooms/Wired/**`, 141 files), games (`Games/**`, 64 files), pets and bots (30 systems), rollers, jukebox, crackables, mystery box, internal raid protection, 105 Room handlers (sample of 8) |
| Economy and inventory | **Analyzed** on the essential journeys | Catalog purchase end to end, wallet (debit, single credit), inventory (grant, view/durable), marketplace (listing, redeem), trading, vouchers, commerce journal and relay, ADR-001/002, V4 acceptance matrix | Gifts, LTD, targeted offers, club, vault, mystery box, crafting, NFT/mint, reward tracks, habbicons, fishing (existing tests read in the acceptance matrix, code not re-read), site shop (`Vortex.Shop`, disabled by default: HMAC signature and tolerance verified by grep) |
| Persistence | **Partial** | EF registration (pool, retry, AutoDetect), transactions and execution strategies (3 sites), unique indexes in the snapshot (tickets, receipts, vouchers, badges, names), global soft-delete filter, `MigrationHelper`, backup, counting of destructive/raw SQL migrations, documented loss window | Reading the 302 migrations, snapshot/model coherence (`has-pending-model-changes` not run: no database), exhaustive column lengths, N+1 queries on the hot paths, real MySQL behaviour (tests on SQLite/InMemory) |
| Security and HTTP surfaces | **Partial** | Dashboard: authentication, per-capability policies, cookie, CSP, login rate limit, MFA step-up, Swagger; WebApi: rate limits, cookie, CORS, registration (name, uniqueness, avatar cap), sessions; `/metrics` (loopback or constant-time token); supervisor (token validated, constant time); secret validators (`CHANGE_ME`); BCrypt hashing (factor 12, `Task.Run`) | Exhaustive enumeration of the dashboard endpoints (spread across partials: only the root file and one accounts file read), endpoint-by-endpoint IDOR, article sanitation, asset upload/traversal, front-end dependencies |
| Protocol and handlers | **Partial** | `ClientPacket` primitives (bounds), grep over the 1,240 parsers (preallocation, loops), 22 parsers bounded by packet size, `RateLimitConfig`, 40 handlers with no authentication guard (list), 27 handlers acting on a client-supplied room id (authorization verified for settings/deletion), 23 moderator handlers (capabilities), motto, `unknowns --severity critical` (127), header and wire-conflict baselines | Reading the serializers (placeholders), classification of the 61 baselined wire conflicts (the AS3 sources are absent), Help/CFH handlers, GroupForums, Camera, Navigator (beyond the `Take` limits) |
| Operations and quality | **Analyzed** | Startup (optional migrations, AutoDetect, validators), shutdown, Dockerfile (non-root, healthcheck), compose (dev), CI (`quality.yml`, last 8 runs, logs of run 34803028084), local gate (csharpier, 5 node checks, hooks, specs, npm lint/test), vulnerability scan, `/health`, metrics, tracing, logging, backup, `LoadGen` (role) | Running the emulator, migrations on MySQL, load test, backup restoration, plugins (load/unload not exercised), real supervision |

---

## 5. Risk summary

### 5.1 What blocks opening (P1)

| Risk | Root cause | Player effect |
|---|---|---|
| A silent player becomes a ghost (SES-01) | The presence grain is the only link between socket and world, but nothing keeps it alive: it is collected after 2 min without a message, and its deactivation undoes the room and the observer | Removed from the room without knowing it, offline to their friends, then every server response lost until reconnection |
| Entering a room without going through the door (ROOM-01, ROOM-02) | The entry guards (ban, full, password, doorbell, raid, cancellable event, capacity) live in `RoomService`, not in the grain; two paths call the grain directly | Private rooms open to friends and modified clients; full rooms exceeded; ghost avatars |
| Room deletion = furniture loss (ROOM-03) | Soft delete of the room without returning the furniture to inventory | A player who deletes a room loses everything it contained, permanently |
| Quality gate bypassed (QA-01) | Three broken checks (header registry in ceiling mode, wire conflicts blind, hook self-tests) make the gate red; commits go out with `--no-verify` | No regression is stopped before `main` any more |

### 5.2 What must be controlled during the beta (P2)

- **Network abuse**: no central authentication guard (40 handlers with no guard, DB queries triggerable before SSO), rate limit per session only, no per-IP cap and no pre-SSO deadline, two parsers allocating from a client-supplied counter (SES-02, NET-01, PROTO-01).
- **Replayable SSO tickets** within a 30 s sliding window, not bound to the IP at use time (AUTH-01).
- **Inconsistent entry states** when avatar creation fails: ghost population, a room ticking at 20 Hz forever (ROOM-04).
- **Operations**: backups disabled and never restored, console logs only, manual post-pivot commerce recovery with no runbook, no production deployment defined, TLS to be terminated in front of the WebSocket (OPS-01 to OPS-04).
- **Persistence**: 302 migrations of which 121 destructive, no test lane on real MySQL, rollback = restore (DB-01).
- **Vulnerable dependency** in four test projects which will fail the dedicated CI step as soon as the gate passes (QA-02).

### 5.3 What is solid (to preserve)

- Money and items: transactional conditional debit, journalled pivot, conditional claims on five ownership paths, idempotent receipts by unique index, at-least-once event relay with deduplication on the consumer side (V4 acceptance matrix verified against the code for catalog, marketplace and trading).
- Moderation and rights: 23/23 moderator handlers check a capability; settings, deletion, placement, pickup and wired check the control level in the grain.
- Control plane: revocable server sessions, `HttpOnly`/`Secure`/`SameSite=Strict` cookie, CSP, per-capability policies generated from the canonical list, MFA step-up, login rate limit, audit.
- Intrinsic quality: 3,869 green tests, clean csharpier, validated specs, analyzers set to errors on the critical diagnostics, mechanized architecture walls.

---

## 6. Findings table

| ID | Title | Category | Priority | Confidence | Main references | Impact |
|---|---|---|---|---|---|---|
| SES-01 | `PlayerPresenceGrain` deactivation after 2 min of inactivity: orphaned live session | Defect | **P1** | Confirmed (code) | `PlayerPresenceGrain.cs:194-226`, `PlayerPresenceGrain.Room.cs:106-165`, `OrleansHostConfig.cs:23`, `HostApplicationBuilderExtensions.cs:95`, `MessageSystem.cs:50,85`, `SuperSocketHostBuilderExtensions.cs:107`, `NetworkingConfig.cs:39` | Player removed from the room, seen as offline, then mute until reconnection |
| ROOM-01 | `FollowFriend` enters the room with no guards and no entry payload | Defect | **P1** | Confirmed (code) | `FollowFriendMessageHandler.cs:84`, `RoomService.cs:60-150`, `RoomGrain.Avatar.cs:24-70`, `RoomAvatarModule.cs:91` | Ban/full/password/doorbell/raid bypass, ghost avatar, desynchronized client |
| ROOM-02 | `GetRoomEntryData` admits a ringer with no answer from the owner | Defect | **P1** | Confirmed (code) | `RoomService.cs:93,144`, `GetRoomEntryDataMessageHandler.cs:46-50`, `header-registry-baseline.json` (id 1250) | Entry into a locked room with a forged packet |
| ROOM-03 | Room deletion: the furniture stays attached to the deleted room | Defect | **P1** | Confirmed (code) | `RoomGrain.Settings.cs:204-266`, `InventoryFurnitureLoader.cs:64`, consumers of `RoomDeletedEvent` | Permanent loss of a deleted room's furniture |
| QA-01 | Quality gate red on `main`, pushes with `--no-verify` | Operational prerequisite | **P1** | Confirmed (CI logs + local reproduction) | runs 316→323 of `quality.yml`, `check-header-registry.mjs:191-204`, `check-wire-conflicts.mjs`, `scripts/hooks/__test/run.mjs` | No regression is stopped; the gate can no longer be required |
| SES-02 | No central authentication guard in the pipeline | Architectural weakness | P2 | Confirmed (code) | `MessageRegistry.cs:48`, 40 handlers (list §7), `RedeemVoucherMessageHandler.cs`, `VoucherGrain.cs:129-175` | DB queries and grain activations for id −1 before SSO; a forgotten guard = a hole |
| NET-01 | Incomplete network abuse limits (per session only) | Risk | P2 | Confirmed (code) | `RateLimitConfig.cs`, `TokenBucketRateLimiter.cs`, `NetworkManager.cs:242`, `appsettings.json` (`serverOptions`) | N connections = N × 50 packets/s; unlimited pre-SSO sessions; WS Origin unchecked |
| PROTO-01 | Preallocation from a client-supplied counter | Defect | P2 | Confirmed (code) | `AcceptFriendMessageParser.cs:14`, `DeclineFriendMessageParser.cs:22` (counter-example `FishingParsers.cs:63`) | Allocation of up to 8 GB attempted from an 8-byte packet |
| AUTH-01 | SSO ticket replayable within the sliding window | Risk | P2 | Confirmed (code) | `AuthenticationConfig.cs:34`, `AuthenticationService.cs:29-130`, `WebApiAuthService.cs:201-250` | Session theft from an observed ticket (30 s sliding, IP not checked) |
| ROOM-04 | `SetActiveRoomAsync` leaves an inconsistent state if avatar creation fails | Defect | P2 | Confirmed (code) | `PlayerPresenceGrain.Room.cs:45-103`, `RoomGrain.Avatar.cs:24-70`, `RoomDirectoryGrain.cs:177` | Ghost population, orphaned stream, room ticking forever |
| QA-02 | Vulnerable test dependency (SQLitePCLRaw 2.1.10, High) | Operational prerequisite | P2 | Confirmed (scan) | `Directory.Packages.props`, `quality.yml:67`, 4 test projects | The "Scan for vulnerable packages" step will fail as soon as the gate passes |
| OPS-01 | Backups disabled by default, no restore ever tested | Operational prerequisite | P2 | Confirmed (code + config) | `appsettings.json` (`Vortex:Database:Backup`), `DatabaseBackupService.cs`, `DatabaseBackupScheduler.cs` | No recovery possible after corruption or an operator error |
| OPS-02 | Console logging only; volume drivable by a client | Operational prerequisite | P2 | Confirmed (code) | `Vortex.Logging/*`, `PackageHandler.cs:88` | No incident history, possible log saturation |
| OPS-03 | Post-pivot commerce recovery manual, with no runbook and no operator action | Operational prerequisite | P2 | Confirmed (code) | `CommerceRelayService.cs` (`EscalateAsync`), ADR-001 ("resuming it is not yet automatic") | A player debited without delivery waits for an undocumented manual intervention |
| OPS-04 | No production deployment defined | Operational prerequisite | P2 | Confirmed (repository) | `docker-compose.yml` (dev), `Dockerfile`, `README.md` "Real secrets outside development", `HostApplicationBuilderExtensions.cs:40-70` | Secrets, WS TLS, single-node mode, migrations: decisions not taken |
| DB-01 | Migration hygiene and absence of a real MySQL lane | Risk | P2 | Confirmed (count) / to verify (MySQL) | `Vortex.Database/Migrations/*` (302, 121 destructive, 37 raw SQL), `scripts/sql/recover_half_applied_*`, SQLite/InMemory tests | Unrecoverable partial migration, untested MySQL behaviours |
| SEC-01 | Accounts: no lockout, web sessions not revoked on ban | Weakness | P2 | Confirmed (code) | `AccountAuthenticator.cs`, `WebApiAppConfigurator.cs:90-140`, `WebApiSessionStore.cs` (comment "Nothing calls it on a ban") | Slow brute force possible; a banned visitor keeps their web session |
| PERF-01 | One grain call per inbound packet for the metrics | Weakness | P3 | Confirmed (code) | `MessageSystem.cs:50,85-113` | Added latency and load on the hot path (movement, chat) |
| NET-02 | TCP with no heartbeat, `PongTimeout` = 0 | Risk | P3 | Confirmed (code) | `SuperSocketHostBuilderExtensions.cs:99-141`, `NetworkingConfig.cs:39` | Dead sessions with no FIN kept, players wrongly "online" |
| ROOM-05 | Entry snapshot sent before the stream subscription | Risk | P3 | To verify | `RoomService.cs:300-360`, `GetRoomEntryDataMessageHandler.cs:189`, `PlayerPresenceGrain.Room.cs:66-79` | Room events lost during entry (ghost avatar, missing furniture) |
| DB-02 | Flush batch poisoned by a physically deleted row | Risk | P3 | To verify | `RoomPersistenceGrain.cs:130-190` | A room's positions never persisted again after a direct SQL deletion |
| PROTO-02 | Lengths not bounded server-side (motto, room name/description); `PlayerId < 0` guard | Weakness | P3 | Confirmed (code) | `ChangeMottoMessageHandler.cs:22`, `PlayerGrain.cs:183`, `RoomGrain.Settings.cs:51-120` | MySQL exceptions logged; content not filtered |
| SEC-02 | Room password in clear; Swagger mounted unconditionally | Weakness | P3 | Confirmed (code) | `RoomGrain.Settings.cs:80`, `RoomGrain.cs` (`Password` snapshot), `DashboardWebHost.cs:664-669` | Exposure on a DB/snapshot leak; discovery surface |
| OBS-01 | Traces not exported; `/health` limited | Weakness | P3 | Confirmed (code) | `VortexTelemetry.cs:8`, `WebApiEndpoints.cs:240-275` | Incomplete diagnosis in production |
| DATA-01 | Unbounded caches and loads | Weakness | P3 | Confirmed (code) | `PlayerDirectoryGrain.cs:26-27`, `InventoryFurnitureLoader.cs:64` | Memory proportional to the number of players looked up / items owned |
| DOC-01 | Drift of the contract documents and the baselines | Weakness | P3 | Confirmed | `docs/orleans.md`, `CONTEXT.md`, `AGENTS.md` ("23" conflicts, "14" ids), `README.md` ("SDK 9.x"), `wire-conflicts-baseline.json` (61), `header-registry-baseline.json` (3) | The contracts steer the AI tools and the reviewers towards false assumptions |
| SPEC-01 | 127 mute handlers (critical unknowns) | Missing feature | P3 | Confirmed (CLI) | `dotnet run --project Vortex.Specs.Cli -- unknowns --severity critical` | Features silently accepted (camera, competitions, campaigns…) |
| ORL-01 | `RoomDirectoryGrain` `[KeepAlive]` with no reaper | Risk | P3 | Confirmed (code) | `RoomDirectoryGrain.cs:69-78`, `docs/codebase/03-orleans/lifecycle-concurrency.md` | Orphaned entries until restart if a room dies without `OnDeactivateAsync` |

---

## 7. Detailed analyses

Each finding follows the same plan: conditions and execution path, behaviour, impact, protections looked for, reproduction or validation, recommended fix.

### SES-01 — `PlayerPresenceGrain` deactivation after two minutes of inactivity (P1, confirmed)

**Path.** `PlayerPresenceGrain` is an ordinary Orleans grain: no `[KeepAlive]`, no `DelayDeactivation` (the repository's only two uses of `DelayDeactivation`/`DeactivateOnIdle` are in `RoomGrain.cs:228-235`). `GrainCollectionOptions.CollectionAge` is 2 minutes by default (`OrleansHostConfig.cs:23`, applied in `HostApplicationBuilderExtensions.cs:95`). An activation with no message for that long is collected, and `OnDeactivateAsync` (`PlayerPresenceGrain.cs:194-226`) empties the queue, calls `UnregisterSessionObserverAsync` (`:111`) which calls `ClearActiveRoomAsync` (`PlayerPresenceGrain.Room.cs:106-165`: avatar removed from the room, `PlayerLeftRoomEvent`, removal from the directory, stream unsubscription), then sets `_sessionObserver` to `null`.

What touches that grain in normal operation: `MessageSystem.ResolveRoomIdAsync` (`MessageSystem.cs:50,85-113`) does a `GetActiveRoomAsync` on **every inbound packet**, and every `SendComposerAsync` addressed to the player. In other words, the grain's life depends on the client's traffic. But:

- the TCP listener has **no heartbeat**: `UsePingPong` is not called (`NetworkManager.cs:242` exists only for the WS) and the TCP builder's `RunHeartbeatAsync` has an emptied body (`SuperSocketHostBuilderExtensions.cs:99-141`, `return Task.CompletedTask` line 107);
- the WS listener sends a `PING` after 30 s of silence, but `PongTimeout` is 0 (`NetworkingConfig.cs:39`) and, by documented choice, a frozen tab that no longer answers is **not** closed.

**Deduced behaviour.** A client whose socket stays open but which sends nothing for more than two minutes (browser tab backgrounded and frozen by Chrome, machine asleep, network cut with no FIN) suffers: removal from the room server-side (the other occupants see them leave), `IsOnlineAsync` = `false` (friends told they are offline) while `SessionGateway._playerToSession` keeps them online, then, on the next reactivation (as soon as the client sends another packet), a fresh grain with `_sessionObserver == null`: `ProcessOutgoingQueueAsync` drains nothing (`PlayerPresenceGrain.cs:230-262`), the queue fills to 500 (`PlayerPresenceConfig.MaxOutgoingQueueSize`) then `EnqueueOutgoing` replaces everything with a `CloseConnectionMessageComposer` (`:150-192`) which is never delivered either. The client is **mute**: it sends, the server processes, nothing comes back. Only a new SSO (`SessionGateway.AddSessionToPlayerAsync`, `SessionGateway.cs:117`) registers an observer again.

**Protections looked for.** No re-registration outside SSO; no `OnActivateAsync` that asks the gateway for the observer again; no periodic reconciliation between `SessionGateway` and the grains; no test in `Vortex.Players.Tests` or `Vortex.Hosting.Tests` activates a `PlayerPresenceGrain` and then waits for collection.

**Reproduction (test environment).** Set `Vortex:Orleans:GrainCollectionAge` to `00:01:00`, connect a WS client, enter a room, block the client's sends (drop the `PONG`s, or suspend the client process) for three minutes, resume: observe `PlayerLeftRoomEvent` server-side then, on the next packet, the absence of any response (`Vortex.packets.dropped` does not move, the internal queue grows). Automatable with `Microsoft.Orleans.TestingHost`: activate the grain, register a fake observer, advance the collection clock, check `IsOnlineAsync`.

**Recommended fix.** Tie the activation's life to the session: `RegisterSessionObserverAsync` calls `DelayDeactivation(TimeSpan.MaxValue)` (or a conditional `[KeepAlive]` through a renewed `DelayDeactivation`) and `UnregisterSessionObserverAsync` calls `DeactivateOnIdle()`; in addition, `OnActivateAsync` queries `ISessionGateway` (already available in DI in the silo) to recover the player's current observer, which makes reactivation self-healing. Add a TestingHost test. See evolution B (§8).

### ROOM-01 — `FollowFriend` bypasses the entry guards and does not send the payload (P1, confirmed)

**Path.** `FollowFriendMessageHandler.cs:84` calls `selfPresence.SetActiveRoomAsync(activeRoom.RoomId, ct)` directly. All the entry guards live in `RoomService.OpenRoomForPlayerIdAsync` / `CompleteRoomEntryAsync` (`RoomService.cs:60-150` then `:152-361`): room ban (`IRoomModerationStore.IsBannedAsync`), room full (`PlayersMax`), password, locked door → doorbell, cancellable `PlayerEnteringRoomEvent`, raid protection (`EvaluateEntryAsync`), and sending `OpenConnection`, `RoomReady`, maps, furniture, avatars. `RoomGrain.CreateAvatarFromPlayerAsync` (`RoomGrain.Avatar.cs:24-70`) and `RoomAvatarModule.CreateAvatarFromPlayerAsync` (`RoomAvatarModule.cs:91`) check **nothing**: no capacity, no door, no ban.

**Deduced behaviour.** A player who is friends with someone present in a locked, password-protected or full room is added to the room server-side (avatar created, directory incremented, stream subscribed) **without** their client receiving a single entry packet: they stay in the hotel view or in their old room (from which `ClearActiveRoomAsync` removed them), while the occupants see a motionless avatar at the door. Their subsequent room packets (`ctx.RoomId` = the friend's room) act in that room.

**Impact.** Bypass of room privacy (lock, password, doorbell), of the population limit and of raid protection, through a legitimate action of the official client; client/server desynchronization. Exposure: any player with a friend.

**Protections looked for.** `IsFriendAsync` only. No guard in the grain; no test in `Vortex.Rooms.Tests` exercises this handler.

**Reproduction.** Account A owns a password-protected room, account B is friends with A and in another room; B sends `FollowFriend(A)` from the friends list: server-side B appears in A's room (`GetRoomPopulationAsync`), on B's client nothing changes.

**Fix.** Replace the call with a `RoomForwardMessageComposer { RoomId }` response (the client will then send `OpenFlatConnection`, exactly as `CreateFlatMessageHandler.cs` does after a creation). A one-line local fix; the structural fix is evolution A (§8).

### ROOM-02 — `GetRoomEntryData` admits a ringer with no answer from the owner (P1, confirmed)

**Path.** `RoomService.OpenRoomForPlayerIdAsync` calls `SetPendingRoomAsync(roomId, approved: true)` **before** the checks (`RoomService.cs:93`). For a locked door, it records the doorbell ring and returns (`:144-148`) without resetting the pending state. `GetRoomEntryDataMessageHandler.cs:46-50` reads `GetPendingRoomAsync()` and only tests `roomId <= 0`, never `Approved`; it then sends the whole entry payload and calls `SetActiveRoomAsync` (`:189`).

**Deduced behaviour.** A client that rings then immediately sends `GetRoomEntryData` (header 1250 in `Headers.cs`) enters the locked room with no answer. The `scripts/hooks/header-registry-baseline.json` baseline notes that "no message of this shape exists in the WIN63 client": the official client does not emit it, a modified client or a packet injector (common in the retro ecosystem) can. The doorbell stays pending: at the timeout (`RoomGrain.Doorbell.cs`, 20 s) the server sends `FlatAccessDenied` and `SetPendingRoomAsync(Invalid)` to a player already inside the room.

**Impact.** Same family as ROOM-01 (bypass of the doorbell, of raid protection, of the cancellable event and of capacity). Exposure: modified client.

**Fix.** Immediate: in `GetRoomEntryDataMessageHandler`, require `pendingRoom.Approved` **and** only set `approved: true` at the end of `CompleteRoomEntryAsync` (or in `AnswerDoorbellAsync` after admission); otherwise remove the handler from the registry (a dead id for the official client). Structural: evolution A.

### ROOM-03 — Room deletion: the furniture stays attached to the deleted room (P1, confirmed)

**Path.** `RoomGrain.DeleteRoomAsync` (`RoomGrain.Settings.cs:204-266`) checks the owner, sets `DeletedAt` on the room, removes the avatars, removes the room from the directory, publishes `RoomDeletedEvent` and deactivates. No `UPDATE furniture SET room_id = NULL`; the only consumers of `RoomDeletedEvent` are the audit handlers (`Vortex.Observability/Events/RoomLifecycleAuditHandlers.cs`). The inventory loader only lists rows with `RoomEntityId == null` (`InventoryFurnitureLoader.cs:64` and the predicate cited in `docs/codebase/06-economy/inventory.md`), and the deleted room can no longer be opened (global soft-delete filter, `ModelBuilderExtensions.cs:48`, `HydrateRoomStateAsync` → `RoomNotFound`).

**Behaviour.** Every piece of furniture (and placed pet/bot) in a deleted room becomes inaccessible to its owner, with no warning. The official client presents deletion as a normal action; on Habbo, the items come back to the inventory.

**Impact.** Permanent loss of player data on a common journey (players do delete rooms). Support tickets guaranteed, manual SQL repairs.

**Fix.** In the same transaction as the soft delete: `UPDATE furniture SET room_id = NULL WHERE room_id = @room` (each row's owner is already `player_id`, so everyone gets theirs back), likewise for `pets`/`bots`, then `InventoryGrain.ReloadFurnitureAsync` for the owners concerned; or refuse deletion while the room contains items (the client displays the error). Add a test in `Vortex.Rooms.Tests` (room with furniture from two owners → deletion → both inventories list them).

### QA-01 — Quality gate red on `main`, pushes with `--no-verify` (P1, confirmed)

**Facts.** Runs 316 to 323 of `.github/workflows/quality.yml` (13-14 September) all fail on the three OSes. The Ubuntu job log for run 34803028084 shows the cause: `node scripts/hooks/check-header-registry.mjs` exits with code 2 (`Directory.Build.targets(38,5): error MSB3073`) after flagging `CustomStackingHeightUpdateMessageComposer = 9201` above the 4101 ceiling. The messages of commits `4ca7c8a`, `482acb1` and `0455032` explicitly say `--no-verify`. Reproduced locally (section 9): `check-header-registry.mjs` exits 2 (ceiling mode, `check-header-registry.mjs:191-204`: the "ceiling" fallback **ignores the baseline** although that id is recorded there as unreachable), `check-wire-conflicts.mjs` exits 2 ("parsed no conflicts — the CLI output format changed, this check is blind"), and `scripts/hooks/__test/run.mjs` counts three failing self-tests.

**Impact.** Three of the gate's mechanized checks are broken, so the gate is red whatever a contributor does; the observed response is to bypass it. Every regression FastCheck and QualityGate were meant to stop gets through (dashboard capabilities, architecture walls, wire drift, hooks). The "Required validation before completion" rule in `AGENTS.md` is no longer tenable.

**Fix.** (1) `check-header-registry.mjs`: apply the baseline (`unreachable`) in ceiling mode too, or remove the 9201 mapping if it is dead; (2) `check-wire-conflicts.mjs`: re-align the parser with the current output of `Vortex.Specs.Cli -- conflicts` (or have the script read the conflict files directly) and **fail loudly when it is blind rather than a silent exit 2 or a misleading exit 0**; (3) repair the hook self-tests; (4) protect the `main` branch by the gate status and ban `--no-verify` from the workflow. Criterion: a green run on the three OSes, including the scan step (QA-02).

### SES-02 — No central authentication guard (P2, confirmed)

`MessageRegistry.CreateContextAsync` (`MessageRegistry.cs:48`) builds a `MessageContext` with `PlayerId = -1` when the session has not passed SSO and dispatches anyway. Of 557 handlers, 350 contain a guard of the form `PlayerId <= 0`; **40 use `ctx.PlayerId` with no guard**: `RedeemVoucherMessageHandler` (two DB queries per packet and activation of a `PlayerPresenceGrain(-1)` for the response, `VoucherGrain.cs:129-175`), `CreateFlatMessageHandler` (an `InvalidOperationException` logged after a query), `MoveAvatarMessageHandler`, `QuitMessageHandler`, `OpenFlatConnectionMessageHandler`, fifteen Navigator handlers, `GetNftAssetInventoryMessageHandler`, three NewNavigator handlers, `SetNewNavigatorWindowPreferencesMessageHandler`, `GetGuildFurniContextMenuInfoMessageHandler`, etc. The downstream services catch the case most of the time (player −1 not found), but each handler remains responsible for its guard, and the SSO handler itself has to remember that `ctx.PlayerId` stays −1 throughout its execution (a past bug recorded in its comment, `SSOTicketMessageHandler.cs`).

**Fix.** An `IMessageBehavior<IMessageEvent>` ordered right after `RateLimitBehavior` that rejects (and counts) any message not marked `[AllowUnauthenticated]` when `ctx.PlayerId <= 0`; mark the 9 handshake messages. The context can then guarantee a valid `PlayerId` and the 350 guards become redundant. Evolution C (§8).

### NET-01 — Incomplete network abuse limits (P2, confirmed)

The only limiter is per session (`RateLimitConfig`: 50/s, burst 100; `TokenBucketRateLimiter`: one bucket per `SessionKey`). Nothing bounds the number of connections per address, the duration of an unauthenticated session, or the total number of sessions (SuperSocket options not overridden in `appsettings.json`/`NetworkingConfig`). No `Origin` check exists in `Vortex.Networking` for the WS listener: a third-party site can open a WebSocket to the hotel from a browser (limited cross-site WebSocket hijacking, since the SSO ticket is required, but resources are consumed). Combined with SES-02, an attacker gets N × 50 requests/s to the database with N sockets without ever authenticating.

**Fix.** A cap on simultaneous connections per IP and globally (SuperSocket `MaxConnectionNumber` + a per-IP counter in `SessionGateway.AddSessionAsync`), a maximum delay before SSO (close after 30 s without an `SSOTicket`), a configurable `Origin` check on the WS listener, and a per-IP limiter in addition to the per-session one.

### PROTO-01 — Preallocation from a client-supplied counter (P2, confirmed)

`AcceptFriendMessageParser.cs:14` and `DeclineFriendMessageParser.cs:22` do `new List<int>(friendsCount)` with `friendsCount` read as is (`PopInt`, signed 32-bit). An 8-byte packet triggers an attempted allocation of `4 × friendsCount` bytes (up to 8 GB) before `Ensure` rejects the next read; at 50 packets/s per session, that is enough to saturate the large object heap and the garbage collector. `FishingParsers.cs:63` shows the right pattern (`Math.Clamp(packet.PopInt(), 0, MaxTimelineLength)`). The other 22 parsers that loop over a counter are bounded in practice by `MaxPacketBodyBytes` (64 KB) via `ClientPacket.Ensure`.

**Fix.** A `PopCount(maxItems, bytesPerItem)` primitive on `IClientPacket` bounding by `Remaining / bytesPerItem`, used by the three parsers concerned and adopted by the "add a feature" walkthrough.

### AUTH-01 — Replayable SSO ticket (P2, confirmed)

The ticket is strong (two GUIDs, 256 bits, `WebApiAuthService.cs:230`) but `TicketSingleUse` is `false` by default (`AuthenticationConfig.cs:34`): each use **extends** expiry by 30 s (`AuthenticationService.cs:90-125`) with no absolute cap (`TicketAbsoluteLifetimeSeconds` not set), and the IP address recorded at issuance is not compared at use time. An observed ticket (proxy, history, `Referer`, unencrypted transport) allows taking over the session, which disconnects the victim (`AddSessionToPlayerAsync` closes the old session) — visible but effective. The issuance rate is limited (`SsoTokenRateLimitPolicy`).

**Fix.** `TicketSingleUse = true` by default for the beta (reconnecting asks the site for a ticket again, which is the client's normal flow), `TicketAbsoluteLifetimeSeconds` = 60 in case the sliding mode is kept, and optional IP binding (with tolerance for NAT).

### ROOM-04 — `SetActiveRoomAsync` leaves an inconsistent state if avatar creation fails (P2, confirmed)

`PlayerPresenceGrain.Room.cs:45-103` sets `ActiveRoomId`, calls `RoomDirectoryGrain.AddPlayerToRoomAsync` (`:66`), subscribes to the stream (`:79`), then `CreateAvatarFromPlayerAsync` (`:95`). If that last one returns `false` (any exception there is swallowed and converted to `false`, `RoomGrain.Avatar.cs:24-70`) or if `SubscribeAsync`/`GetSummaryAsync` throw, the state stays "in the room" on the presence and directory side with no avatar. No caller reads the boolean (`GetRoomEntryDataMessageHandler.cs:189`, `RoomService.cs:360`, `FollowFriendMessageHandler.cs:84`). Consequences: ghost population in the navigator, `RoomDirectoryGrain.CheckRoomsAsync` (`RoomDirectoryGrain.cs:177`) considers the room populated and keeps it active **indefinitely** (20 Hz tick, timers, memory), stream subscribed for an absent player.

**Fix.** Reorder: create the avatar first, then directory and stream; on failure, undo everything (back to `-1`, `RemovePlayerFromRoomAsync`, unsubscribe) and return `false` to the handler, which answers `CantConnect`. Covered by evolution A.

### QA-02 — Vulnerable test dependency (P2, confirmed)

`dotnet list Vortex.Cloud.sln package --vulnerable --include-transitive` reports `SQLitePCLRaw.lib.e_sqlite3 2.1.10` (High, GHSA-2m69-gcr7-jv3q) via `Microsoft.EntityFrameworkCore.Sqlite 9.0.8` in `Vortex.Rooms.Tests`, `Vortex.Database.Tests`, `Vortex.Players.Tests`, `Vortex.Dashboard.Tests` (section 9). The "Scan for vulnerable packages" CI step (`quality.yml:67`) fails on "has the following vulnerable packages"; it is masked today because the gate fails before it. No runtime exposure (tests only).

**Fix.** Explicitly pin `SQLitePCLRaw.bundle_e_sqlite3`/`SQLitePCLRaw.lib.e_sqlite3` to the fixed version in `Directory.Packages.props` (transitive pinning already enabled).

### OPS-01 to OPS-04 — Operational prerequisites (P2, confirmed)

- **OPS-01 Backups.** `Vortex:Database:Backup:Enabled` is `false` and `MysqlDumpPath` empty by default; `DatabaseBackupService` (mysqldump `--single-transaction --routines --events`, password via `MYSQL_PWD`) writes locally to `backups/`, retention deletes beyond `RetentionCount`, a failure is only a `LogError`. No restore procedure, no restore test, no off-machine copy, no PITR (binlog). For a beta with persistent data, this is the first thing that must exist.
- **OPS-02 Logs.** `Vortex.Logging` only provides a console formatter and a provider to the dashboard's server console (`ServerConsoleLoggerProvider`); no file sink and no rotation. In a container, Docker's log driver is enough if configured; on bare metal with the supervisor, the buffer is 2,000 lines (`appsettings.json`). `PackageHandler.HandleCoreAsync` (`PackageHandler.cs:60-90`) logs at `Error` with a 128-byte hexdump for **every** invalid packet: a client can produce 50 error lines per second, plus one record in the error sink.
- **OPS-03 Commerce recovery.** The journal detects and **escalates** (`CommerceRelayService.EscalateAsync` → `NeedsIntervention`, `LogCritical`) but repairs nothing, by design (ADR-001: "resuming it is not yet automatic, and that is the next slice"). There is neither a runbook nor a dashboard action to: list operations in `Debited`/`Pivoted`/`NeedsIntervention`, re-deliver, refund. Without that, a DB incident during a purchase peak ends in manual support through SQL.
- **OPS-04 Deployment.** `docker-compose.yml` is explicitly a development workstation (public secrets, `DOTNET_ENVIRONMENT=Development`, cleartext HTTP allowed). Outside development, the host refuses to start without `Vortex:Orleans:ClusteringProvider/GrainStorageProvider = adonet` (which requires the Orleans SQL scripts applied by hand) or `AllowUnclusteredOutsideDevelopment = true` (`HostApplicationBuilderExtensions.cs:40-70`). The validators reject `CHANGE_ME` values (crypto, IP hash, supervisor, shop): good. But nothing describes the beta environment: where TLS terminates for the WebSocket (`wss://`, indispensable for the browser client and for the SSO ticket's confidentiality), how secrets are injected, which migration mode (`MigrateOnStartup` single-node or `dotnet ef` before deployment), what log retention, what supervision.

### DB-01 — Migration hygiene and absence of a real MySQL lane (P2, confirmed/to verify)

302 migration files since February 2026, of which 121 contain `DropTable`/`DropColumn` and 37 raw SQL; `scripts/sql/recover_half_applied_habbicon_migration.sql` and `resync_habbicon_migration_history.sql` attest to at least one half-applied migration incident (MySQL does not transact DDL). `MigrationHelper.ApplyStartupMigrationsAsync` (`Program.cs:198`) logs the pending ids then `MigrateAsync` with no multi-host lock (documented). All persistence tests run on SQLite or InMemory; MySQL behaviours (the `utf8mb4_unicode_ci` collation that makes `Name` case-insensitive, conditional `ExecuteUpdate`, non-transactional DDL, `strict mode` on lengths) are never exercised in CI. No rollback path exists (the `Down` migrations of 302 steps are not a strategy): the rollback is a backup restore, which leads back to OPS-01. Not verified for lack of a database: `dotnet ef migrations has-pending-model-changes`.

**Fix.** Before opening: freeze a schema **baseline** (squash the 302 migrations into one initial migration + a generated idempotent script), document "migration = before deployment, host stopped, backup taken first", and add a MySQL integration test lane (Testcontainers or a CI service) for the conditional-claim paths and for applying migrations from scratch.

### SEC-01 — Accounts: no lockout, sessions not revoked on ban (P2, confirmed)

`AccountAuthenticator.VerifyCredentialsAsync` does a BCrypt (factor 12, dummy hash for unknown accounts, off-thread) with no failure counter; the only brakes are the endpoints' fixed-window limiters (`WebApiAppConfigurator.cs:90-140`, `DashboardEndpoints.cs:118`), by default per client. Web and dashboard sessions are in memory (`AccountSessionStore`, 256 bits, configured expiry): lost on restart (every site visitor logged out on every deployment) and, as `WebApiSessionStore.cs` notes itself, never revoked on a ban ("a banned visitor keeps browsing until the cookie expires"). MFA: present on the dashboard side with step-up; on the site side, TOTP if enrolled.

**Fix.** Progressive lockout per account (increasing delay after N failures) and per IP, revocation of web/dashboard sessions in the ban path (the `IAccountSessionRevoker` already exists), and, if deployments are frequent, persisted sessions (a table) to avoid the mass logout.

### P3 findings (summarized)

- **PERF-01.** `MessageSystem.ResolveRoomIdAsync` (`MessageSystem.cs:85-113`) pays an Orleans round trip per packet to fill `roomId` in the metrics; it is also, by accident, what keeps the presence grain alive (SES-01). Replace it with a read of the gateway's local cache (the active room can be published by the grain to `SessionGateway` on every change) and measure on `MoveAvatar`.
- **NET-02.** TCP with no heartbeat and `PongTimeout = 0`: a dead socket with no FIN stays "online" until the OS keepalive (often 2 h). Enable TCP keepalive server-side (SuperSocket `KeepAliveOptions`) and a long `PongTimeout` (10 min) that distinguishes a frozen tab from a cut cable.
- **ROOM-05 (to verify).** The entry payload (`Objects`, `Items`, `Users`) is sent before the stream subscription (`RoomService.cs:300-360`, `GetRoomEntryDataMessageHandler.cs:189`, `PlayerPresenceGrain.Room.cs:79`): a `UserRemove`/`ObjectAdd` published in that window is lost for the entrant. Measure the window in an integration test; fix: subscribe first then emit the snapshot (or re-synchronize after subscribing).
- **DB-02 (to verify).** `FlushDirtyItemsAsync` attaches a batch of 100 entities and saves in one go (`RoomPersistenceGrain.cs:130-190`); a missing row (direct SQL deletion, purge) makes it throw `DbUpdateConcurrencyException` for the whole batch, retried on every tick without ever isolating the offending item. To be confirmed on MySQL; fix: remove from the batch the entity that fails after N attempts and log it.
- **PROTO-02.** `SetMottoAsync` (`PlayerGrain.cs:183`) and `UpdateRoomSettingsAsync` (`RoomGrain.Settings.cs:51-120`) enforce no length (`varchar(512)`/`varchar(50)` columns: MySQL strict rejects, exception logged) and no word filter; `ChangeMottoMessageHandler.cs:22` tests `< 0` instead of `<= 0`. Chat is bounded to 100 characters (`RoomChatSystem.cs:28`) with flood control.
- **SEC-02.** Room password in clear in the database and in `RoomSnapshot` (Habbo standard, but avoidable by server-side hashing); unconditional `UseSwagger()` on the dashboard (`DashboardWebHost.cs:664`) and the WebApi: to be disabled outside development.
- **OBS-01.** `TracingEnabled` produces `ActivitySource`s that are never exported (no exporter registered, `VortexTelemetry.cs:8`); `/health` (`WebApiEndpoints.cs:240-275`) only tests the database and the service guard, not the game listeners nor the silo. Add an OTLP exporter and an "accepts a handshake" probe for the orchestrator.
- **DATA-01.** `PlayerDirectoryGrain` keeps `id↔name` for every player looked up indefinitely (`:26-27`); the inventory loads everything (`InventoryFurnitureLoader.cs:64`). Acceptable in beta; bound it (LRU) and paginate before scaling up.
- **DOC-01.** `docs/orleans.md` describes `[PersistentState]`/`PlayerStore` that do not exist; `CONTEXT.md` promises a multi-session fan-out the code forbids (one session per player, `SessionGateway._playerToSession`); `AGENTS.md`/`CLAUDE.md` mention 23 wire conflicts and 14 baselined ids (real: 61 and 3); `README.md` says "SDK 9.x". These documents drive the repository's AI tools: their drift produces changes based on false premises.
- **SPEC-01.** 127 handlers "reach no domain operation and send nothing" (camera, competitions, campaigns, advertisements, seasonal calendar…). None is on an essential journey, but each is a feature the client offers and the server silently ignores: either display them as such (a client message) or remove them from the registry.
- **ORL-01.** `RoomDirectoryGrain` is `[KeepAlive]` and only cleans up through `RemoveActiveRoomAsync`; a room that dies without `OnDeactivateAsync` (silo killed) leaves an entry until restart — benign on a single node since a restart empties everything.

---

## 8. Proposed architectural evolutions

Each evolution is classified: **before the beta**, **to plan soon**, or **later**. The criterion is the one from the request: cost now versus cost after the players and the persistent data arrive.

### A. One single room entry door, held by the grain (before the beta)

**Limits of what exists.** The entry guards (ban, capacity, password, doorbell, raid, cancellable event) are in `RoomService` (a stateless service) while the state they protect is in `RoomGrain`. Any path reaching the grain without going through the service (`FollowFriend`, `GetRoomEntryData`, tomorrow a wired teleport, a moderator "go to", a dashboard action) bypasses everything (ROOM-01, ROOM-02). The presence state (`Pending`/`Approved`/`Active`) is set optimistically before the checks and is not undone on failure (ROOM-04).

**Options.**
1. *Fix each caller* (one line for `FollowFriend`, one condition for `GetRoomEntryData`, a rollback in `SetActiveRoomAsync`). Minimal cost, but the structural hole remains: the next caller will reopen it.
2. *Move admission into the grain*: `IRoomCore.TryAdmitAsync(ActionContext, AdmissionRequest) → AdmissionDecision` (Admitted / Full / WrongPassword / Banned / RingDoorbell / Refused(reason)), which applies in order ban → capacity → door → raid → cancellable event and, on admission, creates the avatar in the same turn. `RoomService` then only translates the decision into composers and drives the presence. `SetActiveRoomAsync` only exists with an `Admitted` decision and becomes transactional (avatar created → directory → stream, full rollback otherwise). `FollowFriend` answers `RoomForward`.
3. *Explicit presence state machine* (enum `None → Pending(room) → Admitted(room) → Active(room)`) carried by the presence grain, every transition validated. Complements option 2.

**Recommendation.** Option 2 + 3. Benefits: a whole class of hole closed, testability (the grain tests in TestingHost with no service), readability of the entry journey (today spread over three files and two grains). Trade-off: `RoomGrain` grows by one method and one decision type; the doorbell keeps its state in the grain (already the case). Risk: a regression on the eight entry cases → cover with a test table (open, full, right/wrong password, locked with/without an answer, banned, raid, cancelled event). Cost now: 2 to 3 days; after opening: the same in code, but every bypass discovered in the meantime is a privacy incident.

**Transition.** Introduce `TryAdmitAsync` alongside what exists, route `OpenRoomForPlayerIdAsync` then `AnswerDoorbellAsync` through it, remove the direct calls to `SetActiveRoomAsync` outside a decision, then delete the old path. No data migration.

### B. The presence grain's life follows the session (before the beta)

**Limits.** The presence grain is the routing pivot (observer, active room, outgoing queue) but its lifetime is that of a cache: 2 minutes without a message (SES-01). The gateway (`SessionGateway`) believes a player online whom the grain believes offline.

**Options.**
1. *Session-driven `DelayDeactivation`*: `RegisterSessionObserverAsync` → `DelayDeactivation(TimeSpan.MaxValue)`; `UnregisterSessionObserverAsync` → `DeactivateOnIdle()`. Three lines, solves the symptom, Orleans guarantees the semantics.
2. *Self-healing reactivation*: `OnActivateAsync` queries `ISessionGateway.GetSessionObserver(GetPlayerSession(id))` and re-registers itself. Also solves reactivation after an explicit deactivation or an activation crash.
3. *Periodic reconciliation* gateway ↔ grains (`IsOnlineAsync` vs `_playerToSession`) which logs and repairs the discrepancies: a safety net and an inconsistency metric.

**Recommendation.** 1 + 2, with 3 as a metric (`Vortex.presence.mismatch`). Benefits: a connected player can no longer become a ghost; presence memory is bounded by the number of sessions, which is the right invariant. Trade-off: the presence grains of connected players are never collected again (intended). Risk: a `DelayDeactivation` forgotten at disconnect would keep activations alive; covered by `OnDeactivateAsync` and by the reconciliation. Cost: half a day plus a TestingHost test.

### C. Authentication and authorization guard in the pipeline (before the beta)

**Limits.** 557 handlers, 350 copied guards, 40 missing (SES-02). The context can carry an invalid `PlayerId` all the way into the grains. The SSO handler works with a stale context.

**Recommendation.** A global `AuthenticationBehavior` (ordered right after `RateLimitBehavior`): refuse any message whose type is not marked `[AllowUnauthenticated]` when the session is not bound; count the refusals (`Vortex.packets.dropped{reason=unauthenticated}`); close the session after N refusals. Then make `MessageContext.PlayerId` non-negative by construction and progressively delete the local guards. Then add, on the same mechanism, a per-message authorization declaration (`[RequiresCapability(...)]`) for the 23 moderator handlers, which makes authorization readable and testable in one place. Cost: one day; benefit: a class of hole closed and 350 guard lines removable.

### D. Repair the quality gate and make it unavoidable (before the beta)

Detailed in QA-01/QA-02. Add: branch protection on `main` conditioned on the gate status, running `dotnet list package --vulnerable` in FastCheck (it costs nothing), and a principle: a "blind" check fails or announces itself as such in a separate status, never as an anonymous exit 2.

### E. Operational foundation (before the beta)

Groups OPS-01 to OPS-04, OBS-01, NET-02. Deliverables: (1) a beta deployment manifest ("prod" compose or systemd: secrets through `env_file`/secret store, `AllowUnclusteredOutsideDevelopment=true` **or** documented adonet, single-node `MigrateOnStartup`, TLS reverse proxy in front of 30001 and 9000/8080, Swagger disabled); (2) structured logs to stdout captured by the log driver with rotation, and `Warning` level for `PackageHandler` with sampled hexdumps; (3) backups enabled, copied off-machine, **restore rehearsed** on a test environment; (4) an OTLP exporter or, failing that, a Prometheus/Grafana dashboard wired to `/metrics` with alerts on `LogCritical` (commerce escalations), `Vortex.packets.dropped`, tick latency; (5) a runbook: hot restart (what is lost: in-memory rooms, presences, subscriptions), ghost player, commerce operation in `NeedsIntervention` (query, re-delivery, refund through the grains, never direct SQL), half-applied migration (`scripts/sql/recover_half_applied_*` generalized).

### F. Room deletion = items returned (before the beta)

Detailed in ROOM-03. One transaction: soft delete of the room + `room_id = NULL` for furniture, pets, bots + reload of the owners' inventory views; integration test.

### G. Persistence: schema baseline and MySQL lane (to plan soon)

Detailed in DB-01. Squashing the migrations is all the cheaper for being done **before** the first production database; afterwards, databases at various levels have to be handled. The MySQL lane (Testcontainers) protects the conditional claims, the collation and migrations from scratch. Trade-off: CI time (+3 to 5 min); to be limited to tests marked `[Trait("db","mysql")]`.

### H. Declarative input validation for the protocol (to plan soon)

Detailed in PROTO-01/PROTO-02. A bounded read primitive and a length attribute on the `string` fields of inbound messages (`[MaxLength]` read by the pipeline) avoid relying on every handler. Unify the word filter (chat, motto, room names, forum) behind a single service.

### I. Mechanically verified contracts and documentation (to plan soon)

Detailed in DOC-01. The numbers cited in `AGENTS.md`/`CLAUDE.md` (conflicts, baselined ids) must be computed by the scripts and not copied by hand; `docs/orleans.md` and `CONTEXT.md` must be rewritten or replaced by links to `docs/codebase/` (which is accurate on those points). A freshness test (the docs generator compares its commit to `HEAD`) prevents silent drift.

### J. Multi-silo and durable streams (later)

The single-silo thesis is coherent and guarded by `RefuseAnUndeclaredSecondSiloAsync`. Do not invest before measuring one node's capacity with `Vortex.LoadGen` on a test environment. Reopening conditions: a measured overrun of one node, or a high-availability requirement. The inventory of components to make cluster-aware is already kept in `OrleansHostConfig.MultiSiloReady` and `docs/architecture-v4/single-silo-inventory.yaml`.

What the audit **does not recommend**: global event sourcing, generalized CQRS, splitting `RoomGrain` into per-object grains (forbidden by ADR-000 and rightly so: the room's single turn is the concurrency model), replacing the permission engine. None of those projects answers an observed defect.

---

## 9. Validations performed

All commands were run in the audit container (`/root/.dotnet`, SDK 10.0.401, Node 22.22), with no database.

| Validation | Command | Result |
|---|---|---|
| Restore | `dotnet tool restore && dotnet restore Vortex.Cloud.sln` | OK; NU1903 warnings (SQLitePCLRaw 2.1.10) on 4 test projects |
| Build | `dotnet build Vortex.Cloud.sln --no-restore` | **OK**, 0 errors, 7 warnings, 3 min 47 |
| Tests | `dotnet test <project> --no-build` for the 18 test projects | **3,869 passed, 0 failed, 0 skipped** (Rooms 1,478, Signals 437, Players 349, Revisions 295, Database 220, Specs 217, Dashboard 215, Hosting 138, WebApi 134, Rewards 94, Authentication 68, Crypto 64, Navigator 54, Supervisor 36, Plugins 31, Shop 19, Pipeline 13, PacketHandlers 7); TRX in the audit environment |
| Format | `dotnet csharpier check .` | OK (6,539 files) |
| Non-compiler checks | `node scripts/hooks/check-dashboard-capabilities.mjs` | OK (64 capabilities, 52 routes, 3,486 locale keys) |
| | `node scripts/hooks/check-header-registry.mjs` | **Failure, exit 2**: ceiling mode (client sources absent), id 9201 flagged although baselined |
| | `node scripts/hooks/check-architecture-walls.mjs` | OK (7 walls, 0 baselined leaks) |
| | `node scripts/hooks/check-logic-groups.mjs` | OK (261 keys, 14 groups) |
| | `node scripts/hooks/check-wire-conflicts.mjs` | **Failure, exit 2**: "parsed no conflicts — the CLI output format changed, this check is blind" |
| | `node scripts/hooks/__test/run.mjs` | **3 self-tests failing** (two tied to the header registry, one to the csharpier probe) |
| Specs | `dotnet run --project Vortex.Specs.Cli -- validate` | OK (3,923 files, 0 errors, 0 warnings) |
| | `dotnet run --project Vortex.Specs.Cli -- unknowns --severity critical` | 127 critical unknowns (mute handlers) |
| Front end | `npm run lint` / `npm run test` (Vortex.Dashboard.Web) | OK (svelte-check 0 errors, 16 warnings; 4 tooling checks OK) |
| Vulnerabilities | `dotnet list Vortex.Cloud.sln package --vulnerable --include-transitive` | SQLitePCLRaw.lib.e_sqlite3 2.1.10 (High) in Rooms/Database/Players/Dashboard.Tests |
| CI | `mcp github actions_list` / `get_job_logs` (run 34803028084) | Last 8 runs failing; cause: `check-header-registry.mjs` exit 2 in FastCheck; scan step skipped |
| Inventories by grep | grains (54), `[KeepAlive]` (16), interleaving attributes (6 + manifest), handlers (557 / 350 guarded / 40 unguarded), preallocating parsers (3), locks/blocking/`.Ignore()` in the grains (0), `ConfigureAwait(false)` in the grains (0), transactions (3 sites), unique indexes (126), destructive migrations (121) and raw SQL (37) | See §7 |

**Checks impossible in this environment, with the missing prerequisite:**

- Running the emulator, applying the 302 migrations, `dotnet ef migrations has-pending-model-changes`, measuring startup, dynamically reproducing SES-01/ROOM-01/ROOM-02/ROOM-03 → **a MySQL server** (the Docker daemon is not reachable in the container).
- Classifying the 61 baselined wire conflicts, checking the serializers against the client → **the WIN63 client's AS3 sources** alongside the repository.
- Load test and capacity (`Vortex.LoadGen`), real loss window, tick cost → **a test environment with a database and a client**. No player capacity is claimed in this report; the only existing figures are micro-benchmarks (`docs/architecture-v4/benchmarks/`).
- TCP keepalive behaviour, tab freezing, reconnection → **a real client**.

---

## 10. Work plan before the beta

Ordered by dependency; each item carries its acceptance criterion. The durations are orders of magnitude for one person familiar with the repository.

### Phase 0 — Put the net back (1 to 2 days)

1. **QA-01** Repair `check-header-registry.mjs` (baseline applied in ceiling mode, or the 9201 mapping removed), `check-wire-conflicts.mjs` (parser re-aligned, explicit failure when blind), hook self-tests. *Acceptance*: `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` green locally and `quality.yml` green on the three OSes.
2. **QA-02** Corrected SQLitePCLRaw pin. *Acceptance*: "Scan for vulnerable packages" step green.
3. **Governance** Branch protection on `main` by the gate; `--no-verify` banned from the workflow. *Acceptance*: rule enabled, PR mandatory.
4. **DOC-01 (numbers part)** `AGENTS.md`/`CLAUDE.md`/`README.md` corrected (61 conflicts, 3 ids, SDK 10, one session per player). *Acceptance*: numbers derived from the baseline files.

### Phase 1 — Close the P1 defects and the security P2s (5 to 8 days)

5. **Evolution A** (ROOM-01, ROOM-02, ROOM-04): `TryAdmitAsync` in the grain, presence state machine, `FollowFriend` → `RoomForward`, `GetRoomEntryData` conditioned on `Approved` (or removed). *Acceptance*: entry test table (8 cases) green in TestingHost; a forged client sending `GetRoomEntryData` during a doorbell ring receives `CantConnect`; `GetRoomPopulationAsync` returns to 0 after a failed entry.
6. **Evolution B** (SES-01): presence tied to the session + self-healing reactivation + discrepancy metric. *Acceptance*: TestingHost test "observer registered, collection age exceeded, `IsOnlineAsync` stays true"; manual scenario, tab frozen 5 min → the player stays in their room and receives the messages on resume.
7. **Evolution F** (ROOM-03): room deletion returns furniture/pets/bots. *Acceptance*: integration test with two owners; no furniture with a `room_id` pointing at a room with `DeletedAt IS NOT NULL` after deletion.
8. **Evolution C** (SES-02): `AuthenticationBehavior` + `[AllowUnauthenticated]` on the 9 handshake messages. *Acceptance*: pipeline test "unmarked message on an unbound session → rejected, counter incremented"; the 40 listed handlers are no longer reachable without SSO.
9. **PROTO-01** bounded read primitive, applied to the two parsers. *Acceptance*: parser test with `friendsCount = int.MaxValue` → empty list or exception with no allocation (measured with `GC.GetTotalAllocatedBytes`).
10. **AUTH-01** `TicketSingleUse = true`, `TicketAbsoluteLifetimeSeconds` set. *Acceptance*: two successive SSOs with the same ticket → the second is refused (an existing test to extend in `Vortex.Authentication.Tests`).
11. **NET-01** per-IP and global connection cap, pre-SSO deadline, configurable WS Origin check. *Acceptance*: `SessionGateway` tests; 200 sockets without SSO from one IP → refused beyond the cap, closed after the deadline.

### Phase 2 — Operational base (5 to 8 days, in parallel with phase 1)

12. **OPS-04** beta deployment manifest (secrets, TLS in front of WS/HTTP, `AllowUnclusteredOutsideDevelopment` or adonet, Swagger disabled, `MigrateOnStartup` decided). *Acceptance*: successful startup outside `Development` with the versioned configuration (secrets aside), `ListenerSecurity` with no insecure opt-in.
13. **OPS-01** backups enabled, off-machine copy, **restore rehearsed** on a test environment. *Acceptance*: procedure written and executed twice, duration measured.
14. **OPS-02 / OBS-01** persistent structured logs with rotation, sampled hexdump, OTLP exporter or a Prometheus dashboard with alerts (LogCritical, drops, tick latency, flush failures). *Acceptance*: a commerce `LogCritical` triggers a visible alert.
15. **OPS-03** operations runbook + a dashboard "pending commerce operations" action (list, re-deliver through the grains, refund). *Acceptance*: the "crash between debit and grant" scenario played in test, resolved without SQL.
16. **NET-02** TCP keepalive + long `PongTimeout`. *Acceptance*: a socket cut with no FIN detected in under 15 min.
17. **SEC-01** progressive lockout, session revocation on ban. *Acceptance*: authentication tests.

### Phase 3 — Before opening (3 to 5 days)

18. **Evolution G** (DB-01): schema baseline (squash) + MySQL lane (Testcontainers) on the conditional claims and migrations from scratch. *Acceptance*: CI green with the lane; an empty database migrated in one step.
19. **Load test** with `Vortex.LoadGen` on the beta environment: 100 then 300 synthetic clients, room entry, movement, chat, purchases. *Acceptance*: tick latency p95 < 25 ms on a room of 50, no memory leak over 2 h, no commerce escalation; figures recorded in `docs/architecture-v4/benchmarks/`.
20. **Incident rehearsal**: hard stop during a building session (measuring the real loss window), restart, backup restore. *Acceptance*: runbook executed, gaps fixed.
21. **PROTO-02 / SEC-02** unified lengths and filter, room password hashed, Swagger outside development.

---

## 11. Opening checklist

Observable criteria; each is verifiable by a command, a query or an observation.

**Launch**

- [ ] `quality.yml` green on the three OSes for the deployed commit; no `--no-verify` since phase 0.
- [ ] Host started outside `Development` with the versioned beta configuration; no `CHANGE_ME`; `ListenerSecurity` with no insecure opt-in; TLS active in front of 30001, 8080, 9000.
- [ ] `TicketSingleUse = true`; `Vortex:Orleans:GrainCollectionAge` documented; presence tied to the session (TestingHost test green).
- [ ] Room entry tests (8 cases) green; `FollowFriend` answers `RoomForward`; `GetRoomEntryData` refused outside admission.
- [ ] Room deletion returns the items (integration test green).
- [ ] Automatic backup active, last successful restore less than 7 days old.
- [ ] Persistent logs with rotation; alert on `LogCritical` tested.
- [ ] `/health` "Healthy", `/metrics` reachable by the scraper with a token, dashboard showing: sessions, players online, active rooms, tick latency, `Vortex.packets.dropped` by reason, commerce operations by state.
- [ ] Runbook available: restart, ghost player, blocked commerce operation, half-applied migration, restore.
- [ ] Load test run on the beta environment with the figures recorded.
- [ ] Abuse limits active: rate limit per session **and** per IP, connection cap, pre-SSO deadline.

**Incident recovery**

- [ ] Hot restart: duration measured, rooms reloaded from the database, players reconnected with a new ticket (the site issues a new one); what is lost is known (positions not flushed < 2 s + batch window, presences, subscriptions).
- [ ] After a database outage: `RoomPersistenceGrain` replays its batches (positions), `CommerceRelayService` relays the events, the `NeedsIntervention` operations are listed and handled.
- [ ] After a crash between debit and grant: the `Debited` operation is visible, refund or delivery carried out per the runbook, player informed.
- [ ] Backup restore: procedure executed, the gap between the backup and the incident announced to the players.

---

## 12. Deferred work

| Topic | Reason for deferral | Trigger condition |
|---|---|---|
| Multi-silo (evolution J), durable streams, ADO.NET PubSubStore | Coherent single-node thesis, guarded; no measured need | One node's capacity reached (phase 3 measurement) or a high-availability requirement |
| PERF-01 (one grain call per packet) | Cost not measured; the accidental keep-alive of the presence must be handled first (evolution B) | p95 latency of `MoveAvatar`/`Chat` packets measured above the target |
| ROOM-05 (snapshot before subscription) | A window of a few milliseconds, a rare symptom repaired by re-entering | Reports of missing furniture/avatars on entry, or during evolution A if the reordering is free |
| DB-02 (poisoned flush batch) | Only happens on a physical deletion outside the code; to be confirmed on MySQL | MySQL lane in place (evolution G) |
| DATA-01 (unbounded caches and inventories) | Beta volumes are small | More than 50,000 players looked up or inventories > 20,000 items |
| SPEC-01 (127 mute handlers) | Off the essential journeys | Player reports on the features concerned; display them as unavailable until then |
| Modernizing the game transport's encryption | Constrained by the client (RSA-1024, DH-384, RC4); TLS in front of the WS covers the browser | Client change |
| Plugins in production (hot reload, isolation) | The sample plugin is not required; documented memory leak on reload | First plugin deployed in beta: hot reload forbidden, restart only |
| Evolution I (mechanically verified documentation) | No player impact; impact on the quality of AI-assisted changes | From phase 0 for the numbers; freshness test in phase 3 |
| ORL-01 (`RoomDirectoryGrain` with no reaper) | Benign on a single node (emptied on restart) | Move to multi-silo |

### Remaining work on the audit itself

To complete the coverage (§4), in order of usefulness: the wired engine (execution limits in real conditions, CPU cost of a box spam), room games and pets (exceptions in the tick, orphaned timers), endpoint-by-endpoint enumeration of the dashboard (authorization, IDOR, pagination), a wider sample of the 105 Room handlers and of the Help/GroupForums handlers, reading the placeholder serializers, classifying the 61 wire conflicts with the client sources, snapshot/model coherence and N+1 on MySQL, actually running the emulator and the load test. The reproductions described in §7 are the resumption point.
