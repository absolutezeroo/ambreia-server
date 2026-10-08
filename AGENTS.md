# Vortex RPG emulator — agent contract

Server of an online RPG set in the Habbo universe: C# / .NET 10, Orleans (one in-process silo),
EF Core 9 + Pomelo MySQL, SuperSocket. The wire protocol is Habbo's WIN63 revision, and it diverges
on purpose from the official client. Since the core cut (2026-10-07) the server keeps only the
network, sign-in, zones (heightmap, pathfinding, avatars, furniture) and a minimal web API. The RPG
systems come back on this base. Design: `docs/superpowers/specs/2026-10-06-vortex-rpg-design.md` (the
unified spec; where its server scope conflicts with the cut, `docs/superpowers/plans/2026-10-07-coupe-noyau.md`
wins).
Architecture map: `docs/architecture.md` (read it when a task crosses projects).

## Commands

| Goal | Command |
|---|---|
| Build everything | `dotnet build Vortex.Cloud.sln` (redirect to a file; never read a build through a pipe) |
| Fast gate (format, analyzers, walls) | `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck` |
| Full gate (+ every test) | `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate` |
| One test project | `dotnet test Vortex.Rooms.Tests --filter "FullyQualifiedName~Name"` |
| Run it for real | `node .claude/skills/run-vortex-emulator/driver.mjs smoke` (register → SSO → zone → one step) |
| Format | `dotnet csharpier format <files>` (the gate runs `csharpier check .` on the whole repo) |
| Schema | `scripts/ef.ps1 …`; reference data `scripts/import-reference-data.ps1 -Target <db>` |

## Where code goes

| You are adding… | Put it in | Example to copy |
|---|---|---|
| A packet handler | `Vortex.PacketHandlers/<Domain>/<Name>MessageHandler.cs` | `Room/Engine/MoveAvatarMessageHandler.cs` |
| An incoming message / outgoing composer | `Vortex.Protocol/Messages/{Incoming,Outgoing}/<Domain>/` | `Outgoing/Room/Session/RoomForwardMessageComposer.cs` |
| Its parser / serializer + header | `Vortex.Revisions/Revision20260701/{Parsers,Serializers}/`, `Maps/<Domain>Map.cs`, `Headers.cs` | `Maps/HandshakeMap.cs` |
| Stateful per-player / per-room logic | a grain in `Vortex.Players/Grains` or `Vortex.Rooms/Grains` (+ interface in `Vortex.Primitives`) | `Vortex.Players/Grains/PlayerGrain.cs` |
| Room behaviour (walk, map, furniture) | a module or system of `RoomGrain` (`Vortex.Rooms/Grains/Modules`, `Systems`) | `RoomAvatarModule.cs` |
| What a furniture does | a `[RoomObjectLogic("…")]` class in `Vortex.Rooms/Object/Logic` | `Furniture/Floor/FurnitureGateLogic.cs` |
| A contract two layers share (snapshot, id, interface) | `Vortex.Primitives` — never a reference upward | `Orleans/Snapshots/Players/PlayerSummarySnapshot.cs` |
| A table | an entity in `Vortex.Database/Entities` + regenerated `InitialCreate` | `Entities/Room/RoomEntity.cs` |
| A web route | `Vortex.WebApi/Hosting/WebApiEndpoints.cs` + `Services/` | `/api/ssotoken` |
| RPG content (a class, an item…) | a JSON file in `content/<kind>/`, its texts in `content/texts/fr.txt`; pushed with `driver.mjs content` | `content/item/rusted_sword.json` |
| A new kind of content | an `IContentKind` in `Vortex.Rpg/Content/Kinds` + its record in `Definitions/`, declared in `RpgModule`; `UPDATE_CONTENT_SCHEMAS=1` for its schema | `Kinds/ItemKind.cs` |
| An RPG system (character, combat, quests…) | its `Vortex.Rpg.*` project, Platform layer (spec §3.4, §4); add the project to `PLATFORM` in `check-architecture-walls.mjs` | `Vortex.Rpg.Characters` (grain, module, stat source) — ask before creating a project |
| Host wiring, console commands | `Vortex.Main` | `Program.cs` |

Layers, enforced by `scripts/hooks/check-architecture-walls.mjs`: **Core** (Networking, Pipeline,
Protocol, Messages, Revisions, PacketHandlers, Runtime, Crypto, Database, Events, Logging,
Observability, Plugins, Primitives) → **Platform** (Rooms, Players, Furniture, Permissions) →
**Web** (WebApi, WebApi.Authentication). References only point down; `Vortex.Main` wires them all.

## Rules (each one has bitten this repo)

- Handlers orchestrate: validate, call a grain, done. **No `DbContext` in a handler** — put the
  query in the grain or service that owns the data.
- **Never send to a socket from a handler or a service** — the grain that changed state calls
  `PlayerPresenceGrain.SendComposerAsync` (or the room stream for everyone in the room).
- **Grain-owned rows are written by their grain**, never by a script or another service while the
  emulator runs: the grain writes its in-memory copy back over yours.
- **Removing a feature means removing its `Get*Grain` accessor in `GrainFactoryExtensions` and every
  caller.** A grain interface without an implementation compiles and throws on the first call
  (`MoveAvatar` once called the deleted fishing grain on every step).
- **Persisted values are never renumbered**: enum members stored in the database and Orleans
  `[Id(n)]` fields keep their numbers; a removed one leaves its number retired with a comment.
- One migration, `InitialCreate`, **regenerated** rather than stacked. A seed every install needs
  lives in `Vortex.Database/Seeds/` and is applied by `InitialCreate`; a one-off fix is a re-runnable
  script in `scripts/sql/` (the `/sql-fix` skill).
- `turbo` (MySQL) is read-only reference data. The fork's database is in Vortex.Main's user-secrets;
  **never print its password** — pass it through `MYSQL_PWD`.
- A packet with no parser is ignored with a warning, not a disconnect. A handler whose message has
  no parser, or a composer with no serializer, is silently dead:
  `node scripts/hooks/check-inert-declarations.mjs` finds both.
- For a revision other than the embedded `Revision20260701`, edit
  `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`, not this repo.

Path-scoped rules load with the files they cover; read them before editing those areas:
`.claude/rules/grains.md` (Orleans), `.claude/rules/protocol.md` (packets),
`.claude/rules/database.md` (schema, seeds), `.claude/rules/tests.md`.

## Gotchas

- The content registry re-reads every stored document strictly at start, and invalid stored
  content refuses the start. So a field **added** to a `Vortex.Rpg/Content/Definitions` record is
  optional with a default, and a field **removed** stays declared (ignored) until a pack without it
  has been imported. Locked out anyway: `scripts/sql/rpg-content-reset.sql`, then push again.
- A system that stores content ids in a grain-owned row (a character's class) never writes the id
  back from memory: an import's `replace` rule rewrites the row (`IContentReferences`), and the
  grain re-reads the id when `IRpgContent.Version` changes.
- `csharpier` reformats a `.cs` after each edit (Claude hook) and the gate checks the whole repo: a
  file edited by a script must be formatted too.
- A web API `[FromServices]` or `UseHostService<T>` type that the host no longer registers breaks
  the route or the plugin start — `check-webapi-forwarding.mjs` checks the forwarding lists, not
  the host. After removing a host service, grep `Vortex.WebApi` for it.
- `GrainActivationContext.CreateWithIntegerKey<TGrain>(…)` takes `params object[]`: changing a grain
  constructor compiles in every test and fails at activation.
- Event handlers (`IEventHandler<T>`), room object logics and packet handlers are discovered by
  scanning: nothing references them, and one left behind for a removed feature still compiles.
- A `Vortex.Main` the driver did not start cannot be stopped by you (`guard-emulator.mjs` blocks it,
  `driver.mjs stop` refuses): ask the owner, and build only the changed project meanwhile.
- Some tests pin counts and manifests (`interleaving-manifest.yaml`, composer-scan thresholds,
  `scripts/hooks/*-baseline.json`). Updating them is part of the change, not a workaround.

## Done means verified

1. `VortexCloudQualityGate` green (it runs build, format, analyzers, walls and all tests).
2. A change a player can see is checked live: `driver.mjs smoke`, or `play` after a targeted fix.
3. Report the evidence (command and result), not "should work".

Commits: only on `main`, never pushed, with explicit paths (`git commit -o <paths>` or
`--pathspec-from-file`) — the index is shared with the owner. Messages in French.
