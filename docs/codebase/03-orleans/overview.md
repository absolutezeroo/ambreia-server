# Orleans overview

## Purpose

How the silo is configured, why the deployment is single-node, and what Orleans is actually used for
here — which is less than you would expect.

## The one-paragraph version

Orleans provides three things in this codebase: **single-threaded per-key execution** (which is used
as the concurrency model everywhere), **virtual actor activation/deactivation** (which is the caching
strategy), and **memory streams** (which are the room fan-out). It provides essentially **no
persistence** — that is EF Core's job.

## Silo configuration

One `builder.UseOrleans(...)` call, everything inline:
`Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs` — `AddOrleans`.

| Concern | Configuration |
|---|---|
| Clustering | `UseLocalhostClustering(siloPort, gatewayPort)`, or `UseAdoNetClustering` when `ClusteringProvider == "adonet"` |
| Endpoints | `ConfigureEndpoints(AdvertisedIp, siloPort, gatewayPort, listenOnAnyHostAddress: true)` — applied **after** clustering, deliberately (last-writer-wins on `EndpointOptions`) |
| Grain storage | **`PubSubStore` only**, memory or AdoNet |
| Streams | `AddMemoryStreams("DefaultStreamProvider")` + `AddMemoryStreams("RoomStreamProvider")` |
| Collection | `GrainCollectionOptions.CollectionAge`, default **2 minutes** |
| Reminders | **none registered anywhere** — no `IRemindable`, no `RegisterOrUpdateReminder`, no reminder table |
| Serialization | Orleans default codegen. 791 `[GenerateSerializer, Immutable]` types; 15 `[Alias(...)]`, all on room facet interfaces |

The ADO.NET driver is registered only when needed:
`DbProviderFactories.RegisterFactory(hostConfig.Invariant, MySqlConnectorFactory.Instance)`.

## There is no grain persistence

> **No grain in this repository uses `[PersistentState]`.**

`[PersistentState` appears exactly twice repo-wide, both inside comments. The one in
`HostApplicationBuilderExtensions.cs` says it outright:

```
// PLAYER_STORE/ROOM_STORE were never wired to a [PersistentState] grain — every
// grain in this codebase persists through EF Core instead, so only PubSubStore
// (required by the stream providers below) is actually used.
```

`Vortex.Primitives/Orleans/OrleansStorageNames.cs` declares exactly one constant,
`PUB_SUB_STORE = "PubSubStore"`. `OrleansStateNames.PLAYER_STATE` and `PLAYER_PRESENCE` are **dead
constants** with zero consumers — the vestige of a design that was never built.

`Vortex:Orleans:GrainStorageProvider` therefore affects **only** the stream pub/sub store. Setting it
to `adonet` does not make grain state durable, because no grain has Orleans-managed state.

The consequence is the inverse of the usual Orleans warning, and worse. The standard hazard is
"direct DB edits get overwritten by stale store data". Here there is no store to inspect: grains
hydrate from MySQL on activation, hold authoritative mutable state in **plain in-memory fields**, and
write it back. A raw SQL edit is overwritten by a grain's in-memory snapshot.

→ [Persistence and state ownership](persistence.md)

`Vortex.Rooms/Grains/RoomPersistenceGrain.cs` is what a "persistent grain" looks like here: a queue,
a timer, and hand-written EF writes. → [Lifecycle and concurrency](lifecycle-concurrency.md)

## The single-silo thesis, enforced

A second active silo is a startup error:

```csharp
// Vortex.Main/VortexEmulator.cs — RefuseAnUndeclaredSecondSiloAsync, called first in StartAsync
if (_orleansConfig.MultiSiloReady) { return; }

Dictionary<SiloAddress, SiloStatus> hosts = await _grainFactory
    .GetGrain<IManagementGrain>(0).GetHosts(onlyActive: true).WaitAsync(ct);

if (hosts.Count <= 1) { return; }

throw new InvalidOperationException(
    $"{hosts.Count} active silos are in this cluster, and this build's caches, metrics and "
        + "room streams are silo-local: …");
```

`MultiSiloReady` has **no `appsettings.json` entry** — it defaults to `false`, so the gate ships
armed.

What has to become cluster-aware first is inventoried in `OrleansHostConfig.MultiSiloReady`'s XML doc
and asserted against `Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml` by
`Vortex.Hosting.Tests/Architecture/SingleSiloInventoryTests.cs` — **14 reference-data providers and 2
aggregators**. That test fails when a new `IReferenceDataProvider` or `*Aggregator` appears that is
not listed, which is what keeps the debt from growing unnoticed.

The three families of silo-local state:

| Family | Why a second silo breaks it |
|---|---|
| 14 `IReferenceDataProvider` singletons | each process reloads its own copy; an admin edit reaches one node |
| `LiveStatsAggregator`, `RoomPerformanceAggregator` | each aggregates only its own node's measurements |
| `SessionGateway` + memory streams | room fan-out never leaves the silo that owns the room |

None of these fails loudly on a second node. They go quietly stale — which is exactly why the refusal
is a hard throw rather than a warning.

### The second gate

`AddOrleans` also refuses `ClusteringProvider == "localhost" && GrainStorageProvider == "memory"`
**outside Development**, unless `Vortex:Orleans:AllowUnclusteredOutsideDevelopment` is set. With the
opt-in it only warns to stderr.

This is why `docker-compose.yml` sets `DOTNET_ENVIRONMENT: Development` deliberately.

## Streams

Two providers are registered; **one is used**.

`ROOM_STREAM_PROVIDER` carries `RoomOutbound { RoomId, Composer, ExcludedPlayerIds }` from
`RoomGrain.SendComposerToRoomAsync` to every occupant's `PlayerPresenceGrain`, which subscribed as an
`IAsyncObserver<RoomOutbound>` in `SetActiveRoomAsync`.

> `OrleansStreamProviders.DEFAULT_STREAM_PROVIDER` is registered and **never used** — no
> `GetStreamProvider("DefaultStreamProvider")` call exists. Dead registration.

Memory streams are the reason `PubSubStore` must exist at all, and the reason room fan-out cannot
cross a silo.

→ [Presence routing](presence-routing.md)

## Grain access

Every grain has a canonical accessor in `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs`
(348 lines). **Verified programmatically: zero interfaces without an accessor, zero accessors without
an interface.**

```csharp
_grainFactory.GetPlayerWalletGrain(playerId)        // integer key
_grainFactory.GetRoomFurni(roomId)                  // one of 14 room facets, same activation
_grainFactory.GetPlayerDirectoryGrain()             // SingletonGrainId.GLOBAL
_grainFactory.GetVoucherGrain(code)                 // normalises code.Trim().ToUpperInvariant()
```

Two things that live in the accessor rather than the grain: singleton keying (`"global"`) and voucher
code case-normalisation. `Vortex.PacketHandlers` contains **zero** `GetGrain<` calls — all access is
through these.

## Configuration

`Vortex:Orleans` → `Vortex.Main/Configuration/OrleansHostConfig.cs`, bound with `ValidateOnStart()`.

| Key | Default | Effect |
|---|---|---|
| `AdvertisedIp` | `127.0.0.1` | must parse as a literal IP; hostnames rejected |
| `SiloPort` | 11111 | 1–65535, ≠ `GatewayPort`, ≠ any game listener port |
| `GatewayPort` | 3000 | same constraints |
| `GrainCollectionAge` | `00:02:00` | ≥ 1 minute (Orleans quantum) |
| `ClusteringProvider` | `localhost` | `localhost` \| `adonet` |
| `GrainStorageProvider` | `memory` | `memory` \| `adonet` — **PubSubStore only** |
| `Invariant` | `MySqlConnector` | required when either provider is `adonet` |
| `AllowUnclusteredOutsideDevelopment` | `false` | hard startup refusal |
| `MultiSiloReady` | `false` | hard startup refusal |

`appsettings.json` sets only `AdvertisedIp`, `SiloPort` and `GatewayPort`;
`appsettings.Development.json` sets no Orleans section at all. **Both gates ship armed.**

ADO.NET mode reuses `Vortex:Database:ConnectionString` rather than taking its own
(`OrleansHostConfigValidator.Validate`), and requires Orleans' official SQL scripts to be applied
first — a deployment prerequisite this process cannot satisfy for you.

## Known unknowns

- **Unknown:** the Orleans ADO.NET table names.
  - Inspected: `OrleansHostConfig`, `AddOrleans`, the whole `Vortex.Database` migration set.
  - Why unresolved: the scripts are at `https://aka.ms/orleans-sql-scripts`, are not vendored here,
    and no code names the tables.
  - What would resolve it: inspecting a deployed schema, or vendoring the scripts.
- **Unknown:** whether `GrainCollectionAge = 2 minutes` was measured or inherited.
  - Inspected: the XML doc, which says only "the default matches the value this was hardcoded to".
  - Why it matters: `RoomGrain` hydration reads room + rights + group members + mutes, and
    `MessengerGrain` fans `IsOnlineAsync` out over an entire friend list. Two minutes is aggressive
    for both.
  - What would resolve it: an activation-cost benchmark against a realistic population.

## Sources

- `Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs` — `AddOrleans`
- `Vortex.Main/Configuration/OrleansHostConfig.cs`, `OrleansHostConfigValidator.cs`
- `Vortex.Main/VortexEmulator.cs` — `RefuseAnUndeclaredSecondSiloAsync`
- `Vortex.Primitives/Orleans/{OrleansStorageNames,OrleansStateNames,OrleansStreamProviders,GrainFactoryExtensions}.cs`
- `Vortex.Hosting.Tests/Architecture/SingleSiloInventoryTests.cs`
- `Vortex.Hosting.Tests/OrleansEndpointTests.cs`
- `Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml`
- `appsettings.json`, `docker-compose.yml`
