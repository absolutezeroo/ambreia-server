---
paths:
  - "Vortex.*/Grains/**"
  - "Vortex.Primitives/**/Grains/**"
  - "Vortex.Rooms/**"
  - "Vortex.Players/**"
---

# Orleans grain rules

Every rule here shipped as a bug at least once. `.claude/agents/grain-rules-reviewer.md` checks a
diff against them; run it on any change under `Grains/`.

| Don't | Do |
|---|---|
| `catch { }` or a catch that does not log | `catch (Exception ex)` + `_logger.Log…(ex, …)`; every grain that calls another grain or the DB injects `ILogger<T>` |
| `await` N independent grain calls in a `foreach` | `Task.WhenAll` — calls to different grains overlap |
| Call the same grain method inside a loop | hoist it before the loop |
| `ExecuteDeleteAsync` per entity | one `WHERE … IN (…)` |
| A DB write per event inside the grain turn | queue dirty state, flush on a `RegisterGrainTimer` and in `OnDeactivateAsync` (`RoomPersistenceGrain`) |
| `Take(50)`, capacity constants | a parameter fed from configuration (`IOptions<T>` or the server config grain) |
| `ExecuteDelete` + insert that must succeed together | tracked `Remove` + `Add`, one `SaveChangesAsync` |
| `.Ignore()` on a grain task | `.LogAndForget(_logger, …)` |
| An in-memory collection that grows per message | a configured cap |
| Domain logic and heavy persistence in one grain | a secondary grain for the I/O (`RoomGrain` → `RoomPersistenceGrain`) |
| `lock` / `SemaphoreSlim` in a grain | the grain's own single-threaded turn (one grain per player for player-scoped work) |
| A caller building and sending the composer after a grain call | the grain that changed the state sends it (`PlayerPresenceGrain.SendComposerAsync`) |
| A cached authorization flag updated only in the DB | update the live state in the same method and hydrate it on activation (`RoomLiveState.PlayerIdsWithRights` once never was) |
| `[KeepAlive]` on an entity grain | only directories and managers (`PlayerDirectoryGrain`, `RoomDirectoryGrain`) |

Interleaving: `[AlwaysInterleave]`, `[Reentrant]`, `[MayInterleave]` are listed with a justification
in `Vortex.Hosting.Tests/Architecture/interleaving-manifest.yaml` (`InterleavingManifestTests` fails
otherwise). Only two kinds are legal: an immutable read, or a mutation finished synchronously before
the first `await`.

Contracts: a grain interface lives in `Vortex.Primitives/<Domain>/Grains`, is reached through a
`Get…Grain` extension in `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs`, and returns
`[GenerateSerializer, Immutable]` snapshots — never mutable state. `[Id(n)]` numbers are never reused.
