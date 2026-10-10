# Vortex Cloud

## 5-Minute Quickstart
1. Clone and enter the repo.
2. Run bootstrap once.
3. Set your local DB connection string.
4. Run the app in Development mode.

Clone:

```bash
git clone <your-repo-url> vortex-cloud
cd vortex-cloud
```

Bootstrap (PowerShell):

```powershell
pwsh -File scripts/bootstrap.ps1
```

Bootstrap (bash/zsh):

```bash
sh scripts/bootstrap.sh
```

Set `Vortex:Database:ConnectionString` in `appsettings.Development.json`, then run:

PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT="Development"; dotnet run --project Vortex.Main/Vortex.Main.csproj
```

bash/zsh:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project Vortex.Main/Vortex.Main.csproj
```

## What This Repository Is
`Vortex.Cloud.sln` is the main Vortex emulator solution.
It includes the host executable (`Vortex.Main`), domain modules (`Vortex.Rooms`, `Vortex.Players`, `Vortex.Database`, and others), networking/message layers, and plugin infrastructure.

## Tooling Baseline
- .NET SDK 9.x (pinned via `global.json`)
- Git
- MySQL running locally (or reachable dev instance)

Check SDK:

```bash
dotnet --version
```

## Local Configuration
- `appsettings.json` contains shared defaults.
- `appsettings.Development.json` is local-only and gitignored.
- The bootstrap script creates `appsettings.Development.json` from `appsettings.json` if missing.

## Démarrage par conteneur (Container Startup)
`Dockerfile`, `.dockerignore` and `docker-compose.yml` at the repository root build and run
`Vortex.Main` plus a MySQL 8 instance. They are build/run artefacts only — no application code
depends on them, and running the emulator from the SDK as described above is unaffected.

### `docker compose up`

```bash
docker compose build
docker compose up
```

The `vortex` service waits on `depends_on: mysql: condition: service_healthy`. This is required,
not cosmetic: `Vortex.Database/Extensions/ServiceCollectionExtensions.cs` resolves the server
version with `ServerVersion.AutoDetect(connectionString)` whenever
`Vortex:Database:MySqlServerVersion` is unset (the default). `AutoDetect` opens a real MySQL
connection *during DI configuration*, before the host is built and outside the reach of
`EnableRetryOnFailure`, so a database that is not yet accepting connections is a hard startup
failure rather than a retried transient.

**The schema is not created for you.** Migrations are never applied at host startup — see
[Applying migrations](#applying-migrations) below. On a fresh volume, start the stack, apply the
migrations once, then restart `vortex`.

### What is exposed, on which ports

| Host port | Container | Service |
| --- | --- | --- |
| 30000 | `vortex` | Game client, raw TCP socket (SuperSocket) |
| 30001 | `vortex` | Game client, WebSocket socket (SuperSocket) |
| 8080 | `vortex` | Vortex web API — Swagger UI at `http://localhost:8080/swagger` |
| 3307 | `mysql` | MySQL 8 (3307 on the host to avoid a local MySQL already on 3306) |

The Orleans silo (11111) and gateway (3000) ports are deliberately not published: the stack runs a
single in-process silo.

MySQL data lives in the named volume `vortex-mysql-data`, so `docker compose down` keeps the
schema and `docker compose down -v` destroys it.

### The `ListenerSecurity` guard, and why the compose file opts in

`Vortex.Primitives/Hosting/ListenerSecurity.cs` refuses to start an HTTP listener bound to a
non-local address with HTTPS disabled, because the HTTP surface carries credentials and a session
cookie. Its `IsLocalHost` treats `0.0.0.0`, `::`, `*` and `+` as **not** local — they accept
traffic on every interface, which is precisely the remote-exposure case.

Inside a container, a listener bound to `127.0.0.1` is unreachable: published ports are forwarded
to the container's external interface. The listeners must bind `0.0.0.0`, so the guard fires.

The compose file does not bypass or modify the guard. It sets the opt-in key the code already
reads:

| Configuration key | Declared in | Environment variable in `docker-compose.yml` |
| --- | --- | --- |
| `Vortex:WebApi:AllowInsecureRemoteHttp` | `Vortex.WebApi/Configuration/WebApiConfig.cs` | `VORTEX__Vortex__WebApi__AllowInsecureRemoteHttp` |

With those set, `ValidateListener` returns `AllowedWithWarning` instead of `Refused`, and the
warning is still logged on every start.

**This is a local development setting only.** It is acceptable here because the published ports
are meant to be reached from `localhost` on the developer's machine. Anywhere the ports are
reachable from a network, remove that variable and either enable HTTPS on the listeners or
bind them to a local address behind a TLS-terminating reverse proxy.

Note that the two game sockets are configured through *unprefixed* variables
(`serverOptions__TcpServer__listeners__0__ip`, `serverOptions__WebSocketServer__listeners__0__ip`).
`Vortex.Networking/NetworkManager.cs` builds them with `SuperSocketHostBuilder.Create()`, which
bottoms out in `Host.CreateDefaultBuilder(args)` — a separate generic host with its own
configuration root. The `VORTEX__` prefix registered in `Vortex.Main/Program.cs` does not reach it;
`CreateDefaultBuilder` reads unprefixed environment variables. `ListenerSecurity` does not police
these two — it guards the HTTP surfaces only.

### Applying migrations
Migrations are **not** applied automatically when the host starts; when to migrate is an operations
decision. Apply them explicitly against the running `mysql` service.

A single-node deployment can hand that decision to the host instead:

```
Vortex:Database:MigrateOnStartup = true      # VORTEX__Vortex__Database__MigrateOnStartup
```

Pending migrations are then applied **before any listener opens**, and a failure stops the boot with
a non-zero exit code rather than letting the host serve a schema its code does not match. Leave it
off for anything running more than one host: two of them racing the same migration is a real hazard.
It logs the pending ids before applying them, because a migration that fails halfway leaves the
history table showing only what committed.

On a development machine, `scripts/ef.ps1` forwards to `dotnet ef` with the working directory,
the *unprefixed* connection-string variable and the pinned server version already set — the three
things the design-time factory needs and that fail quietly when they are wrong:

```powershell
scripts/ef.ps1 migrations add AddThing
scripts/ef.ps1 database update
scripts/ef.ps1 migrations script --idempotent --output artifacts/migrations.sql
```

`dotnet-ef` is in `.config/dotnet-tools.json`, so the wrapper's `dotnet tool restore` installs it on
first use. It also warns when a migration file name and its `[Migration]` id disagree, which is
invisible until EF applies a migration under an id the history table does not expect.

The runtime image is intentionally SDK-free, so run the EF tooling from a throwaway SDK container
joined to the compose network, with the repository mounted:

```bash
docker compose up -d mysql

docker run --rm \
  --network vortex-cloud_default \
  -v "$PWD:/src" -w /src/Vortex.Database \
  -e Vortex__Database__ConnectionString="Server=mysql;Port=3306;Database=vortex;User Id=vortex;Password=vortex-dev;" \
  -e Vortex__Database__ServerVersion="8.0-mysql" \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'dotnet tool install --global dotnet-ef && export PATH="$PATH:/root/.dotnet/tools" && dotnet ef database update'
```

PowerShell: replace `$PWD` with `${PWD}` and the line continuations with backticks.

Details that matter:
- `dotnet ef` is not in `.config/dotnet-tools.json`, hence the `dotnet tool install` in the command.
- The working directory must be a project directory: `VortexDbContextFactory` reads
  `appsettings.json` from `Directory.GetCurrentDirectory()/..`, i.e. the repository root.
- Both variables are **unprefixed**. That factory builds its own configuration with a plain
  `AddEnvironmentVariables()`, so `VORTEX__…` would be ignored, and it reads
  `Vortex:Database:ServerVersion` (not `MySqlServerVersion`, which is the runtime host's key).
  Pinning the version skips `AutoDetect`.

From the host instead of a container, the same thing works against the published port:

```bash
cd Vortex.Database
Vortex__Database__ConnectionString="Server=127.0.0.1;Port=3307;Database=vortex;User Id=vortex;Password=vortex-dev;" \
Vortex__Database__ServerVersion="8.0-mysql" \
dotnet ef database update
```

### Importing reference data
A freshly migrated database has no room model or furniture definition. Copy them from an older
Vortex/Turbo database (read only), and create the development zone, with:

```powershell
scripts/import-reference-data.ps1 -Source turbo -Target vortex_noyau_check
```

It copies the shared columns of `room_models` and `furniture_definitions`, replays the seeds that only
match once furniture definitions exist, then runs `scripts/sql/dev_zone.sql`: one zone on `model_a`,
and `world.start_room_id` pointing at it so every login lands there. Re-runnable; the header of `scripts/sql/import_reference_data.sql` says
what a re-run replaces.

### Real secrets outside development
Every credential in `docker-compose.yml` is a development value committed to the repository, so it
is public: the MySQL passwords, the RSA handshake keypair and the IP-hash secret. The compose file
is a local dev stack, not a deployment.

To run this anywhere else:
- Move every `VORTEX__…` value out of the compose file. It reaches the host through
  `AddEnvironmentVariables(prefix: "VORTEX__")` in `Vortex.Main/Program.cs`, so any mechanism that
  sets process environment variables works: an untracked `.env` file referenced with
  `env_file:` (`.env` is already in `.dockerignore`), Docker/Swarm secrets read into the variable
  at entrypoint, or the orchestrator's own secret store (Kubernetes `Secret` via `envFrom`, ECS
  task-definition secrets, systemd `EnvironmentFile=`).
- Generate a fresh RSA keypair — `openssl genrsa -3 1024` — and configure the client with the
  matching modulus. `CryptoConfigValidator` rejects the `CHANGE_ME` placeholders, so a
  half-configured host fails at startup rather than at the first handshake.
- Set a real `Vortex:Authentication:IpHashSecret`. Outside Development the validator refuses the
  repository defaults, since a known HMAC key makes the hashed IPs in auth events reversible.
- Drop `DOTNET_ENVIRONMENT: Development`. Outside Development,
  `Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs` refuses to start Orleans with
  localhost clustering plus in-memory grain storage, which is what this single-container stack
  uses. A real deployment needs `Vortex:Orleans:ClusteringProvider` and
  `Vortex:Orleans:GrainStorageProvider` set to `adonet` — or an explicit
  `AllowUnclusteredOutsideDevelopment` if a single node with non-durable Orleans state is genuinely
  what you want.
- Drop the two `AllowInsecureRemoteHttp` opt-ins and terminate TLS in front of the listeners.
- Behind that proxy, set `Vortex:WebApi:UseForwardedHeaders` and list it in `KnownProxies` or
  `KnownNetworks`: outside Development the host refuses to start without them, because the login,
  registration and ticket rate limits would otherwise see the proxy's address and throttle the
  whole hotel as one client.

## Quality Model (Two-Phase)
- Fast local commit check:
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
- Full quality gate (pre-push + CI):
  - `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`
- AI policy rollout phase:
  - Default is `VortexAIPolicyPhase=1` (warn-first).
  - Preview strict mode with `-p:VortexAIPolicyPhase=2`.

Hooks are repository-managed in `.githooks`:
- `pre-commit` runs the fast check.
- `pre-push` runs the full quality gate.

## Build Scope Matrix
| Command | Scope | Default? | Use when |
| --- | --- | --- | --- |
| `dotnet build Vortex.Main/Vortex.Main.csproj` | Core emulator only | Yes | Normal core development and CI-compatible local checks |
| `dotnet build Vortex.Cloud.sln` | All projects currently in solution (including sample plugin) | No | One-window integrated core + plugin work |
| `dotnet build ../turbo-sample-plugin/TurboSamplePlugin/TurboSamplePlugin.csproj` | Sample plugin only | No | Plugin-only iteration |

`TurboSamplePlugin` intentionally stays in `Vortex.Cloud.sln` for IDE convenience, but the default repo build contract is project-scoped to `Vortex.Main`.

## Daily Commands
- Core build (default): `dotnet build Vortex.Main/Vortex.Main.csproj`
- Integrated solution build (optional): `dotnet build Vortex.Cloud.sln`
- Plugin build only (optional): `dotnet build ../turbo-sample-plugin/TurboSamplePlugin/TurboSamplePlugin.csproj`
- Fast checks: `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck`
- Full quality gate: `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`
- Run in Development: `dotnet run --project Vortex.Main/Vortex.Main.csproj`

## Local Dev Plugins
Plugin loading supports both the runtime plugin folder and dev-specific paths:
- Default folder: `<runtime>/plugins`
- Optional config: `Vortex:Plugin:DevPluginPaths`

Example:

```json
{
  "Vortex": {
    "Plugin": {
      "DevPluginPaths": [
        "C:/Users/you/RiderProjects/turbo-sample-plugin/TurboSamplePlugin/bin/Debug/net10.0"
      ]
    }
  }
}
```

If the same plugin key exists in both places, `DevPluginPaths` wins and a warning is logged.

### Plugin project setup
Your plugin's `.csproj` must copy `manifest.json` to the build output:

```xml
<ItemGroup>
  <Content Include="manifest.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

You can list multiple plugin paths in `DevPluginPaths` when developing several plugins at once.

### Two-terminal dev workflow
Terminal 1 (run emulator):

```bash
dotnet run --project Vortex.Main
```

Terminal 2 (watch plugin):

```bash
cd C:/path/to/your-plugin
dotnet watch build
```

When `dotnet watch` rebuilds your plugin, Vortex Cloud detects the new DLL and hot-reloads the plugin in-process.

### Plugin hot-reload limitations
- Grain types cannot be hot-reloaded because Orleans grain type registration happens at silo startup.
- Memory may grow over many reloads if assembly references are retained; restart periodically during long sessions.

### Integrated plugin dev loop (single terminal flow)
Canonical integrated workflow lives in the plugin repo:
- Guide: `../turbo-sample-plugin/README.md`
- PowerShell: `pwsh -File ../turbo-sample-plugin/scripts/dev-integrated.ps1`
- bash/zsh: `sh ../turbo-sample-plugin/scripts/dev-integrated.sh`

## Orleans Notes
Vortex Cloud uses Orleans as its core runtime model for stateful domain workflows.
Runtime flows, grains and persistence: `docs/architecture.md`.

## Troubleshooting
### MySQL connection errors
If you see `Unable to connect to any of the specified MySQL hosts`:
1. Verify `Vortex:Database:ConnectionString` in `appsettings.Development.json`.
2. Verify MySQL host/port are reachable.
3. Verify no `VORTEX__...` environment variables override your local setting.

### Development file not loading
1. Ensure `DOTNET_ENVIRONMENT=Development` when running.
2. Confirm `appsettings.Development.json` exists at repo root.

### Quality check failures
1. Run `dotnet tool restore`.
2. Run `dotnet csharpier .`.
3. Run `dotnet format style`.
4. Run `dotnet format analyzers`.
5. Re-run `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`.

### Solution build fails but core build passes
If `dotnet build Vortex.Cloud.sln` fails because of plugin project state, use the default core build command:

```bash
dotnet build Vortex.Main/Vortex.Main.csproj
```

## AI-Assisted Development
- `AGENTS.md` — the contract every AI tool reads first: commands, where code goes, rules, gotchas,
  what "done" means. Codex and Copilot read it natively; `CLAUDE.md` imports it.
- `.claude/rules/*.md` — rules scoped to an area (grains, protocol, database, tests), loaded by Claude
  Code when it touches those files and pointed to from `.github/copilot-instructions.md`.
- `.claude/skills/*/SKILL.md` — step-by-step procedures with scripts: adding a packet, changing the
  schema, adding a furniture logic, removing dead code, running the emulator, one-off SQL fixes.
- `docs/architecture.md` — runtime flows (sign-in, zone entry, walking), grains, persistence.
- `DATA-MODEL.md` — schema conventions; `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md` — the RPG design.
