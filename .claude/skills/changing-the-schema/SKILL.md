---
name: changing-the-schema
description: Changes the Vortex emulator's database schema — adding, editing or removing an EF Core entity, column, index or seed — by regenerating the single InitialCreate migration, keeping its seed block, and proving it on a fresh database with the reference-data import. Use when the user asks for a new table, column, relation or seed, or when an entity or VortexDbContext changes.
---

# Changing the schema

The schema is **one** migration, `InitialCreate`, regenerated on every change — never a second
migration on top. Conventions for entities: `DATA-MODEL.md`. Copy this checklist and tick it:

```
- [ ] 1. Entity + DbSet (+ Fluent config only if attributes cannot say it)
- [ ] 2. Seeds: new file in Vortex.Database/Seeds and its line in the seed block, if every install needs it
- [ ] 3. Regenerate InitialCreate (script)
- [ ] 4. Fresh database: migrate + import (script)
- [ ] 5. Tests and gate
```

## 1. Entity

`Vortex.Database/Entities/<Domain>/<Name>Entity.cs`, inheriting `VortexEntity` (never redeclare `id`,
timestamps, `deleted_at`). Add `public DbSet<XEntity> Xs { get; init; } = null!;` to
`Vortex.Database/Context/VortexDbContext.cs`. Removing a column or entity: also remove every use
(`grep` the property name across `Vortex.*`), including navigation properties on other entities.

A grain owns the table at runtime? The grain hydrates it on activation and writes it back (timer
flush); nothing else writes it while the emulator runs.

## 2. Seeds

| The data is… | Goes in |
|---|---|
| needed by every install, independent of imported rows | `Vortex.Database/Seeds/<name>.sql` + a `SeedScripts.Read("<name>.sql")` line at the end of `InitialCreate.Up` |
| dependent on `room_models`/`furniture_definitions` (imported from turbo) | `scripts/sql/<name>.sql`, run by `scripts/import-reference-data.ps1` (see `dev_zone.sql`) |
| a one-off correction | the `/sql-fix` skill |

Seeds are re-runnable (`NOT EXISTS`, `ON DUPLICATE KEY UPDATE`) and name every NOT NULL column that
has no default (MySQL strict mode). `SeedCatalogTests` fails if an embedded seed is not applied.

## 3. Regenerate

```bash
node .claude/skills/changing-the-schema/scripts/regenerate-initialcreate.mjs
```

It keeps the seed block (same order), deletes the migration, Designer and snapshot, runs
`scripts/ef.ps1 migrations add InitialCreate`, puts the block back and formats. A new seed line is
added to the block **after** this, by hand, in the order it must run. Read the generated `Up()`:
the tables and columns should be exactly the ones you meant.

## 4. Fresh database

```bash
pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_schema_check
```

Drop/create, `InitialCreate`, reference import from `turbo` (read only), development zone. It reads
the password from user-secrets into `MYSQL_PWD` and never prints it. The owner's database is not
touched; to run the emulator on the new one, see the `run-vortex-emulator` skill.

## 5. Verify

```bash
dotnet test Vortex.Database.Tests
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```

A change the game reads (a column a grain hydrates) also gets a `driver.mjs smoke` against the fresh
database.
