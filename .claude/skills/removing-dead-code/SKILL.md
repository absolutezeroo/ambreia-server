---
name: removing-dead-code
description: Removes a feature or dead code from the Vortex emulator without leaving runtime traps — grain accessors and callers, scanned handlers and logics, DI registrations, packets, entities, tests and baselines — using bundled liveness-sweep scripts. Use when the user asks to delete or cut a feature, system, packet or table, to clean up after a removal, or to find code nothing uses any more.
---

# Removing dead code

The compiler finds a dangling reference; it does not find what is reached only by reflection, by
scan or through a grain proxy. Those are the traps. Copy this checklist and tick it:

```
- [ ] 1. Cut the feature at its roots (handlers, grains, services, registrations)
- [ ] 2. Remove the grain contracts AND their Get…Grain accessors
- [ ] 3. Sweep to a fixed point (scripts)
- [ ] 4. Scanned code: event handlers, logics, handlers, hosted services
- [ ] 5. Database: entities, columns, seeds (changing-the-schema skill)
- [ ] 6. Tests, manifests and baselines
- [ ] 7. Gate green + smoke
```

## 1–2. Roots and grain contracts

Delete the handlers, grains, services and their registrations (`*Module.cs`, `Program.cs`). Then:

- Remove the grain interface **and** its `Get…Grain` method in
  `Vortex.Primitives/Orleans/GrainFactoryExtensions.cs`, and build: every caller becomes a compile
  error. Left alone, the interface compiles and the first call throws at runtime.
- Grep `Vortex.WebApi` for `UseHostService<`/`[FromServices]` of anything you unregistered: the
  plugin's container gets its services from the host, and a missing one fails at start or per request.

## 3. Sweep to a fixed point

```bash
node .claude/skills/removing-dead-code/scripts/sweep.mjs            # report
node .claude/skills/removing-dead-code/scripts/sweep.mjs --apply    # repeat until "deleted 0"
node .claude/skills/removing-dead-code/scripts/fixusings.mjs        # usings of vanished namespaces
dotnet build Vortex.Cloud.sln > build.log                           # then read build.log
```

`sweep.mjs` deletes Protocol/Revisions/Primitives/entity files no live code names (a DbSet name
counts as naming its entity), with their map statements, `Headers.cs` constants and DbSets. It keeps
codec helpers a live serializer uses. What breaks after it is a real dangling use: fix the user, do
not restore the file blindly.

`unnamed.mjs <dir>` lists files whose types nobody names — expected for grains, logics, handlers and
extension classes (scanned or called by method), suspicious for anything else.
`deadmethods.mjs <dirs>` lists public methods nobody calls (noisy: confirm each by hand).

## 4. Scanned code

```bash
node .claude/skills/removing-dead-code/scripts/deadhandlers.mjs Vortex.Observability/Events          # report
node .claude/skills/removing-dead-code/scripts/deadhandlers.mjs Vortex.Observability/Events --apply
```

An `IEventHandler<T>` for an event nobody publishes, a `[RoomObjectLogic]` nobody binds, a hosted
service with nothing to do: all compile and run for nothing.

## 5. Database

Entities the sweep removed, and dead columns on entities that stay: follow the
`changing-the-schema` skill (regenerate `InitialCreate`, drop the seeds of removed tables from
`Vortex.Database/Seeds` and from the seed block, fresh database).

## 6. Tests, manifests, baselines

Delete the tests of the removed behaviour; keep and fix tests of what stays. Then:
`Vortex.Hosting.Tests/Architecture/interleaving-manifest.yaml`, `scripts/hooks/*-baseline.json`
(`node scripts/hooks/check-authorization-surface.mjs --update`,
`node scripts/hooks/check-inert-declarations.mjs --update` after reviewing what moved), and
`scripts/hooks/__test/run.mjs` if a probe names a removed project.

## 7. Verify

```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
node .claude/skills/run-vortex-emulator/driver.mjs smoke
```

A removal is only finished when the running emulator still logs a player into the zone.
