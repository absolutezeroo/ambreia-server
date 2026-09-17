# Deployment

## Purpose

The container image and the compose stack — including what the compose stack does *not* cover.

## The image

`Dockerfile`, two stages.

**Build** on `mcr.microsoft.com/dotnet/sdk:10.0` (a 9.x SDK "cannot satisfy that pin" — `global.json`
pins `10.0`):

- a restore layer built from `*/*.csproj` only, flattened by `COPY` then moved back into
  `<Name>/`. **This is why the `<Name>/<Name>.csproj` convention is load-bearing** — break it and the
  restore layer silently misses a project
- `dotnet restore Vortex.Main/Vortex.Main.csproj`, then `dotnet publish --no-restore`

**Runtime** on **`aspnet:10.0`, not `runtime`** — the comment explains why: `Vortex.WebApi` and
`Vortex.Dashboard.API` each build their own Kestrel `WebApplication` inside the process.
→ [Hosting](hosting.md)

Runs as the image's non-root `app` user (uid 1654), pre-creating `logs/`, `plugins/` and `assets/`
chowned to `app`.

```
EXPOSE 30000 30001 8080 9000
ENTRYPOINT dotnet Vortex.Main.dll
```

Orleans' silo (11111) and gateway (3000) ports are **deliberately not published** — a single
in-process silo has no reason to accept cluster traffic.

> `dotnet build` also runs `npm ci` + Vite for the dashboard SPA, so **Node 22 is a hard build
> prerequisite**. `Vortex.Dashboard.API/Assets/` is generated and gitignored, and
> `VerifyDashboardFrontendEmbedded` errors if the build would embed nothing.

## The compose stack

Two services.

**`mysql:8.0`** — host port **3307**, named volume `vortex-mysql-data`, and a healthcheck that
authenticates as the *application* user over TCP rather than as root over a socket.

**`vortex`** — with `depends_on: condition: service_healthy`, which is **mandatory, not tidiness**:
`ServerVersion.AutoDetect` opens a real connection *during DI configuration*, outside
`EnableRetryOnFailure`. Without the health gate the container races the database and dies.

Three details worth understanding before copying it:

| Detail | Why |
|---|---|
| `DOTNET_ENVIRONMENT: Development` | deliberate. Outside Development the host **refuses** localhost clustering + in-memory grain storage unless `AllowUnclusteredOutsideDevelopment` is set |
| Two env-var prefixes coexist | `VORTEX__` for the main host, **unprefixed** `serverOptions__…` for the SuperSocket child hosts → [Hosting](hosting.md) |
| `/metrics` enabled with a dev bearer token | a request through a published port arrives from the bridge gateway (172.x), **never 127.0.0.1** — so the loopback default is not "the developer's machine" inside a container |

## Port map

| Port | Serves |
|---|---|
| 30000 (dev 40000) | game TCP |
| 30001 (dev 40001) | game WebSocket |
| 8080 | web API, `/health`, `/metrics` |
| 9000 (HTTPS 9443) | operator dashboard |

## The supervisor was removed (2026-09-17)

`Vortex.Supervisor` was a separate process that ran the emulator as a **child**, on the reasoning
that *"nothing living inside a process can restart that process"*: it started, stopped and restarted
it, carried its stdout both ways, and polled `/health` so an operator had a control page even while
the hotel was down.

It was deleted because the container runtime already is that process. `docker-compose.yml` carries
`restart: unless-stopped` and the `Dockerfile` a `HEALTHCHECK` on `/health` — restart on exit and a
health probe, the supervisor's two jobs. This document already recorded the tell: it appeared in
**neither** `Dockerfile` nor `docker-compose.yml`, so it was wired into no deployment and never ran.

It made sense for the other way of running the hotel — `dotnet Vortex.Main.dll` on a machine, with
nothing to relaunch it and no restart button for someone without SSH. If that deployment ever comes
back, this is what to restore: `git log -- Vortex.Supervisor`.

## Gaps

One, verified:

1. **No `appsettings.Production.json` in the tree.** Production values have to arrive through
   `VORTEX__`-prefixed environment variables, which is what the compose stack does.

## Production checklist

Derived from the validators and gates rather than from a runbook:

- [ ] `VORTEX__Vortex__Database__ConnectionString` set — the shipped value is a rejected placeholder
- [ ] migrations applied — **the host does not apply them**; that is an operations decision
      → [Migrations](../07-database/migrations.md)
- [ ] `Vortex:Database:MySqlServerVersion` pinned, so `AutoDetect` does not open a connection during
      DI configuration
- [ ] if `DOTNET_ENVIRONMENT` is not Development: either real clustering/storage providers, or
      `AllowUnclusteredOutsideDevelopment`
- [ ] if `ClusteringProvider = adonet`: Orleans' own SQL scripts applied first
      (https://aka.ms/orleans-sql-scripts — **not vendored here**)
- [ ] dashboard behind TLS, or `DashboardAllowInsecureRemoteHttp` consciously set
- [ ] `Vortex:WebApi:MetricsToken` set if `/metrics` is reachable off-box
      → [Observability](../10-operations/observability.md)
- [ ] `SeedDashboardStaffActor` applied, or every room-scoped dashboard operation fails
- [ ] one silo only, or `MultiSiloReady` consciously set with the debt understood
      → [Orleans overview](../03-orleans/overview.md)

## Sources

- `Dockerfile`, `docker-compose.yml`, `global.json`
- `Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs`
- `Vortex.Dashboard.API/Vortex.Dashboard.API.csproj`
- `README.md`
