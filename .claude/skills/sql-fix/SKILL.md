---
name: sql-fix
description: Fix wrong or missing rows with a reviewable SQL script in scripts/sql/ instead of building a seeder. Use for one-off data corrections.
disable-model-invocation: true
---

# Data fix

## The decision, first

| Symptom | Fix |
|---|---|
| Rows are wrong **once** — a misbound furniture logic, a zone on the wrong model, a bad backfill | A script in `scripts/sql/`, run once. **Default.** |
| The data must be correct on **every boot** — a fresh install must come up with it | Then, and only then, a seed applied by `InitialCreate` (`Vortex.Database/Seeds/`) |

A seeder for a one-off hole is overkill: it adds startup work, a code path to maintain, and a
migration story, to do something a reviewable `.sql` file does once and then stops doing. When in
doubt, write the SQL.

## Recipe

1. **Measure first.** A `SELECT` that counts exactly the rows the fix will touch. Paste the count
   into the conversation before writing any `UPDATE`/`INSERT`. If the count surprises you, the
   diagnosis is wrong, not the query.
2. **Write it to `scripts/sql/<subject>.sql`.** `dev_zone.sql` is the format reference: a header
   comment states what it does, what it deliberately does not touch, and that it is re-runnable —
   that is what makes a data script reviewable.
3. **Make it idempotent.** Guard inserts with `NOT EXISTS`, scope updates with a `WHERE` that stops
   matching once applied. Running it twice must be a no-op, because it will be run twice.
4. **Hand it to the user to run.** Do not execute it yourself unless asked. Give the command and the
   expected row count.

```bash
# The fork's own database (Vortex.Main user-secrets). `turbo` is READ ONLY: never a target.
# The password goes through MYSQL_PWD; never print it or put it on the command line.
/c/laragon/bin/mysql/mysql-8.4.3-winx64/bin/mysql.exe -h127.0.0.1 -uroot <fork_db> < scripts/sql/<subject>.sql
```

## Traps

- **Grain-owned rows are not yours to edit while the emulator runs.** A grain holds them in memory
  and writes them back on deactivation, over your change (`AGENTS.md`, "Do not mutate the database
  directly for grain-owned state"). Players, rooms and placed furniture: stop first, or go through
  the grain.
- Take a `mysqldump` before any destructive script.
- `furniture_definitions.name` is **not unique** (3533 duplicates, by design — the client's own
  furnidata ships duplicates). Never key a join or a `ToDictionary` on it.
