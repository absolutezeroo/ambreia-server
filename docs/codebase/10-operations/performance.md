# Performance and load testing

## Purpose

How the hotel is measured under load, and the one architectural decision that makes the numbers
trustworthy.

## The two-process split

`Vortex.LoadGen`'s `.csproj` carries a one-line comment that explains the whole design:

> **"Deliberately depends on nothing."**

Zero project references. `OutputType Exe`. The only entry is
`<InternalsVisibleTo Include="Vortex.Rooms.Tests" />`.

`Program.cs` records why it moved out of process: a hundred synthetic players meant **~1000
thread-pool wakeups per second inside the process being measured**, so a multi-second stall could not
be attributed to the hotel or to the generator.

```
Vortex.Benchmark (in the emulator)          Vortex.LoadGen (a child process)
  BenchmarkProvisioner ── creates accounts,
      room, furniture (only the emulator
      can — it owns the DB)
  LoadGeneratorHost ──── spawns the exe ──────► reads a LoadPlan as JSON on stdin
                     ◄── one LoadSample ────── writes one JSON line per second to stdout
                         JSON line/sec
  BenchmarkReportWriter ─ logs/benchmark/run-<utc>.json
```

## The synthetic client

`SyntheticClient` speaks the raw wire and **decodes nothing**:

- framing `[int32 length][int16 header][body]`, big-endian
- strings `[uint16 len][UTF-8]`
- `NoDelay = true`
- six hard-coded headers: SSO ticket 882, open flat 3234, move avatar 2364, chat 3034, latency ping
  request 544 / response 188

> **RC4 is the one layer it does not exercise** — the handshake is skipped, and the server treats a
> keyless session as plaintext. Accept, framing, parsers, handlers, grains and per-session fan-out are
> all real.

Per second it reports: `Connected`, `RttMedianMs`, `RttP95Ms` (from latency-ping round trips only),
`Packets`, `Bytes`, `Failures`.

## Vortex.Benchmark

**Not BenchmarkDotNet.** A library (`IHostPluginModule`, key `vortex-benchmark`) hosted inside the
emulator.

`BenchmarkService` runs one load test at a time as a background task, gated by the `ServerConfigGrain`
boolean key **`benchmark.enabled`** (off by default) and a `SemaphoreSlim`. It sweeps residue at
startup via `provisioner.TeardownAsync`.

Phases: `Provisioning → Ramping → Steady → TearingDown → Finished|Failed`.

`LoadGeneratorHost.IsAvailable` refuses up front if either the launcher **or** `Vortex.LoadGen.dll` is
missing — so a run never provisions and then discovers it cannot run.

`Vortex.Main.csproj` references LoadGen with `ReferenceOutputAssembly=false` +
`OutputItemType=Content` — **copied, never loaded**.

The run reads the **live** listener config (`serverOptions:TcpServer:listeners:0`, mapping `0.0.0.0` →
`127.0.0.1`), and teardown always runs in a `finally`, with the report written last so it includes it.

## Reading a report

`logs/benchmark/run-<yyyyMMdd-HHmmss>.json`, schema `vortex.benchmark/1`:

| Section | Contents |
|---|---|
| plan + outcome | what was asked for |
| `verdict` | `BenchmarkVerdict.Evaluate(samples, rooms.Tick.P99Ms)` |
| client-side | peak clients, worst p95 RTT, failures, every sample |
| **server-side** | `RoomPerformanceSnapshot` — tick percentiles, **per-step breakdown ordered by `SumMs`**, directory-call latency |
| process | before/after `ProcessCounters` with per-generation GC deltas |

The server-side half is the point:

> the round trips say **that** the hotel got slower; the room-tick step breakdown says **which step**
> did.

Enums serialize as names — a numeric grade once threw and silently dropped runs from the history list.

## What to look at when it is slow

| Symptom | Look at |
|---|---|
| RTT p95 climbing, tick fine | the packet path, or `MessageSystem`'s per-packet presence-grain call → [Packet pipeline](../02-network-protocol/packet-pipeline.md) |
| tick p99 climbing | the per-step breakdown; `wired` and `avatars` are the usual candidates |
| one step dominating `SumMs` | that system, not the tick |
| directory-call latency | `RoomDirectoryGrain` contention — it is a `[KeepAlive]` singleton |
| memory growth across a run | the GC deltas; then the unbounded caches below |

## Known cost hot spots

Recorded from code reading rather than measurement, in rough order of interest:

1. **One presence-grain round trip per inbound packet.** `MessageSystem.ResolveRoomIdAsync` calls
   `GetActiveRoomAsync` purely for the trace room id — and it runs *before* `RateLimitBehavior` can
   reject the packet. `MessageRegistry` at least reuses the value rather than calling twice
   (`PERF-01`).
2. **`GrainCollectionAge = 2 minutes` against expensive hydration.** `RoomGrain` reads room, rights,
   group members and mutes on activation; `MessengerGrain` fans `IsOnlineAsync` out over an entire
   friend list. Whether 2 minutes was measured is **Unverified**.
3. **Two unbounded caches.** `PlayerDirectoryGrain` is `[KeepAlive]` with two dictionaries and no
   eviction; `PermissionService._playerToAccount` is a `ConcurrentDictionary` that is never
   invalidated (only `_byAccount` has a 60 s TTL). Both grow with the distinct-player count.
4. **`MessengerGrain` activation fan-out** — one grain call per friend, bounded only by
   `messenger.max_friends` (300).

## Deliberate optimisations worth not undoing

| Optimisation | Where |
|---|---|
| Pooled `DbContext` factory — 1085 ms → 56 ms over 20 000 cycles | `AddVortexDatabaseContext` |
| One `UserUpdateMessageComposer` per tick, from a **reused** list | `RoomAvatarTickSystem` |
| Boundary arithmetic instead of a stepping loop | `AlignToNextBoundary` |
| Last-write-wins dirty map, so a sofa dragged ten times is one row | `RoomPersistenceGrain` |
| Metrics read back via `MeterListener` rather than a second write path at 20 Hz/room | `RoomPerformanceAggregator` |
| Pet position writes ride the dirty-flush timer instead of writing per move | `RoomPetSystem` |
| Login's ~15 grain reads batched with `Task.WhenAll` | `SSOTicketMessageHandler` |

That last one is worth knowing: login was once **15 sequential grain reads**, which meant arrivals
froze the hotel while steady state was fine — a shape a naive benchmark misses entirely.

## In-repo micro-benchmarks

`Vortex.Rooms.Tests` also holds two harnesses that are not xunit facts:
`RoomObjectHydrationBenchmark` and `WiredEngineBenchmark`.

## Sources

- `Vortex.LoadGen/{Program,SyntheticClient}.cs`, `Vortex.LoadGen.csproj`
- `Vortex.Benchmark/{BenchmarkService,LoadGeneratorHost,BenchmarkProvisioner,BenchmarkReportWriter,BenchmarkModule}.cs`
- `Vortex.Main/Vortex.Main.csproj` — the `ReferenceOutputAssembly=false` reference
- `Vortex.Observability/Runtime/RoomPerformanceAggregator.cs`
- `Vortex.Messages/MessageSystem.cs`, `Registry/MessageRegistry.cs`
- `Vortex.Database/Extensions/ServiceCollectionExtensions.cs` — the pooling measurement
- `Vortex.Rooms/Grains/Systems/RoomAvatarTickSystem.cs`
- `Vortex.PacketHandlers/Handshake/SSOTicketMessageHandler.cs`
