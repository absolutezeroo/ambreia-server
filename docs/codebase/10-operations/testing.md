# Testing and the checks the compiler cannot run

## Purpose

What the test suite covers, what it **structurally cannot** cover, and which non-compiler checks fill
each gap.

## The suite

**14 test projects**, all xunit + FluentAssertions + `Microsoft.NET.Test.Sdk`, ~1958
`[Fact]`/`[Theory]`.

| Project | ~facts | Focus |
|---|---:|---|
| `Vortex.Rooms.Tests` | 801 | avatars, Banzai, bots, chat, events, Freeze, furniture, games, grains, groups, moderation, mystery box, observability, permissions, pets, prizes, trading, wallet, wired. The **only** project with `Microsoft.Orleans.TestingHost` |
| `Vortex.Players.Tests` | 258 | players, progression, social, collectibles |
| `Vortex.Revisions.Tests` | 243 | wire-layout pins |
| `Vortex.Database.Tests` | 178 | EF, InMemory **and** SQLite |
| `Vortex.Hosting.Tests` | 99 | config validation, listener security, Orleans endpoints, console dispatcher, boundaries, and an `Architecture/` suite |
| `Vortex.Dashboard.Tests` | 47 | dashboard API via `TestServer` |
| `Vortex.Authentication.Tests` | 43 | tickets, MFA, permissions |
| `Vortex.Crypto.Tests` | 39 | |
| `Vortex.WebApi.Tests` | 31 | includes the 7-case metrics gating suite |
| `Vortex.Navigator.Tests` | 25 | |
| `Vortex.Plugins.Tests` | 22 | real activation against a real byte-loaded plugin |
| `Vortex.Pipeline.Tests` | 13 | the envelope host |

Two **support** projects are not test projects: `Vortex.Tests.Support` (the grain harness) and
`Vortex.Plugins.TestPlugin` (a fixture plugin).

## Testing grains

Two harnesses for two different questions.

### A real cluster

`Vortex.Rooms.Tests/Grains/VortexClusterFixture.cs` — an in-process `TestCluster(1)` with an in-memory
EF store, shared by `VortexClusterCollection`. Its doc is explicit that it exists to test what a
hand-constructed grain cannot: **one activation per key, turn-based execution, and by-value argument
serialization**.

`GrainTurnIsolationTests` fires 250 concurrent `AddPlayerToRoomAsync` calls at a real
`RoomDirectoryGrain` and asserts the exact population — its header notes the no-locks assumption was
*"load-bearing and, until now, asserted nowhere"*.

### Outside a silo

`Vortex.Tests.Support/GrainActivationContext.cs` builds a grain with `new`.

It reflects over `RuntimeContext.SetExecutionContext` / `ResetExecutionContext` and sets the ambient
activation context **around the constructor call** — because `RoomGrain` reads its primary key *in its
own constructor*, so patching the instance afterwards is too late. Both reflection lookups throw a
named error if Orleans moves them.

The context is a `FakeProxy` (a `DispatchProxy`) answering `get_GrainId` with a real `GrainId`,
`get_ActivationServices` with a stub provider, and **any interface-returning member with a recursive
stub** — so arming a timer succeeds and does nothing. (Orleans goes registry → timer and then touches
the timer it got back, so returning null is not enough.)

`Vortex.Rooms.Tests/Support/RoomHarness.cs` builds on it: a real `RoomGrain`, a recording stream
proxy in place of `_roomOutbound`, an `EventRecorder` listener, and a simulated clock started at
`Grain.NowMs()` — the comment notes a test ticking from zero would be permanently in the room's past.

## Testing against the database

Two providers, **deliberately**.

**EF InMemory** (14 files) for model-metadata assertions and simple round-trips.

> **Limitation:** these prove the model *declares* a constraint, not that anything enforces it. EF
> InMemory has no unique-index enforcement and **no `ExecuteUpdate`/`ExecuteDelete` at all**.

**SQLite in-memory** (5 files) exactly where that bites. `MarketplaceClaimRaceTests` says so:

> *"On SQLite rather than the in-memory provider, because the in-memory provider does not implement
> ExecuteUpdate at all — it would throw, and a test that cannot run the guard cannot vouch for it."*

A second SQLite gotcha is documented in the fixtures → [Migrations](../07-database/migrations.md).

## Architecture as tests

`Vortex.Hosting.Tests/Architecture/`:

| Test | Asserts |
|---|---|
| `SingleSiloInventoryTests` | every `IReferenceDataProvider` and `*Aggregator` is listed in `Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml` — **this is what keeps the silo-local debt from growing unnoticed** |
| `InterleavingManifestTests` | the interleaving manifest |
| `RoomGrainConcurrencyTests`, `ConfiguredBudgetTests` | |

Plus `ProjectBoundaryTests`, `ConfigValidationTests`, `ListenerSecurityTests`, `OrleansEndpointTests`,
`RequiredServiceGuardTests`.

## What the suite cannot catch

This is the important half. `Directory.Build.targets`' comment explains why these moved into the gate:
they *"lived only in the PostToolUse hook, which means they fired for exactly one tool and for exactly
the files it happened to edit."*

| Bug class | Invisible to | Filled by | Runs in |
|---|---|---|---|
| Dashboard capability/route/locale parity — a page hidden from every operator, a chunk that 404s, a raw i18n key | build, `dotnet test`, `npm run build` | `check-dashboard-capabilities.mjs` | FastCheck + PostToolUse |
| Architecture boundaries that were prose | the compiler | `check-architecture-walls.mjs` (6 walls) | FastCheck |
| An undefined identifier in Svelte markup — compiles to a global lookup, renders blank | `npm run build` | `eslint` / `npm run lint` | QualityGate + PostToolUse |
| **A hook that silently stops blocking** | everything | `scripts/hooks/__test/run.mjs` | QualityGate |
| Killing the live emulator | n/a | `guard-emulator.mjs` | PreToolUse |

### The hook self-test

`scripts/hooks/__test/run.mjs` does not mock. It writes **real probe fixtures** — an
undefined-identifier `__HookProbe.svelte`, a protocol-importing `__WallProbe.cs` in
`Vortex.Primitives/Rooms/`, and a temporarily added `ProjectReference` in `Vortex.Marketplace.csproj`
— asserts 0/2 exit codes across 12 payload cases plus 4 direct invocations, then restores everything.

It exists because **a hook that fails open says nothing**.

### Two hooks are wired into nothing

`scripts/hooks/check-design-tokens.mjs` and `scripts/hooks/check-logic-groups.mjs` have no gate
target, no `post-edit.mjs` branch and no `.claude/settings.json` entry. Runnable by hand only.

## The gates

`Directory.Build.targets`, conditioned on `MSBuildProjectName == 'Vortex.Main'`.

### `VortexCloudFastCheck`

```
1  dotnet tool restore                          (csharpier 1.2.6)
2  dotnet restore Vortex.Cloud.sln              solution-wide — every later step is --no-restore
                                                and the test projects are not in Vortex.Main's graph
3  dotnet csharpier check .
4  dotnet build Vortex.Main/Vortex.Main.csproj  triggers BuildDashboardFrontend (npm ci + vite)
5  dotnet test Vortex.Cloud.sln
6  node scripts/hooks/check-dashboard-capabilities.mjs
8  node scripts/hooks/check-architecture-walls.mjs
```

### `VortexCloudQualityGate`

`DependsOnTargets="VortexCloudFastCheck;VortexCloudAiGovernanceCheck"`, then:

```
1  npm run lint            (Vortex.Dashboard.Web)
2  node scripts/hooks/__test/run.mjs
4  dotnet format style     --verify-no-changes --exclude-diagnostics IDE1006
5  dotnet format analyzers --verify-no-changes --exclude-diagnostics IDE1006
```

`VortexCloudAiGovernanceCheck` only asserts six files exist: `AGENTS.md`, `CONTEXT.md`,
`.github/copilot-instructions.md`, and the three `docs/patterns/*.cs`.

`IDE1006` is excluded because ~1220 pre-existing naming violations are tracked as debt.

### Git hooks and CI

`.githooks/pre-commit` = FastCheck. `pre-push` = the gate.

`.github/workflows/quality.yml` runs the gate on **ubuntu, windows and macos** with
`VortexAIPolicyPhase=2`, plus a `dotnet list package --vulnerable` scan whose output it **greps**,
because that command exits 0 regardless.

Node 22 + `npm ci` is a hard prerequisite — the SPA is generated, not committed.

## Analyzer policy

`Directory.Build.props` promotes `CS8602`, `CS8604`, `CS8618`, `CA2000`, `CA2012`, `CA2201` to errors
at phase 2, plus `ORLEANS0012` and `ORLEANS0013` always.

`IDE0005` and `IDE0058` are **explicitly excluded** with a documented reason — IDE0058 fires 8574
times, overwhelmingly on fluent APIs.

> `.github/workflows/quality.yml`'s comment claims phase 2 promotes `IDE0005` and `IDE0058` to errors.
> `Directory.Build.props` excludes both. The CI comment is stale.

## Cross-process wires

Two places have no compiler between the halves, and both have a test:

- **LoadGen ↔ Benchmark** — the `LoadSample` JSON line (`Vortex.LoadGen/Program.Wire` vs
  `LoadGeneratorHost.Parse`)

## Sources

- the 14 `*.Tests/*.csproj`
- `Vortex.Tests.Support/{GrainActivationContext,FakeProxy}.cs`
- `Vortex.Rooms.Tests/Grains/{VortexClusterFixture,GrainTurnIsolationTests}.cs`, `Support/RoomHarness.cs`
- `Vortex.Hosting.Tests/Architecture/**`, `ProjectBoundaryTests.cs`
- `Vortex.Database.Tests/Marketplace/MarketplaceClaimRaceTests.cs`
- `Directory.Build.targets`, `Directory.Build.props`
- `.github/workflows/quality.yml`, `.githooks/pre-commit`
- `scripts/hooks/**`, `scripts/hooks/__test/run.mjs`
- `AGENTS.md` — "Required validation before completion"
