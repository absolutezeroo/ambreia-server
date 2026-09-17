# Configuration

## Purpose

Where settings come from, the three key namespaces that behave differently, and the keys that gate
behaviour rather than tune it.

## Sources, in order

1. `appsettings.json` — **at the repository root**, not in `Vortex.Main`.
   `Vortex.Main.csproj` copies `../appsettings*.json` to output.
2. `appsettings.Development.json` — **gitignored** (`.gitignore`: `**/appsettings.*.json`).
3. Environment variables with the prefix **`VORTEX__`**, stripped, `__` → `:`.

```
VORTEX__Vortex__Database__ConnectionString  →  Vortex:Database:ConnectionString
```

In Development, `Program.cs` dumps every `JsonConfigurationProvider` and its resolved physical path —
so "which appsettings did it actually read" is answerable without guessing.

## Three namespaces, three behaviours

| Namespace | Bound by | Reaches |
|---|---|---|
| `Vortex:*` | 22 `SECTION_NAME` config classes, mostly with `ValidateOnStart()` | the main host |
| `serverOptions:*` | SuperSocket's own `ServerOptions` | **only** the two game listener child hosts — **unprefixed env vars** |
| `Logging:*` | the standard logging builder | everything |

The `serverOptions` split is the first configuration trap:
[Hosting](hosting.md).

## Gate keys

Six keys do not tune anything — they decide whether the process runs at all, or whether a subsystem
exists.

| Key | Default | Effect |
|---|---|---|
| `Vortex:Orleans:MultiSiloReady` | `false` | **startup throws** when >1 active silo |
| `Vortex:Orleans:AllowUnclusteredOutsideDevelopment` | `false` | **startup throws** on localhost+memory outside Development |
| `Vortex:Observability:DashboardEnabled` | code `false`, shipped `true` | off = the dashboard `BackgroundService` returns immediately |
| `Vortex:Observability:DashboardRequired` | `false` | fatal vs degraded |
| `Vortex:WebApi:Enabled` | code `false`, shipped `true` | off = no `/health`, no `/metrics` |
| `Vortex:WebApi:MetricsEnabled` | **`false`** | off = no scrape endpoint even when the web API is on |

Note the pattern: **the code default and the shipped default differ** for the two hosts, so reading
the class alone is misleading.

## Notable keys by consumer

| Key | Consumer | Effect |
|---|---|---|
| `Vortex:Database:ConnectionString` | `AddVortexDatabaseContext`, and Orleans for ADO.NET | `ValidateOnStart`; placeholders rejected; **never echoed** |
| `Vortex:Database:MySqlServerVersion` | runtime EF | unset → `AutoDetect`, which **opens a connection during DI configuration**, outside `EnableRetryOnFailure` |
| `Vortex:Orleans:GrainCollectionAge` | idle deactivation | `00:02:00` |
| `Vortex:Orleans:ClusteringProvider` / `GrainStorageProvider` | silo | `adonet` needs Orleans' SQL scripts already applied. Storage affects **PubSubStore only** |
| `Vortex:Networking:MaxPacketBodyBytes` | `ClientPacketDecoder` | 65536 |
| `Vortex:Networking:PongTimeout` | WS heartbeat | **`Zero` = never close**, with a measured rationale |
| `Vortex:Networking:RateLimit:*` | `TokenBucketRateLimiter` | 50/s, burst 100 |
| `Vortex:Rooms:*` | `RoomConfig`, also `IWiredLimits` | **no section exists in appsettings** — all compiled defaults |
| `Vortex:PlayerPresence:MaxOutgoingQueueSize` | presence queue | 500, drop oldest |
| `Vortex:Protocol:*` | 5 revision maps | wire-safety ceilings, not business policy |
| `Vortex:Revisions:DefaultRevisionId` | `RevisionRegistrationService` | **not set** — "first registration wins" decides |
| `Vortex:Plugin:HotReloadEnabled` | `PluginHotReloadService` | see the mismatch below |
| `Vortex:Commerce:Recovery:*` | `CommerceRelayService` | 30 s / 100 / 10 min |

## Runtime-editable config is not here

Tunable business values live in **`IServerConfigGrain`** — a DB-backed, admin-editable KV store with a
write-through cache — not in `IOptions<T>`.

```
messenger.max_friends            300
messenger.message_history_limit  50
club.gift_cycle_days             31
club.streak_grace_days           7
club.kickback_percent            10
club.badge_code / vip_badge_code HC1 / HC2
benchmark.enabled                false
```

The split is deliberate and stated in the config classes: **infrastructure timing stays in
`IOptions<T>`; anything an operator should change without a restart goes to the grain or an
admin-editable table.**

> `AGENTS.md` and `CONTEXT.md` both cite `Vortex:FriendList:UserFriendLimit` as the canonical example
> of a handler-supplied configured limit. **That key does not exist** — grep across `.cs` and `.json`
> returns nothing. `PacketHandlersModule` says so in a comment: *"Tunable moderation/friend-list
> limits moved to IServerConfigGrain (runtime-editable); nothing left to bind here."*

## Undeclared keys

Some handlers read `IConfiguration` directly for keys that appear in **no** config class and **no**
appsettings file. They work — they fall back to their defaults — but they are undiscoverable:

```
Vortex:Players:{NameMinLength, NameMaxLength, NameSuggestionCount}
Vortex:Nux:{DefaultRoomModel, RoomModels:<type>, StarterRoomMaxPlayers, StarterRoomCategoryId}
Vortex:Quests:{DailyTaskCount, DailyBonusTaskCount, CommunityGoalHallOfFameSize}
Vortex:Rooms:DailyRespectLimit
```

## Known inconsistencies

All verified at this commit.

| Issue | Detail |
|---|---|
| **Two names, one concept** | runtime reads `Vortex:Database:MySqlServerVersion`; design-time EF reads `Vortex:Database:ServerVersion`. Setting one does nothing for the other |
| **Hot reload is off by default even in Development** | `ServiceCollectionExtensions` gates registration on a **raw** `pluginSection.GetValue<bool>("HotReloadEnabled")`, which returns `false` when the key is absent — while `PluginConfig.HotReloadEnabled` defaults to `true`. Neither appsettings file has a `Vortex:Plugin` section at all |
| **Dead section** | `appsettings.json` declares `"Game": {}`; nothing binds `Vortex:Game` |
| **Dead key** | `appsettings.Development.json` sets `Vortex:Database:LoggingEnabled`, which is not a property of `DatabaseConfig` |
| **No production file** | There is no `appsettings.Production.json` in the tree; production values arrive as `VORTEX__` environment variables |

## Validation

Config classes bind with `.ValidateOnStart()`, so a bad value fails at startup rather than at first
use. Notable validators:

- `DatabaseConfigValidator` — rejects empty and any of `CHANGE_ME` / `REPLACE_ME` / `YOUR_CONNECTION` /
  `PLACEHOLDER`, and **never echoes the value**
- `OrleansHostConfigValidator` — `AdvertisedIp` must parse as a literal IP (hostnames rejected), ports
  in range and mutually distinct, **and distinct from every game listener port**
- `PluginConfigValidator` — path **syntax** only; a not-yet-built dev folder is normal

Pinned by `Vortex.Hosting.Tests/ConfigValidationTests.cs` and `ListenerSecurityTests.cs`.

## Prefix inventory

[Configuration index](../generated/configuration-index.md) lists the key prefixes a static scan finds.
It is a prefix inventory, not the full key set — the 22 config classes above are the authoritative
list.

## Sources

- `Vortex.Main/Program.cs` — the provider dump, `AddEnvironmentVariables`
- the 22 `SECTION_NAME`-bound option classes across `Vortex.*/Configuration/` (25 `*Config.cs` files)
- `Vortex.Main/Configuration/{OrleansHostConfig,OrleansHostConfigValidator}.cs`
- `Vortex.Database/Configuration/{DatabaseConfig,DatabaseConfigValidator}.cs`
- `Vortex.Plugins/Configuration/{PluginConfig,PluginConfigValidator}.cs`, `Extensions/ServiceCollectionExtensions.cs`
- `Vortex.PacketHandlers/PacketHandlersModule.cs` — the friend-limit comment
- `appsettings.json`, `appsettings.Development.json`, `docker-compose.yml`
