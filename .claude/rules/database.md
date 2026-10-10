---
paths:
  - "Vortex.Database/**"
  - "scripts/sql/**"
  - "scripts/*.ps1"
---

# Database, schema and data

Conventions for entities (base columns, soft delete, FK pairs, enums, prefixes): `DATA-MODEL.md`.
The full schema-change procedure is the `changing-the-schema` skill.

- One migration only: `InitialCreate`, regenerated after an entity change, never a second migration
  on top. Its last lines apply the seeds (`migrationBuilder.Sql(SeedScripts.Read("….sql"))`) in a
  fixed order; regenerating means putting them back, same order.
- `Vortex.Database/Seeds/*.sql` is embedded and every file must be applied by `InitialCreate`
  (`SeedCatalogTests`). Data that needs imported reference rows (a zone needs `room_models`) is a
  `scripts/sql/` script run by `scripts/import-reference-data.ps1` (`dev_zone.sql`).
- A live database that cannot be recreated follows the regenerated `InitialCreate` by the chain
  `scripts/sql/forward/NNN-<name>.sql`, applied in order and once each by `node scripts/sql/forward.mjs`
  (history in `vortex_forward_history`). A schema change adds the next number; never renumber.
- Seeds and scripts are re-runnable: `NOT EXISTS` guards, `ON DUPLICATE KEY UPDATE`, `WHERE` clauses
  that stop matching once applied. MySQL strict mode refuses an insert that omits a NOT NULL column
  with no default.
- `scripts/ef.ps1` reads the **unprefixed** `Vortex__Database__ConnectionString`; the runtime reads
  `VORTEX__Vortex__Database__ConnectionString`. A `VORTEX__` variable given to `ef` does nothing.
- `turbo` is read only. The fork's database comes from Vortex.Main's user-secrets; the password
  goes through `MYSQL_PWD` and is never printed or placed on a command line.
- Write a grain-owned table (players, rooms, furniture) only through its grain while the emulator
  runs.
