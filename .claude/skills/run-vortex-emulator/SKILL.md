---
name: run-vortex-emulator
description: Builds, launches and drives the Vortex RPG emulator on this Windows machine — start/stop, health, raw game-socket frames, and a throwaway player taken from registration through SSO into the start zone and one step. Use when asked to run or start the emulator, check it is alive, probe a packet, log a player in, or prove a change works in the running app rather than in a build log.
---

# Run the Vortex emulator

One deployable: `Vortex.Main`, which hosts the game socket, the web API (a plugin) and a single
in-process Orleans silo. Drive it with `node .claude/skills/run-vortex-emulator/driver.mjs <command>`
— it starts the process, speaks raw Habbo frames to the game socket, and walks a throwaway player
from registration into the start zone. No dependencies; Node 24 and the SDK are already here.

Paths are relative to the repository root. **This is Windows** — the README's `docker compose up`
cannot run here (no Docker on this machine).

## Prerequisites

| | |
|---|---|
| .NET SDK | `dotnet --version` → `10.0.301` (pinned by `global.json`) |
| Node | `node --version` → `v24.14.1` |
| MySQL | Laragon, `C:\laragon\bin\mysql\mysql-8.4.3-winx64\` — the version directory changes, the driver globs it |

The database is the one in Vortex.Main's user-secrets (`Vortex:Database:ConnectionString`); the
driver's `cleanup` SQL reads the same secret. A fresh fork database needs, in order:

1. `scripts/ef.ps1 database update` (InitialCreate, with its seeds: `__world__`, server config);
2. `scripts/import-reference-data.ps1 -Target <db>` — room models and furniture definitions from
   `turbo` (read only), then `scripts/sql/dev_zone.sql`: the development zone on `model_a`, and
   `world.start_room_id` pointing at it.

Without step 2 the SSO handler logs "No start zone configured" and the player stays on the hotel
view. Laragon does not start with Windows: `node .claude/skills/run-vortex-emulator/driver.mjs mysql`.

## Run (agent path)

**Run the driver from PowerShell**, or prefix `MSYS_NO_PATHCONV=1` from Git Bash.

```powershell
node .claude/skills/run-vortex-emulator/driver.mjs smoke
```

`smoke` is start → health → game-socket ping → `play`. `play` is the core loop, end to end:

1. registers `run-skill@vortex.local` through the web API (or logs it in) and asks `/api/ssotoken`
   for an account ticket — what the client's login does;
2. sends `SSOTicket` (882) on the game socket and expects `RpgCharacterList` (9000); creates a
   character (`RpgCharacterCreate`, 9001) if the account has none, sends `RpgEnterWorld` (9003)
   for the first one and expects `AuthenticationOK` (230) and `RoomForward` (3339) carrying the
   start zone's id;
3. sends `OpenFlatConnection` (3234) for that room and expects the entry burst, heightmaps (2260,
   2885) and `Users` (996) included;
4. sends `MoveAvatar` (2364) one tile off the entry tile and expects a `UserUpdate` (2613).

| Command | What it does |
|---|---|
| `smoke` | start + health + ping + play |
| `mysql` | starts Laragon's `mysqld` if 3306 is closed |
| `start` | launches `Vortex.Main`; **reuses** an instance it did not start, never stops one |
| `status` | pids and the ports |
| `stop` | stops **only** an emulator this driver started; refuses otherwise — the owner stops theirs |
| `logs [n]` | tail of the console log of an emulator this driver started |
| `health` | `/health`, `/api/public/info/hello` |
| `ping` | raw frame on :40000 — `InitDiffieHandshake` (2022) out, 3309 back |
| `play` | the four steps above |
| `zone` | publishes `proof_zone` through `/api/editor` (owner role granted to the throwaway account on the target database), enters it (decor in `Objects`), republishes while inside and expects `RoomForward`. Point it at a scratch database: `VORTEX__Vortex__Database__ConnectionString` is read by both the emulator and the driver |
| `enter <key>` | enters the published zone `<key>` (made with the map editor, say) and counts the floor items the room sends |
| `travel` | publishes `proof_a` and `proof_b`, then walks them on the socket: a closed door refuses the step until `UseFurniture` opens it, a sign speaks, a named area and its `EnterZone` trigger speak and open a gate, a passage forwards to `proof_b` and the player lands on the passage's entry |
| `testmode` | the editor's test route: a ticket whose sign-in lands on `proof_b` (6,6); run `travel` first |
| `dungeon` | publishes `proof_crypt` (type `Dungeon`), opens its room and expects a `RoomForward` to the player's own copy (id from 1 000 000 000), then enters the copy |
| `content [dir]` | pushes the RPG content pack (`content/`, or a copy at `dir` relative to the repo) through `scripts/content/push.mjs` as the throwaway account, made owner on the target database. Prints every finding; exit 1 when the pack is refused. A second push of the same pack answers 0 added, 0 changed. By hand: `VORTEX_CONTENT_PASSWORD=… node scripts/content/push.mjs --email <owner>` |
| `items` | the item grant (spec Objets): `POST /api/admin/characters/{id}/items` gives the throwaway account's first character a rusted sword, then again with the same key → `ok`, then `duplicate_grant`, and exactly one more sword row in `rpg_items` (account made owner on the target database). Run `play` first |
| `lobby` | the character screen by the socket alone (spec F3a): a fresh web session's account ticket → `RpgCharacterList` and `RpgClassList`, no `AuthenticationOK`; `RpgCharacterCreate` → result `ok` and the list; `RpgCharacterChooseClass(class.warrior)` → `ok`; `RpgEnterWorld` → `AuthenticationOK`, `RoomForward`; room entry → `RpgCharacterState` (level 1); 250 XP by the admin route → `RpgCharacterStats`, `RpgCharacterProgress` (level 3); a create sent in the world → nothing (phase gate). Push the content first; a full account (5 characters) wants `cleanup` |
| `cleanup` | deletes the throwaway account and its players |

State (pid file, console log) lives in `%TEMP%\vortex-run\`, never in the repo.

### The game socket is the real proof

Frame layout, from `Vortex.Networking/Package/ClientPacketDecoder.cs`:
`[int32 BE length][int16 BE header][body]`, `length` covering header **and** body; a string is an
int16 length then UTF-8 bytes. Plaintext until the Diffie handshake completes — `SetupEncryption`
is what installs the RC4 engines, so a session that never completes it never encrypts. One round
trip walks the socket, the decoder, the revision parser map, the handler, the composer, the
serializer and the encoder. **A green build proves none of it** — a serializer that disagrees with
the client compiles perfectly.

A packet with no parser is ignored with a warning, not a disconnect: the client still sends the
hotel's requests, and the console says which ones went unanswered. Headers are in
`Vortex.Revisions/Revision20260701/Headers.cs`; to probe another message, pass its header and
fields to `frame()` in the driver.

## Build

The emulator holds its own DLLs, so **the full host build only completes while nothing is
running it**. While the hotel is up, build only the project you changed:

```powershell
dotnet build Vortex.Rooms/Vortex.Rooms.csproj --nodeReuse:false
```

Restarting to pick up a host change is the **user's** call, not yours.

## Gotchas

- **The dev game ports are 40000/40001 when `appsettings.Development.json` exists** (it overrides
  `serverOptions`), 30000/30001 otherwise; the driver picks by the file's presence, or by
  `VORTEX_GAME_PORT`. WebStorm holds 30000/30001 on this machine.
- **Without `appsettings.Development.json`** three things are missing, each passed as an
  environment variable before `start`: the RSA keypair
  (`VORTEX__Vortex__Crypto__PublicKey`/`PrivateKey`/`KeySize` — the host refuses the placeholders),
  the game ports (`serverOptions__TcpServer__listeners__0__port`,
  `serverOptions__WebSocketServer__listeners__0__port`, unprefixed — SuperSocket builds its own
  generic host, outside the `VORTEX__` prefix) and the plugin (`VORTEX__Vortex__Plugin__DevPluginPaths__0`).
  Point the plugin path at a `dotnet publish` output of `Vortex.WebApi`, not at its `bin`: a library
  build does not copy Swashbuckle, and the plugin then dies at start-up with
  `Could not load Swashbuckle.AspNetCore.SwaggerGen` while the host reports it loaded.
- **Never stop the running emulator to unblock a build.** `scripts/hooks/guard-emulator.mjs` blocks
  the command and `driver.mjs stop` refuses a pid it did not spawn. Building `Vortex.Main` while it
  runs fails with `MSB3027 ... verrouillé par : "Vortex.Main"`. Build the library instead.
- **A stale MSBuild node can lock `obj\`** (`CS2012 ... used by another process`, nothing running):
  `dotnet build-server shutdown`.
- **Never read a build result through a pipe.** `dotnet build ... | tail -40` reported `exit 0` on a
  build that printed `ÉCHEC de la build`. Redirect to a file, then read it.
- **Web sessions are in-memory.** A host restart drops them; `play` logs in again on every run.
- Migrations are **never** applied at startup unless `Vortex:Database:MigrateOnStartup` is set. A
  schema change needs `scripts/ef.ps1 database update` run deliberately.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `ECONNREFUSED 127.0.0.1:3306` at startup | Laragon MySQL is not running. `driver.mjs mysql`. |
| `MSB3027 ... verrouillé par : "Vortex.Main"` | The hotel is up. Build the changed project only; do not kill it. |
| `play` → "No RoomForward" | `world.start_room_id` is 0: run `scripts/import-reference-data.ps1` (it applies `dev_zone.sql`). |
| `play` → "No AuthenticationOK" | The ticket was refused: consumed, expired, or minted for another IP. Re-run `play`. |
| `play` → "refused or silent" | `CantConnect` or no entry: the zone's door mode, or a missing room model. |
| `driver.mjs stop` refuses | Working as designed: that emulator is the user's. |

## Related

- `AGENTS.md` → "Required validation before completion" for the gate targets.
