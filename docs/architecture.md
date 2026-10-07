# Architecture map

What runs where, traced on the flows that exist today. Read it when a task crosses projects; for a
single project, the code and `.claude/rules/` are enough.

## Contents
- Processes and layers
- Flow 1 — sign-in (web API → SSO → zone)
- Flow 2 — entering a zone
- Flow 3 — walking (the room tick)
- Persistence
- Grains and their state
- Plugins and the web API
- Observability
- Guard rails that are scripts, not prose

## Processes and layers

One process, `Vortex.Main`: the Orleans silo (in-process, single silo — a second one is refused unless
`Vortex:Orleans:MultiSiloReady` is set), the SuperSocket game listeners (TCP 30000 / WebSocket 30001,
40000/40001 in Development) and the web API plugin (HTTP 8080). MySQL holds the durable state.

Core (Networking, Pipeline, Protocol, Messages, Revisions, PacketHandlers, Runtime, Crypto, Database,
Events, Logging, Observability, Plugins, Primitives) → Platform (Rooms, Players, Furniture,
Permissions) → Web (WebApi, WebApi.Authentication). `Vortex.Primitives` holds every contract two
layers share: grain interfaces, snapshots, ids, events.

## Flow 1 — sign-in

1. The site registers an account (`POST /api/public/registration/new`), creates and selects an avatar
   (`/api/user/avatars`, `/api/user/avatars/select`) and asks `GET /api/ssotoken` for a ticket
   (`security_tickets`). It is not single-use: the client reads it from `?sso=` and a page reload
   presents it again, so it stays valid up to `Authentication:TicketAbsoluteLifetimeSeconds` (300 s)
   from issue, sliding on each use.
2. The client connects to the game socket. Frames are `[int32 length][int16 header][body]`, plaintext
   until the Diffie handshake installs RC4 (`Vortex.Networking/Package`).
3. `PackageHandler` finds the parser for the header in the session's revision
   (`Revision20260701`, maps in `Maps/`); no parser → warning, packet dropped. The typed message goes
   through `MessageSystem` to the `IMessageHandler<T>` found by scan.
4. `SSOTicketMessageHandler` resolves the ticket (`IAuthenticationService`), binds the session to the
   player (`SessionGateway.AddSessionToPlayerAsync` → `PlayerPresenceGrain`), then sends
   `AuthenticationOK`, `UserRights` and `RoomForward(world.start_room_id)` — the start zone created by
   `scripts/sql/dev_zone.sql`.

## Flow 2 — entering a zone

`OpenFlatConnection` → `RoomService.OpenRoomForPlayerIdAsync` (`Vortex.Rooms/RoomService.cs`):

- marks the room pending on the player's `PlayerPresenceGrain`, sends `OpenConnection`;
- activates the `RoomGrain` (`EnsureRoomActiveAsync`: map built from the room model, furniture loaded);
- checks the door (locked refuses, password compared) unless the player has rights;
- completes the entry: `RoomReady`, entry tile, properties, `HeightMap`, `FloorHeightMap`, `Users`,
  `UserUpdate`, `Objects`, `Items`, controller level;
- `PlayerPresenceGrain.SetActiveRoomAsync` subscribes the player to the room's Orleans stream
  (`RoomOutbound`): everything the room broadcasts reaches the player through their presence grain.

## Flow 3 — walking

`MoveAvatar` → `RoomService.ClickTileAsync` → `RoomGrain.WalkAvatarToAsync` → `RoomAvatarModule`
plans the path (`RoomPathingSystem`, `RoomMapModule` walkability: heights, furniture flags, avatars).
The room clock is a one-shot grain timer re-armed every `RoomTickMs` (50 ms); avatars step on
`AvatarTickMs` (500 ms) in `RoomAvatarTickSystem`, which broadcasts `UserUpdate` on the room stream
and fires furniture `OnWalkOn/Off` hooks. Each tick step is isolated: one failing step is logged and
the rest of the tick still runs.

## Flow 4 — publishing a zone (map editor)

`/api/editor/*` (web API, `WebApiEndpoints.Editor.cs`, capability `world.zone.edit`/`publish`) →
`IZoneEditorService` (`Vortex.World`): the JSON document is validated (`ZoneValidator`), stored as a
new row of `zone_versions`, then `zones.published_version` moves → `RoomGrain.ReloadZoneAsync`
forwards occupants back in and deactivates. On activation `RoomGrain` asks `IZoneRoomSource`: a zone's
map is compiled from `floor`, its decor built from `decor` (no `furniture` rows, never flushed).
Proof: `driver.mjs zone`. Schema: `schemas/zone.schema.json` (exported from the records, test-checked).

## Flow 5 — a zone at run time

`ApplyZoneAsync` compiles the document once per activation (`ZoneCompiler` → `CompiledZone`: areas
per cell by priority, passages, entries, decor object ids) into `RoomLiveState.ZoneRuntime`
(`ZoneRuntimeState`: doors, inactive areas, visitors, cooldowns, expected arrivals), and
`ZoneSettingsApplier` puts the zone's name, capacity and walls on the room snapshot in memory. The
modules under `Vortex.Rooms/Grains/Zones` then answer, each for one thing:

| Module | Answers | Driven by |
|---|---|---|
| `ZoneArrivalModule` | where an entering avatar appears: expected entry or cell > first `arrival` > door | `RoomAvatarModule.CreateAvatarFromPlayerAsync` |
| `ZoneAreaModule` | which areas a player stands in; `ZoneAreaEntered/LeftEvent`, the area bubble | each committed step (`OnZoneStepAsync`) |
| `ZonePassageModule` | passages: same zone on the spot, another through `IRoomZone.ExpectArrivalAsync` + `RoomForward` | the step, teleporters, `Teleport` actions |
| `ZoneInteractionModule` + `Interactions/` | `UseFurniture`/`UseWallItem` on zone decor: one class per `interaction` | `RoomActionModule.UseItemByIdAsync` |
| `ZoneTriggerModule` + `Triggers/` | triggers on the room event bus and the `zone-clock` tick step; one evaluator per condition, one executor per action | `RoomEventModule`, the room tick |

A `Dungeon` zone is played in copies, one per group: `RoomService` asks the room
`IRoomZone.InstanceForAsync` and forwards the player to the copy `IZoneInstanceGrain` hands out. A
copy's room id (`ZoneInstanceIds`, from 1 000 000 000) names its template, whose row, zone, rights
and visit log it uses; the copy is released when its room deactivates. Proof: `driver.mjs dungeon`.

The map reads zone overrides through `RoomMapModule.CanWalkOn/CanSitOn/CanLayOn` (doors, decor
collision). The editor's test mode: `POST /api/editor/zones/{key}/test` → `IZoneTestGrain` → the SSO
handler forwards the tester's next sign-in to the zone. Proofs: `driver.mjs travel`, `testmode`.

## Persistence

| State | Owner | Written |
|---|---|---|
| player card (name, motto, figure) | `PlayerGrain` | on change and on deactivation |
| presence (session, active/pending room) | `PlayerPresenceGrain` | in memory (Orleans storage) |
| placed furniture | `RoomGrain` → `RoomPersistenceGrain` | queued, flushed every `DirtyItemsTickMs` and drained on deactivation |
| tile heights | `RoomGrain` | flushed by the tick |
| server config (`world.start_room_id`, `motd.lines`) | `ServerConfigGrain` | through the grain |
| audit, item forensics, errors | Observability writers | off the hot path, batched from a channel |

Rows a grain owns are never written by anything else while the emulator runs.

## Grains and their state

| Grain | Key | Holds |
|---|---|---|
| `PlayerGrain` | player id | the card; summary snapshot for rooms |
| `PlayerPresenceGrain` | player id | the socket observer, outgoing queue, active room, stream subscription |
| `PlayerDirectoryGrain` | global, `[KeepAlive]` | name ↔ id |
| `RoomGrain` | room id | `RoomLiveState` (map, tiles, items, avatars, rights) via modules and systems |
| `RoomPersistenceGrain` | room id | the dirty-item queue |
| `RoomDirectoryGrain` | global, `[KeepAlive]` | active rooms and who is in them |
| `ServerConfigGrain` | global | `server_config` rows |

`RoomGrain` is one class split by facet (`RoomGrain.*.cs`) and delegates to modules
(`Grains/Modules`: avatar, map, furniture, objects, security, events) and systems (`Grains/Systems`:
tick, pathing, furniture location store). Furniture behaviour is a `[RoomObjectLogic]` class bound by
`furniture_definitions.logic`.

## Plugins and the web API

`Vortex.Plugins/PluginManager` loads plugins from published folders (`Vortex:Plugin:DevPluginPaths`
in Development). The web API is one: it builds its **own** service container and receives host
services through `WebApiWebHost.ForwardSingletons` / `UseHostService<T>`. A host service it names but
the host no longer registers fails at start or per request.

## Observability

Metrics (`IVortexMetrics`, `/metrics` behind a token), the audit channel (`IAuditSink` →
`AuditWriterService` → `audit_events`), item forensics (`item_events`), error grouping
(`error_groups`), retention sweeps (`ForensicsRetentionService`). Event handlers under
`Vortex.Observability/Events` are discovered by scan.

## Guard rails that are scripts, not prose

| Script | Fails on |
|---|---|
| `scripts/hooks/check-architecture-walls.mjs` (FastCheck) | a handler using the DB, protocol imports in Primitives, a protocol-free project referencing the protocol, a reference up the layers |
| `scripts/hooks/check-webapi-forwarding.mjs` (FastCheck) | a `[FromServices]` type the web API container never receives |
| `scripts/hooks/check-inert-declarations.mjs` | a handler without parser, a composer without serializer |
| `scripts/hooks/check-authorization-surface.mjs` | an actor-taking grain method or route whose authorization idiom changed |
| `Vortex.Hosting.Tests/Architecture/*` | interleaving manifest, room-entry boundaries, single-silo inventory |
