# Startup

## Purpose

What happens between `dotnet Vortex.Main.dll` and the first accepted connection, in order, and why the
order is what it is.

## The sequence

`Vortex.Main/Program.cs` — `Main`.

| # | Step | Why it is here |
|---|---|---|
| 1 | `CultureInfo.DefaultThreadCurrentCulture = InvariantCulture` | **first.** A `fr-FR` host would emit `"1,5"` for a furni altitude and desynchronise the client |
| 2 | bootstrap `ILoggerFactory`, banner, version | so a config failure has somewhere to go |
| 3 | `Host.CreateApplicationBuilder(args)` then `AddEnvironmentVariables(prefix: "VORTEX__")` | prefix stripped, `__` → `:` |
| 4 | dev-only: dump every `JsonConfigurationProvider` and its resolved physical path | "which appsettings did it actually read" |
| 5 | `builder.AddOrleans()` | see below |
| 6 | infrastructure extensions, in a fixed order | see below |
| 7 | 15 `AddHostPlugin<TModule>` | the domain modules |
| 8 | `AssemblyProcessor`, `IConsoleCommandDispatcher`, `ConsoleCommandService` | |
| 9 | `AddHostedService<VortexEmulator>()` — **last** | so the game sockets open after every module's own hosted services |
| 10 | `host.StartAsync(ct)` | |
| 11 | `ConsoleCommandService.Enable()` | the stdin loop starts only after the host is up |
| 12 | on any exception: `LogCritical` + `Environment.ExitCode = 1` | **explicitly**, so the container runtime sees a failure rather than a clean exit |

### Step 5 — `AddOrleans`, in order

`Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs`:

1. bind + `ValidateOnStart` `OrleansHostConfig`; register `OrleansHostConfigValidator`
2. **eagerly** `.Get<OrleansHostConfig>()` — clustering must be decided before `UseOrleans`
3. refuse localhost + memory outside Development unless `AllowUnclusteredOutsideDevelopment`
4. `DbProviderFactories.RegisterFactory` for MySqlConnector, only if either provider is `adonet`
5. `UseOrleans`: `GrainCollectionOptions.CollectionAge` → clustering → **then**
   `ConfigureEndpoints` (after clustering, deliberately — last-writer-wins on `EndpointOptions`) →
   `PubSubStore` → two memory stream providers

→ [Orleans overview](../03-orleans/overview.md)

### Step 6 — infrastructure, in this order

| Extension | Registers |
|---|---|
| `AddVortexLogging` | clears providers, adds the Vortex console formatter and the `ServerConsoleFeed` sink |
| `AddVortexNetworking` | `INetworkManager`, `IRevisionManager`, `ISessionGateway` — **no host started yet** |
| `AddVortexPlugins` | `PluginManager`, hosted `PluginBootstrapper`, dev-only `PluginHotReloadService` |
| `AddVortexDatabaseContext` | `AddPooledDbContextFactory<VortexDbContext>`, `EntityChangeInterceptor`, hosted `CommerceRelayService` and `DatabaseBackupScheduler` |
| `AddVortexEventSystem` | the domain event pipeline |
| `AddVortexMessageSystem` | `MessageFeatureProcessor` as an `IAssemblyFeatureProcessor` |
| `AddVortexCrypto` | RSA/DH, RC4 |
| `AddVortexRevisions` | `IRevision` → `Revision20260701`, hosted `RevisionRegistrationService` |

### Step 7 — the 15 modules

Observability, Authentication, Furniture, Catalog, Player, Social, Progression, Inventory,
Marketplace, **DashboardApi**, Benchmark, Navigator, Room, PacketHandlers, **WebApi**.

Order matters where two modules bind the same section: `DashboardApiModule` and `ObservabilityModule`
both bind `Vortex:Observability`, and both use `TryAddEnumerable` for the validator so it runs once
regardless.

## VortexEmulator

`Vortex.Main/VortexEmulator.cs` — `StartAsync`:

1. `RefuseAnUndeclaredSecondSiloAsync` — **first**
   → [Architecture boundaries](../00-overview/architecture-boundaries.md)
2. group `IReferenceDataProvider` by `LoadStage`, run stages in ascending order with `Task.WhenAll`
   **inside** each stage
3. `_networkManager.StartAsync(ct)` — where the TCP and WebSocket SuperSocket hosts are built and
   started

### Load stages

12 providers at `LoadStage => 0`. **Two at stage 1** — `CatalogSnapshotProvider` and
`CatalogClubGiftProvider` — because they read furniture definitions, which load at stage 0.

Adding a provider that depends on another means giving it a higher stage, not hoping for ordering
inside a `Task.WhenAll`.

## Why hosted-service order matters

`IHostedService`s start in **registration order**. `VortexEmulator` is registered last, so by the time
the first client can connect:

- the dashboard and web API are listening
- the audit and error-grouping writers are draining their channels
- the backup scheduler and commerce relay are running
- every plugin module's own hosted services are up

`PluginBootstrapper` — registered by `AddVortexPlugins`, well before `VortexEmulator` — is what
registers all 539 packet handlers, via the assembly scan.
→ [Packet pipeline](../02-network-protocol/packet-pipeline.md)

## Handler and logic discovery

```
PluginBootstrapper.StartAsync
  ├─ for each IHostPluginModule, in parallel:
  │    AssemblyProcessor.ProcessAsync(module assembly)
  │      └─ every IAssemblyFeatureProcessor:
  │           MessageFeatureProcessor        → IMessageHandler<> / IMessageBehavior<>
  │           EventFeatureProcessor          → IEventHandler<> / IEventBehavior<>
  │           RoomObjectLogicFeatureProcessor → [RoomObjectLogic]
  │           WiredVariableFeatureProcessor  → wired variables
  └─ PluginManager.LoadAllAsync(unloadRemoved: true, ct)   ← external plugins
```

`AssemblyProcessor` rolls back on partial failure. → [Plugins](../09-extensibility/plugins.md)

## Shutdown

`quit` on stdin →
`ConsoleCommandService` stops the host → hosted services stop in **reverse** registration order, so
the game sockets close first and the writers drain last.

`NetworkManager.StopAsync` stops both SuperSocket hosts with a 5 s timeout.
`PluginBootstrapper.StopAsync` unloads plugins leaves-first, all with `CancellationToken.None` — the
comment says abandoning teardown because the host is already stopping *"is how sockets and file
handles survive the process"*.

## Failure behaviour

| Failure | Result |
|---|---|
| Config validation (`ValidateOnStart`) | throws before anything starts |
| Placeholder connection string | `DatabaseConfigValidator` refuses, **without echoing the value** |
| A second active silo | `InvalidOperationException` from `VortexEmulator` |
| Localhost + memory outside Development | throws unless opted in |
| Listener port collides with silo/gateway | `OrleansHostConfigValidator` refuses |
| Cleartext HTTP bound off-box | `ListenerSecurity.ValidateListener` refuses before a socket opens |
| Dashboard fails to build | degrade (default) or `FailHost` if `DashboardRequired` |
| A plugin fails to activate | that plugin is rolled back and skipped; the others load |
| Anything else in `Main` | `LogCritical`, `ExitCode = 1` |

## Sources

- `Vortex.Main/Program.cs`, `VortexEmulator.cs`
- `Vortex.Main/Extensions/HostApplicationBuilderExtensions.cs`
- `Vortex.Main/Configuration/{OrleansHostConfig,OrleansHostConfigValidator}.cs`
- `Vortex.Main/Console/{ConsoleCommandDispatcher,ConsoleCommandService}.cs`
- `Vortex.Plugins/{PluginBootstrapper,PluginManager}.cs`
- `Vortex.Plugins/Extensions/ServiceCollectionExtensions.cs` — `AddHostPlugin<T>`
- `Vortex.Runtime/AssemblyProcessing/AssemblyProcessor.cs`
- `Vortex.Database/Extensions/ServiceCollectionExtensions.cs`
- `Vortex.Networking/NetworkManager.cs`
- `Vortex.Catalog/Providers/{CatalogSnapshotProvider,CatalogClubGiftProvider}.cs` — `LoadStage`
