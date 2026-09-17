# Hosting and listeners

## Purpose

One process, five listeners, **three separate DI containers**. Two of the traps in this codebase come
directly from that arrangement.

## The five listeners

| Listener | Port (prod / dev) | Host object | Container |
|---|---|---|---|
| Game TCP | 30000 / 40000 | SuperSocket `IHost` | **its own** |
| Game WebSocket | 30001 / 40001 | SuperSocket `IHost` | **its own** |
| Orleans silo | 11111 (gateway 3000) | the generic host | the root |
| Web API | 8080 | `WebApplication` | **its own** |
| Dashboard | 9000 (HTTPS 9443) | `WebApplication` | **its own** |

`Dockerfile` publishes `EXPOSE 30000 30001 8080 9000`. The Orleans silo and gateway ports are
**deliberately absent** — a single in-process silo has no reason to accept cluster traffic.

## Trap 1 — the SuperSocket hosts do not see `VORTEX__`

`Program.cs` calls `AddEnvironmentVariables(prefix: "VORTEX__")`. `NetworkManager` builds two
independent generic hosts, and **that prefix never reaches them**.

```yaml
# docker-compose.yml — both styles coexist for a reason
VORTEX__Vortex__Database__ConnectionString: …     # the main host
serverOptions__TcpServer__listeners__0__port: …   # the SuperSocket child hosts, UNPREFIXED
```

`NetworkManager.ConfigureCommonServices` re-registers nine singletons captured from the parent
container **by hand**, for the same reason.

`OrleansHostConfigValidator.GameListenerPorts()` reads the same `serverOptions` tree from the parent
so it can refuse a listener port that collides with the silo or gateway port — pinned by
`Vortex.Hosting.Tests/ConfigValidationTests.cs`.

## Trap 2 — the web hosts must hand-forward every service

`WebApplication.CreateSlimBuilder()` starts with an empty container. Both web hosts forward from the
parent explicitly.

The dashboard does it through a named list with a regression test behind it; **`Vortex.WebApi` does
not**:

| | Dashboard | Web API |
|---|---|---|
| Forwarding | `ForwardedServiceTypes` — 12 types, plus 3 more for the pipeline | six services forwarded inline, **no named constant** |
| Endpoint-parameter guard | `DashboardEndpointServiceTests` | none |
| Pipeline-resolution guard | `DashboardPipelineServiceTests` | none |

A missing forward is not a broken route — it is a **dashboard that never starts**. The two failure
modes, and the incident that produced the second test, are on
[Dashboard API architecture](../08-dashboard/api-architecture.md).

## Shared listener policy

Both web hosts use the same two pieces from `Vortex.Primitives/Hosting/`:

**`ListenerSecurity.ValidateListener`** — the one refusal path for cleartext HTTP bound off-box.
Refuses **before a socket opens**, with an escape hatch named in the message
(`DashboardAllowInsecureRemoteHttp` / `Vortex:WebApi:AllowInsecureRemoteHttp`).

**`RequiredServiceGuard.DecideAction(enabled, required)`**:

| State | Action |
|---|---|
| not enabled | `Ignore` |
| required | `FailHost` — `ExitCode = 1`, `StopApplication()` |
| otherwise | `Degrade` — keep serving players, list the service in `DegradedServices` |

Default is degrade, for both.

## Forwarded headers

Both hosts support `X-Forwarded-*`, and both **clear the default known-proxy/network lists** so the
configured allow-list is authoritative.

That is not tidiness. The dashboard's login rate limiter partitions on the remote IP; an untrusted
`X-Forwarded-For` would let a client forge its own partition key and bypass the limit.

## HTTPS

Dashboard: `DashboardHttpsEnabled` / `DashboardHttpsPort` (9443) / `DashboardCertificatePath` /
`DashboardCertificatePassword` / `DashboardHstsEnabled`.

Certificate loading is **eager and failures propagate** — the comment records that a bad path used to
silently skip HTTPS configuration, leaving a listener that looked like HTTPS and was not.

## Vortex.WebApi — the player front door

Not the dashboard. Serves `/api/public/info/hello`, `/health`,
`/api/public/authentication/{login,password,logout}`, `/api/public/registration/new`,
`/api/user/avatars` (GET/POST), `/api/user/avatars/select`, `/api/ssotoken`, `/api/user/look/save`,
`/api/newuser/name/{check,select}`, `/api/newuser/room/select` (a no-op), plus the Prometheus scrape
endpoint.

Four differences from the dashboard that matter:

1. **No capability model at all.** No `AddAuthorization`, no policies, no `[Authorize]`. Every handler
   does `int? accountId = ctx.AccountId(sessions)` by hand and returns 401 itself; ownership is
   re-checked per route (`owned.Exists(a => a.UniqueId == …)` → 403 `avatar_not_owned`). **A forgotten
   check is a silent hole with nothing to catch it** — the dashboard's authorization-matrix test has
   no counterpart here.
2. **No named forwarded-service list**, so neither DI test protects it — the same startup footgun with
   no guard.
3. **It writes to the database directly.** `WebApiPlayerService.CreateAvatarAsync` calls
   `SaveChangesAsync`. Wall 2 does not scan this project. (`SetNameAsync` and `SaveFigureAsync` do go
   through `PlayerGrain`.)
4. **Rate limiting is per route group** (`webapi-login`, `webapi-registration`, `webapi-ssotoken`)
   rather than one login policy.

What the two share, and it is the important part: the same `ListenerSecurity`, the same
`RequiredServiceGuard`, the same `IAccountAuthenticator` — so the second factor cannot fall behind on
one of them — and the same `IAccountPasswordService`, so a password change revokes dashboard sessions
too via `IAccountSessionRevoker`.

## Console commands

One command set, two front ends: stdin via `ConsoleCommandService`, and HTTP via the dashboard's
console endpoint. `Vortex.Main/Console/ConsoleCommandDispatcher.cs` declares 7 commands, **each
carrying a `Capabilities.Dashboard.*` string** — which is how `dashboard.ops.server.control` gates a
command rather than a route.

`Vortex.Hosting.Tests/ConsoleCommandDispatcherTests.cs` asserts `quit` carries
`OpsServerControl` and `help` carries none.

## Configuration summary

| Key | Effect |
|---|---|
| `serverOptions:TcpServer:*`, `serverOptions:WebSocketServer:*` | the two game listeners — **unprefixed env vars only** |
| `Vortex:Observability:DashboardEnabled` | code default `false`, `appsettings.json` sets `true`. Off = the whole `BackgroundService` returns immediately |
| `Vortex:Observability:DashboardRequired` | fatal vs degraded |
| `Vortex:Observability:DashboardFrontendEnabled` | skips `MapFrontend` — an API-only dashboard |
| `Vortex:Observability:DashboardAllowInsecureRemoteHttp` | downgrades the cleartext refusal to a warning |
| `Vortex:WebApi:Enabled` | code default `false`, `appsettings.json` sets `true` |
| `Vortex:WebApi:MetricsEnabled` / `MetricsToken` / `MetricsPath` | → [Observability](../10-operations/observability.md) |

> **Turning `Vortex:WebApi:Enabled` off silently breaks the container health check.**
> The `Dockerfile`'s `HEALTHCHECK` curls `http://localhost:8080/health`, which is a web API route, so
> the container reports itself unhealthy and nothing explains why.

## Sources

- `Vortex.Networking/NetworkManager.cs` — `CreateTcpSocket`, `CreateWsSocket`, `ConfigureCommonServices`
- `Vortex.Dashboard.API/Hosting/DashboardWebHost.cs`
- `Vortex.WebApi/Hosting/{WebApiWebHost,WebApiEndpoints}.cs`, `Services/WebApiPlayerService.cs`
- `Vortex.Primitives/Hosting/{ListenerSecurity,RequiredServiceGuard}.cs`
- `Vortex.Main/Console/ConsoleCommandDispatcher.cs`
- `Vortex.Main/Configuration/OrleansHostConfigValidator.cs`
- `Vortex.Hosting.Tests/{ConfigValidationTests,ListenerSecurityTests,ConsoleCommandDispatcherTests}.cs`
- `Dockerfile`, `docker-compose.yml`
