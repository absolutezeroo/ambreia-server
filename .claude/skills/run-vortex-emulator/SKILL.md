---
name: run-vortex-emulator
description: Build, launch and drive the Vortex Cloud Habbo emulator on this machine. Use when asked to run or start the emulator, check it is alive, hit its game socket or dashboard API, screenshot a dashboard page, or confirm a change works in the running app rather than in a build log.
---

# Run the Vortex emulator

One deployable: `Vortex.Main`, which hosts the game sockets, the web API, the operator dashboard
and a single in-process Orleans silo. Drive it with
`node .claude/skills/run-vortex-emulator/driver.mjs <command>` — it starts the process, speaks raw
Habbo frames to the game socket, holds a dashboard session, and screenshots dashboard routes
through headless Edge over CDP. No dependencies; Node 24 and the SDK are already here.

Paths are relative to the repository root. **This is Windows** — the README's `docker compose up`
cannot run here (no Docker on this machine).

## Prerequisites

Already installed and verified on this machine — nothing to `install`:

| | |
|---|---|
| .NET SDK | `dotnet --version` → `10.0.301` (pinned by `global.json`) |
| Node | `node --version` → `v24.14.1` (the driver needs 22+ for the global `WebSocket`) |
| MySQL | Laragon, `C:\laragon\bin\mysql\mysql-8.4.3-winx64\` — the version directory changes, the driver globs it |
| Edge | `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`, for screenshots |

The database is the one in Vortex.Main's user-secrets (`Vortex:Database:ConnectionString`, e.g.
`vortex_m0_check` on `127.0.0.1:3306`, `root`); the driver's `login`/`cleanup` SQL reads the same
secret, and only falls back to the old `turbo`/`admin` when there is none. A fresh fork database
needs `scripts/import-reference-data.ps1` before rooms can be built. Laragon does not start with
Windows:

```powershell
node .claude/skills/run-vortex-emulator/driver.mjs mysql
```

## Run (agent path)

**Run the driver from PowerShell.** Git Bash rewrites a `/api/...` argument into
`C:/Program Files/Git/api/...`; from Bash, prefix `MSYS_NO_PATHCONV=1`.

```powershell
node .claude/skills/run-vortex-emulator/driver.mjs smoke
```

`smoke` is start → health → game-socket ping → dashboard login → one authenticated read. Verified
output against the live hotel:

```
Vortex.Main already running (pid 46252) and NOT started by this driver.
Reusing it. Do not stop it -- drive it with `ping`, `health`, `api`.
GET :8080/health -> 200 {"status":"Healthy","database":"up","degradedServices":[]}
GET :8080/api/public/info/hello -> 200 {"status":"ok"}
GET :8080/metrics -> 200  Vortex_players_online... 1  Vortex_sessions_active... 1
GET :9000/ -> 200 (644 bytes of SPA shell)
game socket OK: sent 544(1234) -> got 188(1234)
Registered run-skill@vortex.local.
Dashboard session for account 14 (role 5) -> ...\vortex-run\dash-cookie.txt
GET :9000/api/v1/monitoring/overview -> 200
Smoke OK.
```

| Command | What it does |
|---|---|
| `smoke` | the five steps above |
| `mysql` | starts Laragon's `mysqld` if 3306 is closed |
| `start` | launches `Vortex.Main`; **reuses** an instance it did not start, never stops one |
| `status` | pids and the four ports |
| `stop` | stops **only** an emulator this driver started; refuses otherwise |
| `logs [n]` | tail of the console log of an emulator this driver started |
| `health` | `/health`, `/api/public/info/hello`, `/metrics`, dashboard shell |
| `ping [id]` | raw Habbo frame on :40000 — sends 544, expects 188 back |
| `login` | throwaway owner account + `dash_session` cookie |
| `api <path>` | authenticated GET against the dashboard API |
| `shot [route] [png]` | headless-Edge screenshot of a dashboard route, cookie injected |
| `cleanup` | deletes the throwaway operator account |

State (pid file, console log, cookie, screenshots) lives in `%TEMP%\vortex-run\`, never in the repo.

### The game socket is the real proof

```powershell
node .claude/skills/run-vortex-emulator/driver.mjs ping
# game socket OK: sent 544(1234) -> got 188(1234)
```

Frame layout, from `Vortex.Networking/Package/ClientPacketDecoder.cs`:
`[int32 BE length][int16 BE header][body]`, `length` covering header **and** body. Plaintext until
the Diffie handshake runs — `SetupEncryption` is what installs the RC4 engines, so an
unauthenticated probe never encrypts. Header `544` is `LatencyPingRequestMessageEvent`, answered by
`LatencyPingResponseMessageComposer` `188` echoing the request id. That single round trip walks the
socket, the pipeline filter, the decoder, the revision parser map, the handler, the composer, the
serializer and the encoder. **A green build proves none of it** — the dominant live bug here is a
serializer that disagrees with the client, which compiles perfectly.

To probe a different message, pass its header and int arguments through `frame()` in the driver.

### Dashboard

```powershell
node .claude/skills/run-vortex-emulator/driver.mjs login
node .claude/skills/run-vortex-emulator/driver.mjs api /api/v1/monitoring/overview
node .claude/skills/run-vortex-emulator/driver.mjs shot '#/rooms' "$env:TEMP\dash.png"
```

Routes are `/api/v1/...` (`ApiV1` in `Vortex.Dashboard.API/Hosting/DashboardEndpoints.cs`);
`/api/overview` is a 404. `login` registers `run-skill@vortex.local` through the public web API —
which mints the bcrypt hash, so no hand-crafted password SQL — then grants role 5 (`owner`, the one
carrying `*`) in `player_account_roles`. Registration alone gets you a 403: the dashboard authorizes
on capabilities, not on having an account.

`shot` drives Edge over CDP because plain `--screenshot` only ever photographs the login form —
every route but `/login` is behind `dash_session`. **The SPA is hash-routed**: `AppShell.svelte`
reads `window.location.hash`, so the page is `#/rooms`, and `shot /rooms` silently renders the
overview instead — a screenshot that looks fine and is the wrong page. Route names are in
`Vortex.Dashboard.Web/src/lib/routes.js`. **Then `Read` the PNG.** For iterating on
dashboard *visuals*, use the `dashboard-render` skill instead: it runs Vite with HMR against this
same emulator and needs no rebuild.

## Build

The emulator holds its own DLLs, so **the full host build only completes while nothing is
running it**:

```powershell
dotnet build Vortex.Main/Vortex.Main.csproj      # ~6 min cold: npm ci + Vite are inside it
```

While the hotel is up, build only the project you changed — that succeeds and is what to use:

```powershell
dotnet build Vortex.Rooms/Vortex.Rooms.csproj --nodeReuse:false
# 0 Erreur(s), 00:00:11.65
```

Restarting to pick up a host change is the **user's** call, not yours.

## Run (human path)

The README's `$env:DOTNET_ENVIRONMENT="Development"; dotnet run --project Vortex.Main/Vortex.Main.csproj`
blocks on the console with the log stream — the same thing the driver spawns detached. **Not run in
this session**: an emulator was already up, and a second instance collides on :9000 and :40000.

## Gotchas

- **The dev game ports are 40000/40001 when `appsettings.Development.json` exists** (it overrides
  `serverOptions`), 30000/30001 otherwise; the driver picks by the file's presence, or by
  `VORTEX_GAME_PORT`. WebStorm holds 30000/30001 on this machine.
- **Without `appsettings.Development.json`** (the fork's checkout has none) three things are missing,
  each passed as an environment variable before `start`: the RSA keypair
  (`VORTEX__Vortex__Crypto__PublicKey`/`PrivateKey`/`KeySize` — the host refuses the placeholders),
  the game ports (`serverOptions__TcpServer__listeners__0__port`, `serverOptions__WebSocketServer__listeners__0__port`,
  unprefixed) and the plugins (`VORTEX__Vortex__Plugin__DevPluginPaths__0`/`__1`). Point the plugin
  paths at `dotnet publish` outputs of `Vortex.Dashboard.API` and `Vortex.WebApi`, not at their
  `bin`: a library build does not copy Swashbuckle, and both plugins then die at start-up with
  `Could not load Swashbuckle.AspNetCore.SwaggerGen` while the host reports "Loaded 2 plugins".
- **Republishing the dashboard into a running host breaks it**: the hot reload fails with
  `ObjectDisposedException: DefaultMeterFactory` and :9000 goes away. `stop`, then `start`.
  Those two listeners are configured through *unprefixed* env vars because SuperSocket builds its
  own generic host, outside the `VORTEX__` prefix.
- **Never stop the running emulator to unblock a build.** It is the live-verification hotel;
  `scripts/hooks/guard-emulator.mjs` blocks the command and `driver.mjs stop` refuses a pid it did
  not spawn. Building `Vortex.Main` while it runs fails with
  `MSB3027 ... Le fichier est verrouillé par : "Vortex.Main (46252)"`. Build the library instead.
- **A second, unrelated lock exists.** With no emulator running at all, a cold build still failed
  with `CS2012 ... Vortex.WebApi\obj\Debug\net10.0\Vortex.WebApi.dll ... used by another process` —
  a stale MSBuild node-reuse process left by Rider (`Get-CimInstance Win32_Process -Filter
  "Name='dotnet.exe'"` listed ~60 of them). `--nodeReuse:false` on *your* build does not release
  *its* node — `dotnet build-server shutdown` is what targets those.
- **Never read a build result through a pipe.** `dotnet build ... | tail -40` reported `exit 0` on a
  build that printed `ÉCHEC de la build`. Redirect to a file, then echo `$?`.
- **Dashboard sessions are in-memory.** `AccountSessionStore` is a dictionary with an 8-hour
  lifetime, so a lifetime is never the reason a cookie dies — a host restart is. A path that
  answered 200 a minute ago and now 401s means the emulator restarted; re-run `login`.
- **CDP `Network.setCookie` with `domain:` silently stores nothing** in Edge. It returns
  `success: true` and the only symptom is a screenshot of the login form. Pass `url:` instead.
- **Git Bash mangles paths in arguments.** `driver.mjs api /api/v1/...` becomes
  `http://127.0.0.1:9000C:/Program Files/Git/api/v1/...`. PowerShell, or `MSYS_NO_PATHCONV=1`.
- **`Vortex_sessions_active` counts sockets, `Vortex_players_online` counts logins.** The gap
  between them is the login funnel: the gateway counts a socket from connect, before auth.
- Migrations are **never** applied at startup. A schema change needs `dotnet ef database update`
  run deliberately from `Vortex.Database`.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `ECONNREFUSED 127.0.0.1:3306` at startup | Laragon MySQL is not running. `driver.mjs mysql`. `ServerVersion.AutoDetect` opens a real connection *during DI configuration*, so a missing DB is a hard startup failure, not a retried transient. |
| `MSB3027 ... verrouillé par : "Vortex.Main"` | The hotel is up. Build the changed project only; do not kill it. |
| `CS2012 ... obj\...\*.dll ... used by another process`, nothing running | Stale Rider MSBuild node. Retry or `dotnet build-server shutdown`. |
| `driver.mjs api` → 401 | Emulator restarted since `login`. Re-run `login`. |
| `driver.mjs api` → 403 | Account exists but has no capability. `login` grants role 5; check `player_account_roles`. |
| `driver.mjs api` → 404 on `/api/overview` | Routes are `/api/v1/...`. |
| `TypeError: Invalid URL ... input: 'http://127.0.0.1:9000C:/Program Files/Git/...'` | Git Bash path conversion. PowerShell, or `MSYS_NO_PATHCONV=1`. |
| Screenshot shows the login form | Cookie missing or dead. Re-run `login`, then `shot`. |
| Screenshot shows the overview for a route that is not the overview | Path route instead of hash route. `shot '#/rooms'`, not `shot /rooms`. |
| `shot` → `ERR_CONNECTION_REFUSED` on a bare `127.0.0.1` | Same path mangling — the route argument was rewritten before it reached the driver. |
| `driver.mjs stop` refuses | Working as designed: that emulator is the user's. |

## Related

- `dashboard-render` — iterate on dashboard visuals with Vite HMR; the CSS an operator sees is
  embedded in the assembly, so `npm run build` exiting 0 proves nothing about the render.
- `AGENTS.md` → "Required validation before completion" for the gate targets.
