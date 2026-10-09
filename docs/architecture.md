# Architecture map

What runs where, traced on the flows that exist today. Read it when a task crosses projects; for a
single project, the code and `.claude/rules/` are enough.

## Contents
- Processes and layers
- Flow 1 — sign-in (web API → SSO → zone)
- Flow 2 — entering a zone
- Flow 3 — walking (the room tick)
- Flow 4 — publishing a zone (map editor)
- Flow 5 — a zone at run time
- Flow 6 — RPG content
- Flow 7 — the character
- Flow 8 — items
- Flow 9 — interaction
- Flow 10 — chat
- Flow 11 — combat
- Flow 12 — skills and statuses
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

1. The client logs in over HTTP (`POST /api/public/registration/new` or `/authentication/login`) and
   asks `GET /api/ssotoken` for an **account ticket** (`account_tickets`, one per account): the
   socket opens on the character screen (Flow 7). Only the editor's test mode gets a **player
   ticket** (`security_tickets`), straight into a zone. Both follow the same rules: not single-use unless
   `Authentication:TicketSingleUse`, valid up to `TicketAbsoluteLifetimeSeconds` (300 s) from issue,
   sliding on each use. `IAuthenticationService` (`Vortex.Players/Authentication`) issues both
   (`Issue…TicketAsync`) and resolves them (`ResolveTicketAsync` answers a `TicketOwner`); the
   password rules, hash and check are `IAccountAuthenticator`'s (`Vortex.WebApi.Authentication`).
2. The client connects to the game socket. Frames are `[int32 length][int16 header][body]`, plaintext
   until the Diffie handshake installs RC4 (`Vortex.Networking/Package`).
3. `PackageHandler` finds the parser for the header in the session's revision
   (`Revision20260701`, maps in `Maps/`); no parser → warning, packet dropped. The typed message goes
   through `MessageSystem` to the `IMessageHandler<T>` found by scan.
4. `SSOTicketMessageHandler` resolves the ticket.
   - A player ticket goes to `IWorldEntry.EnterAsync` (`Vortex.Players/Sessions`): the session is
     bound to the player (`SessionGateway.AddSessionToPlayerAsync` → `PlayerPresenceGrain`), then
     `PlayerPresenceGrain.EnterWorldAsync` sends `AuthenticationOK`, `UserRights`, `RoomForward` (where the player lands: see Characters below; by default
     `world.start_room_id`, the zone of `scripts/sql/dev_zone.sql`) and the character's state.
   - An account ticket opens the **account phase** (spec F3a): `SessionGateway.AddSessionToAccountAsync`
     binds the socket to `AccountSessionGrain` (one socket per account, a second one replaces it with
     `ConcurrentLogin`), and `AccountCharactersGrain.OpenAsync` sends `RpgCharacterList` and
     `RpgClassList`. The character screen's packets (9000–9003, `IAccountPhaseMessage`) pass
     `AccountPhaseGateBehavior` only in this phase and `RpgIntentLimitBehavior` once per interval;
     `AccountCharactersGrain` creates (« Sans classe », 5 per account), gives a class and answers
     `RpgCharacterResult`. `RpgEnterWorld` checks ownership, then takes the same `IWorldEntry` path:
     the socket leaves the account phase for the player.

## Flow 2 — entering a zone

`OpenFlatConnection` → `PlayerPresenceGrain.EnterRoomAsync` (`PlayerPresenceGrain.Room.cs`), the
only way into a room (the room pointer has no setter on the grain interface):

- a dungeon is redirected to the group's copy (below);
- marks the room pending, sends `OpenConnection`;
- `RoomGrain.AdmitAsync` (`RoomGrain.Entry.cs`) activates the room (map built from the room model,
  furniture loaded), checks the door (full, locked, password; skipped with rights or on a resume)
  and the cancellable `PlayerEnteringRoomEvent`, and builds the burst from its own state:
  `RoomReady`, entry tile, properties, `HeightMap`, `FloorHeightMap`, `Users`, `UserUpdate`,
  `Objects`, `Items`, controller level;
- the presence grain sends it, then subscribes the player to the room's Orleans stream
  (`RoomOutbound`) and creates the avatar: everything the room broadcasts reaches the player
  through their presence grain. The room never calls the presence grain back during the entry.

## Flow 3 — walking

`MoveAvatar` → `RoomGrain.ClickTileAsync` then `RoomGrain.WalkAvatarToAsync` → `RoomAvatarModule`
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
`ZoneSettingsApplier` puts the zone's name, capacity and walls on the room snapshot in memory.
`Vortex.Rooms/Grains` keeps the grains at its root (`RoomGrain` and its partials,
`RoomDirectoryGrain`, `ZoneInstanceGrain`, `ZoneTestGrain`) and their parts in one folder per
kind, namespace = folder: `Modules/`, `Systems/`, `State/` (`RoomLiveState`, `RoomItemIndex`),
`Persistence/` (`RoomPersistenceGrain`, the furniture location store) and `Zones/`, itself one kind
per folder: `Compilation/` (document → `CompiledZone`, room model, settings), `Runtime/` (what
changes while players are in: `ZoneRuntimeState`, visitors, cooldowns, expected arrivals, and
`ZoneRuleBook`, a cell's rules), `Messaging/` (`ZoneBubble`), `Modules/` (the `RoomGrain` modules
below), `Interactions/` and `Triggers/` (their per-kind classes). The modules answer, each for one
thing:

| Module | Answers | Driven by |
|---|---|---|
| `ZoneArrivalModule` | where an entering avatar appears: expected entry or cell > first `arrival` > door | `RoomAvatarModule.CreateAvatarFromPlayerAsync` |
| `ZoneAreaModule` | which areas a player stands in; `ZoneAreaEntered/LeftEvent`, the area bubble | each committed step (`OnZoneStepAsync`) |
| `ZonePassageModule` | passages: same zone on the spot, another through `IRoomZone.ExpectArrivalAsync` + `RoomForward` | the step, teleporters, `Teleport` actions |
| `ZoneInteractionModule` + `Interactions/` | `UseFurniture`/`UseWallItem` on zone decor: one class per `interaction` | `RoomActionModule.UseItemByIdAsync` |
| `ZoneTriggerModule` + `Triggers/` | triggers on the room event bus and the `zone-clock` tick step; one evaluator per condition, one executor per action | `RoomEventModule`, the room tick |

A `Dungeon` zone is played in copies, one per group: the room entry asks the room
`IRoomZone.InstanceForAsync` and forwards the player to the copy `IZoneInstanceGrain` hands out. A
copy's room id (`ZoneInstanceIds`, from 1 000 000 000) names its template, whose row, zone, rights
and visit log it uses; the copy is released when its room deactivates. Proof: `driver.mjs dungeon`.

The map reads zone overrides through `RoomMapModule.CanWalkOn/CanSitOn/CanLayOn` (doors, decor
collision). The editor's test mode: `POST /api/editor/zones/{key}/test` → `IZoneTestGrain` → the SSO
handler forwards the tester's next sign-in to the zone. Proofs: `driver.mjs travel`, `testmode`.

## Flow 6 — RPG content

Classes, specializations, the XP curve and items are JSON documents in `content/`, one per file,
checked against `schemas/content/*.schema.json` (spec `2026-10-08-rpg-content-design.md`).
`scripts/content/push.mjs` (or `driver.mjs content`) sends the whole pack to
`POST /api/content/import` (`content.publish`). `RpgContentService` validates it all
(`ContentPackValidator`: one `IContentKind` per kind, declared in `RpgModule`), diffs it by hash
against `rpg_content`, refuses a removal players still hold without a `replace` rule
(`IContentReferences`), writes everything in one transaction with a new `rpg_content_packs` row
(its id is the version), then reloads the registry. Imports run one at a time.

`RpgContentRegistry` (`IRpgContent`) is a reference cache, stage 1 after the furniture catalogue:
it reads the tables, validates them like an import and swaps its whole set; invalid stored content
refuses the start, and a failed reload keeps the set it had. Every system reads content through
`IRpgContent.Get<T>/All<T>` and listens to `ContentReloadedEvent`. Texts are served raw by
`GET /api/public/content/texts/{language}`.

## Flow 7 — the character

Every avatar is a character (spec `2026-10-08-rpg-character-design.md`): it starts in the starter
class « Sans classe » (`class.classless`, `"starter": true`), given at creation on the character
screen (`RpgCharacterCreate`, through `IAccountCharactersGrain`) and, for an avatar older than that,
when the screen opens. It leaves the starter once for a real class (`RpgCharacterChooseClass`),
keeping level and XP. Every path calls `IPlayerCharacterGrain`
(`Vortex.Rpg.Characters`, key = player id), the sole writer of `player_characters`: class and
specialization once, level and XP through `GainXpAsync` (server only; a level is written at once,
XP on a 30 s timer and at deactivation), stats from the `IStatSource`s declared in
`CharactersModule` summed by `StatCalculator`. An import's `replace` rule rewrites the class ids
(`ClassReferences`); the grain re-reads them when `IRpgContent.Version` moves and never writes them
back.

The character pushes its own state through the player's presence: `RpgCharacterState` on sign-in
and on every room entered, `RpgCharacterStats` then `RpgCharacterProgress` after XP (stats only on a
level up). Proof of the whole character screen by the socket: `driver.mjs lobby`.

Sign-in (`PlayerPresenceGrain.EnterWorldAsync`) forwards to: a pending editor test entry, else the room the
avatar is still standing in during its grace period, else the character's last world zone if still
published, else `world.start_room_id`. `LastZoneHandler` offers every room entered to the grain,
which keeps world zones only. A socket that drops in a room leaves the avatar there, still, for
`PlayerPresenceConfig.DisconnectGraceSeconds` (60): a sign-in in time resumes it
(`TakeResumeAsync`, no second avatar), otherwise it leaves as before. Proofs: `driver.mjs
lobby`, `grace wait`.

## Flow 8 — items

A character's items (spec `2026-10-08-rpg-items-design.md`) belong to its `PlayerCharacterGrain`
(`PlayerCharacterGrain.Items.cs`): a bag of `ItemConfig.BagSize` (30) cases, ten equipment slots
(`EquipmentSlots`), up to `MaxParcels` (50) parcels for what a full bag could not take. The rules
are a pure class, `CharacterInventory` (`Vortex.Rpg/Items`): every operation checks before it
changes anything. `CharacterItemStore` is the only code on `rpg_items` / `rpg_item_grants`. A
grant is server side only, placed on a copy and written at once in one transaction with its key,
so a replayed key grants nothing (`duplicate_grant`); a destroy, a removal and a binding are also
written before they count; moves, splits and equipment are flushed every `FlushIntervalMs` (2 s)
and at deactivation. The worn items reach the stats through `StatSourceContext.Equipment`
(`EquipmentStatSource`). `definition_id` is never written from memory: an import's `replace` rule
rewrites it (`ItemReferences`) and the grain re-reads it when `IRpgContent.Version` moves. Grants,
destroys, removals and bindings land in `item_events` under the `Rpg…` types. Admin grant:
`POST /api/admin/characters/{playerId}/items` (`economy.item.grant`); proof: `driver.mjs items`.

## Flow 9 — interaction

Spec `2026-10-09-rpg-interaction-design.md`. A zone's decor is frozen: in a room with a
`ZoneRuntime`, `RoomActionModule.UseItemByIdAsync` refuses the Habbo furniture path for any object
the zone interactions (`ZoneInteractionModule`: door, lever, sign, teleport, sit, use) did not take,
whatever the clicker's rights; the object's `Interact` triggers have already fired. What can be
clicked is computed once, `CompiledZone.Interactives` (an interaction, or `interact` for decor an
Interact trigger listens to), and sent with the entry burst as `RpgZoneInteractives` (9008). A
player's target lives in the room: `IRoomTargets.SelectTargetAsync` (packet `RpgTargetSelect`,
9010) → `RoomTargetModule` keeps it in `RoomLiveState.Targets`, answers `RpgTarget` (9009: name,
and level and class read from the target's `PlayerCharacterGrain`) to that player alone, clears it
for an id that is no avatar, and on `PlayerLeftEvent` clears it for whoever aimed at the one
leaving. Runtime only. Proofs: `driver.mjs travel`, `target`.

## Flow 10 — chat

Spec `2026-10-09-rpg-chat-design.md`. No Habbo bubbles: a channelled RPG chat. `RpgChatSend` (9011:
channel, target, text) → `RpgChatSendMessageHandler` → `PlayerPresenceGrain.SendChatAsync` of the
sender → `ChatPipeline` (`Vortex.Players/Chat`): the channel declared and writable by a player, the
text cleaned (control characters, outer spaces; empty → `empty`), the declared `IChatRule`s by
priority (length 200 → `too_long`, rate per channel from `Vortex:Chat:RateLimits` → `rate_limited`;
windows kept in the presence's `ChatSenderState`), the links (`[prefix:id]` of a declared
`IChatLinkKind`, kept if the id exists, at most 5; `item` is declared by `RpgModule`), then the
cancellable `ChatSendingEvent`. The channel routes through `IChatRouting`, which the presence
implements: `general` publishes `RpgChatMessage` (9010) on the sender's room stream, `world` on the
`world-chat` stream every presence joins on world entry, `whisper` finds the name in
`PlayerDirectoryGrain` (case aside), asks the other presence `IsOnlineAsync` (interleaved) and sends
to both. `group` and `guild` are placeholders refusing `not_in_group` / `not_in_guild`. A refusal
is `RpgChatResult` (9011) to the sender alone; a delivered line gets none, and `ChatSentEvent`
follows. `system` is server-only: `SendSystemChatAsync`. Extension points: a module declares an
`IChatChannel` (the last one per key wins; a shipped channel, `IsDefault`, always yields; a
presence listens to its room stream and `world-chat` only, so a group channel delivers member by
member until its module adds a stream), an `IChatRule` or an
`IChatLinkKind` in its own `Chat/` folder; the core names none. Nothing stored, no text logged.
`trade` and `recruitment` are two more `WorldChatChannel`s on `world-chat` (1 message / 30 s). RP
actions (`RpgEmote`, in 9012 / out 9017) go to the room, not the presence: `IRoomEmotes` →
`RoomEmoteModule` checks a configured key (`Vortex:Rooms:EmoteKeys`), an avatar here and the
interval, and broadcasts on the room stream; nothing is kept. Proof: `driver.mjs chat`.

## Flow 11 — combat

Spec `2026-10-09-rpg-combat-m1a-design.md`. The core `Vortex.Rpg` defines the seam: `IZoneRuntime`
(grid, sight, avatars, walking within each avatar's rules and a per-tick path budget, sending, the
clock) and `IZoneSimulation` (+ `IZoneSimulationFactory`). `RoomGrain` implements the runtime
(`Zones/Runtime/RoomZoneRuntime.cs`), creates the simulation in `ApplyZoneAsync` (none for a room
without a zone) and runs it in a `"combat"` tick step every `CombatTickMs` (250 ms). `Vortex.Rooms`
references the core only: the simulation is `Vortex.Rpg.Monsters`' `RpgZoneSimulationFactory`, by
DI. It runs, per tick and only with a player in the zone: `MonsterSpawner` (the zone's `ZoneSpawn`s,
born on the first tick with a player, back after `RespawnSeconds`), each monster's `IMonsterBehavior`
by key (`melee`: wander, aggro, chase, leash → home invulnerable, never on a safe cell), then
`Vortex.Rpg.Combat`'s `CombatZone`: swings at each fighter's cadence (a player out of reach walks to
it), the `IHitStep` chain (dodge, damage, crit, armor, minimum), threat, deaths (a player: ghost
5 s, respawn entry at 50 %, equipment worn; a monster: XP to each participant still here,
`MonsterKilledEvent`), combat exits and regeneration, then one `RpgCombatEvents`/`RpgVitals` per
tick to the zone and `RpgSelfVitals`/`RpgDeath`/`RpgCombatResult` to the player. A monster is a
`RoomMonsterAvatar` (a Habbo pet, no object logic). Health and mana belong to `PlayerCharacterGrain`
(`current_health`, `current_mana`, `vitals_at`): the room reads them once on entry
(`GetCombatProfileAsync`) and writes them back once, awaited, before the avatar leaves (or the room
deactivates); new stats, level or class reach the room as a fresh profile through the presence
(`OnCharacterStatsChangedAsync` → `IRoomCombat`), whose health and mana the zone ignores. A click on a
monster (`RpgTargetSelect`) goes to the simulation first; a manual walk stops the attack. Proof:
`driver.mjs combat`.

## Flow 12 — skills and statuses

Spec `2026-10-09-rpg-skills-m1b-design.md`. `CombatZone` names no skill: it ticks the
`ICombatSystem`s declared by DI (in their `Order`, before the swings), recomputes each fighter's
effective stats from its base and every `IStatModifierSource`, and offers `Damage`/`Heal`/`AddThreat`,
a message queue and parts on fighters and on the zone. `Vortex.Rpg.Skills` brings two systems:
`StatusSystem` (order 100: pulses of damage and heal over time, expiries, modifiers, stun → rooted,
slow → `SetMoveSpeed`; statuses drop on death, the persistent ones are carried out with the vitals
and back in on entry) and `CastSystem` (order 200, the zone's `ICastingSystem`: the checks of spec
§6.1, the approach, an instant landing at the next tick, a cast at its end, a channel by pulses, every
cancellation). Effects are `ISkillEffect`s by key (`damage`, `heal`, `apply_status`, `interrupt`,
`dispel`, `threat`), each one instance seen both by the import (`SkillKind` checks the fields) and by
the zone; `DamageModifierStep` (350) and `ShieldStep` (950) join the hit chain. Skills a character knows
come from content (`KnownSkills`), never stored; its bar is `action_bar`, written by
`PlayerCharacterGrain`. Wire: `RpgSkillCast`/`RpgCastCancel` → `IRoomCombat` → simulation;
`RpgActionBarSet` → the character; out `RpgSkills`, `RpgSkillResult`, `RpgCastStart`/`RpgCastEnd`,
`RpgCooldowns`, `RpgStatuses`, `RpgSkillEvents`, grouped per tick. Proof: `driver.mjs skills`.

## Persistence

| State | Owner | Written |
|---|---|---|
| player card (name, motto, figure) | `PlayerGrain` | on change and on deactivation |
| presence (session, active/pending room) | `PlayerPresenceGrain` | in memory (Orleans storage) |
| placed furniture | `RoomGrain` → `RoomPersistenceGrain` | queued, flushed every `DirtyItemsTickMs` and drained on deactivation |
| tile heights | `RoomGrain` | flushed by the tick |
| character items (`rpg_items`, `rpg_item_grants`) | `PlayerCharacterGrain` | grant, destroy, removal, binding at once; moves every 2 s and on deactivation |
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
