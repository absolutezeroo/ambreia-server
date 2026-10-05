# Architecture boundaries

## Purpose

What the codebase refuses to let you do, and which mechanism refuses it. Most of these exist because
the thing they prevent already shipped once.

## The six walls

`scripts/hooks/check-architecture-walls.mjs` runs inside `VortexCloudFastCheck`. Its own header says
"Six rules". Each is a text scan over `.cs` files, skipping comment-only lines, ratcheted against
`scripts/hooks/architecture-walls-baseline.json`.

> **`CONTEXT.md` says three walls are enforced. There are six.** The three it omits are Walls 4, 5
> and 6 below. `CONTEXT.md` also says a dashboard capability string goes in "six" files; `AGENTS.md`
> says four, and `AGENTS.md` is the one that matches the hook.

| # | Wall | Detects | Baseline |
|---|---|---|---|
| 1 | No DB in packet handlers | `IDbContextFactory\|VortexDbContext\|SaveChangesAsync` under `Vortex.PacketHandlers` | 0 |
| 2 | No dashboard `SaveChanges` | `\bSaveChangesAsync\b` under `Vortex.Dashboard.API` | 0 |
| 3 | Contracts hub stays protocol-free | `^\s*using\s+Vortex\.Protocol\b` inside `Vortex.Primitives` | `[]` — hard zero |
| 4 | Protocol-free projects stay so | the same import across 37 listed projects | ratcheted |
| 5 | One password-verification site | `BCrypt.Verify` outside `Vortex.Authentication` | 0 |
| 6 | `RoomGrain` partials stay facades | `[RoomObjectLogic]` inside a `RoomGrain` partial; any `RoomGrain*.cs` over 900 lines | ratcheted |

Wall 1 is also enforced twice more: `Vortex.PacketHandlers.csproj` has no `Vortex.Database`
reference at all, and `Vortex.Hosting.Tests/ProjectBoundaryTests.cs` asserts both the missing
reference and the absent `using`. Three mechanisms for one rule, because it is the rule that keeps
539 handlers thin.

Wall 5 currently holds: `Vortex.WebApi/Services/WebApiAuthService.cs` only *hashes*;
`Vortex.Authentication/AccountAuthenticator.cs` is the sole `BCrypt.Verify`. That matters because the
SSO-issuing web login and the dashboard login share `IAccountAuthenticator`, so the second factor
cannot fall behind on one of them.

### What Wall 2 does not catch

The regex is `\bSaveChangesAsync\b` in `Vortex.Dashboard.API` only. It does **not** match:

- `ExecuteUpdateAsync` / `ExecuteDeleteAsync` — the exact APIs `ForensicsPurgeService` uses, so the
  pattern is live in this codebase, just not in that project
- synchronous `SaveChanges()`
- `ExecuteSqlRawAsync` / `FromSqlRaw`
- anything in `Vortex.WebApi` — which *does* call `SaveChangesAsync`
  (`Vortex.WebApi/Services/WebApiPlayerService.cs`) and is not walled

And structurally: **the wall is about the file, not the effect.** An `I<Domain>AdminService` that
writes grain-owned state with no live path satisfies the wall perfectly, because its
`SaveChangesAsync` is in another project. That bug class is invisible to it by construction and is
covered instead on [Dashboard operations](../08-dashboard/operations.md).

## Ownership boundaries

### Handlers orchestrate; they do not own

A handler validates input, calls a grain through a `GrainFactoryExtensions` accessor, and maps the
snapshot to a composer. It does not query the database, hold domain state, or decide domain rules.

`Vortex.PacketHandlers` reaches every domain through interfaces in `Vortex.Primitives` — it has no
project reference to `Vortex.Rooms`, `Vortex.Inventory`, `Vortex.Furniture` or `Vortex.Navigator`.

### Grains own their own state and their own outbound

When grain state changes, the **grain** sends the resulting composer. The caller does not build one
after calling a grain method.

```
correct:  handler → grain.UpdateWalletAsync(…) → grain → PlayerPresenceGrain.SendComposerAsync(…)
wrong:    handler → grain.UpdateWalletAsync(…) → handler builds composer → handler sends it
```

### One clarification the written rule is missing

`AGENTS.md` and `CLAUDE.md` say, without qualification, "do not send composers directly to
sockets/sessions from handlers". Taken literally that is contradicted by the dominant pattern: **306
`ctx.SendComposerAsync` call sites across 222 of 538 handler files**, all going through
`MessageContext.SendComposerAsync` → `ISessionContext`. Only 38 handler files use
`GetPlayerPresenceGrain`.

The rule the code actually follows is narrower and defensible:

| Target | Path |
|---|---|
| **the session that sent this packet** | `ctx.SendComposerAsync` — `MessageContext` is the request's own reply channel, not a raw socket |
| **any other player** | `PlayerPresenceGrain.SendComposerAsync` |
| **everyone in a room** | `RoomGrain.SendComposerToRoomAsync` → Orleans stream → each occupant's presence grain |

> **Divergence:** the contract as written forbids what 222 handler files do. The contract as
> *intended* — requester replies locally, everything else routes through presence — is respected
> essentially everywhere. This needs either a wording fix in `AGENTS.md` or a code change; it is
> recorded here rather than silently resolved in either direction.

→ [Presence routing](../03-orleans/presence-routing.md)

### Grain-owned state is not edited in SQL

A grain may hold live state the DB lags or never sees. Admin tools and external systems call grain
methods; they do not issue raw SQL for data a grain owns. The dashboard honours this — see
[Dashboard operations](../08-dashboard/operations.md) for the classification of every admin write.

### The revision tree

`Vortex.Revisions/Revision20260701/**` is the default revision **embedded in core**, so the emulator
runs standalone without a plugin. Editing it here is correct. Any *additional* revision belongs in
the plugin repo (`../turbo-sample-plugin/TurboSamplePlugin/Revision/**`); do not create a second
`Revision<id>/Parsers` tree in this repository.

→ [Protocol revision plugins](../09-extensibility/protocol-revision-plugins.md)

### Single silo

A second active silo is a **startup error** unless `Vortex:Orleans:MultiSiloReady` is set, because
caches, metrics aggregation and room stream fan-out are silo-local and none of them fails loudly on
a second node — they go quietly stale.

```csharp
// Vortex.Main/VortexEmulator.cs — RefuseAnUndeclaredSecondSiloAsync
if (_orleansConfig.MultiSiloReady) { return; }
var hosts = await _grainFactory.GetGrain<IManagementGrain>(0).GetHosts(onlyActive: true)…;
if (hosts.Count <= 1) { return; }
throw new InvalidOperationException($"{hosts.Count} active silos are in this cluster, and this build's caches, metrics and room streams are silo-local: …");
```

The debt inventory it guards lives in `OrleansHostConfig.MultiSiloReady`'s XML doc and is asserted
against `Vortex.Hosting.Tests/Architecture/single-silo-inventory.yaml` by
`Vortex.Hosting.Tests/Architecture/SingleSiloInventoryTests.cs` (14 providers, 2 aggregators).

## Checks the compiler cannot run

Each fills a gap where the build, the tests and `grep` all pass while the feature is broken.

| Check | Gate | Catches |
|---|---|---|
| `check-dashboard-capabilities.mjs` | FastCheck + PostToolUse | a page hidden from every operator, an undefined route guard, a chunk that 404s, a raw i18n key in the sidebar |
| `check-architecture-walls.mjs` | FastCheck | the six walls above |
| `npm run lint` | QualityGate | an undefined identifier in Svelte markup, which `npm run build` compiles and ships |
| `scripts/hooks/__test/run.mjs` | QualityGate | a hook that stopped blocking — a hook that fails open says nothing |
| `guard-emulator.mjs` | PreToolUse | a command that would kill the running `Vortex.Main` |

> **Two hook scripts are wired into nothing**: `scripts/hooks/check-design-tokens.mjs` and
> `scripts/hooks/check-logic-groups.mjs`. No gate target, no `post-edit.mjs` branch, no
> `.claude/settings.json` entry. They are runnable by hand only.

→ [Build and gates](../10-operations/testing.md)

## Sources

- `scripts/hooks/check-architecture-walls.mjs`, `scripts/hooks/architecture-walls-baseline.json`
- `Vortex.Hosting.Tests/ProjectBoundaryTests.cs`
- `Vortex.Hosting.Tests/Architecture/SingleSiloInventoryTests.cs`
- `Vortex.Main/VortexEmulator.cs` — `RefuseAnUndeclaredSecondSiloAsync`
- `Vortex.Main/Configuration/OrleansHostConfig.cs` — `MultiSiloReady`
- `Vortex.Messages/Registry/MessageContext.cs` — `SendComposerAsync`
- `Vortex.Primitives/Players/Grains/IPlayerPresenceGrain.cs`
- `Directory.Build.targets` — `VortexCloudFastCheck`, `VortexCloudQualityGate`
- `AGENTS.md`, `CONTEXT.md`
