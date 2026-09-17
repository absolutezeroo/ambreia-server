# Migrations and seeding

## Purpose

How the schema evolves, how to author a migration without a live database, and one orphan file that
looks alarming and is not.

## The set

**131 migration files** in `Vortex.Database/Migrations/`, spanning `20260205185839` →
`20260826025721`, plus 129 `.Designer.cs`, `VortexDbContextModelSnapshot.cs`, and the non-migration
`MigrationHelper.cs`.

Naming: `<yyyyMMddHHmmss>_<PascalCase>`. Prefix distribution: `Add` 104, `Seed` 6, `Fix` 4, `Reseed` 2,
and one each of `Widen`, `Split`, `Rename`, `Remove`, `Refactor`, `Rebuild`, `Persist`, `Initial`,
`Drop`, `Check`, `Bind`, `Allow`, `Added`.

The snapshot is current: the newest migration adds `furniture_definitions.vending_ids varchar(512)`,
and `VortexDbContextModelSnapshot.cs` contains `.HasColumnName("vending_ids")`.

## Migrations are not applied at startup — unless asked

> `README.md`: *"Migrations are **not** applied automatically when the host starts; when to migrate is
> an operations decision."*

That is still the default. `MigrationHelper.ApplyStartupMigrationsAsync` makes it opt-in for a
single node: `Program.cs` calls it **before `host.StartAsync`**, and it returns immediately unless
`Vortex:Database:MigrateOnStartup` is true. When it is, it logs the pending ids, applies them, and
lets a failure propagate into the host's `catch` — which exits non-zero, so the container runtime
restarts or alerts instead of serving a schema the code does not match.

It resolves `IDbContextFactory<VortexDbContext>`, not `VortexDbContext`: the context is registered
through `AddPooledDbContextFactory`, so `GetRequiredService<VortexDbContext>()` throws — which is
why `MigrateAsync<TContext>` below cannot be reused for it.

`MigrationHelper.MigrateAsync<TContext>` / `UninstallAsync<TContext>` remain plugin-only.

History table is the EF default `__EFMigrationsHistory`; plugins get
`__EFMigrationsHistory_<prefix>`.

## The offline recipe

`ServerVersion.AutoDetect` opens a real connection, and the design-time factory exists to avoid that.

`VortexDbContextFactory.CreateDbContext` builds **its own** configuration root:
`Directory.GetCurrentDirectory()/..` + `appsettings.json` + `appsettings.Development.json` + plain
`AddEnvironmentVariables()` — *unprefixed*, unlike the runtime's `VORTEX__`. It pins the server version
from **`Vortex:Database:ServerVersion`**, which is a different key from the runtime's
`Vortex:Database:MySqlServerVersion`. → [Overview](overview.md)

### Hand-authoring a migration

`20260614200000_AddObservabilityErrorTracking.cs` is the worked example: a hand-written offline
migration carrying its attributes inline instead of a generated `.Designer.cs`:

```csharp
[DbContext(typeof(VortexDbContext))]
[Migration("20260614200000_AddObservabilityErrorTracking")]
public partial class AddObservabilityErrorTracking : Migration { … }
```

EF applies it normally. **The `[Migration]` attribute is what makes it discoverable** — see the orphan
below.

`README.md` documents both the throwaway-SDK-container and the host-local invocations.

## The one orphan

> **`20260205185839_AddedModels.cs` has no `[Migration]` attribute and no `.Designer.cs`.**
>
> `MigrationsAssembly` never discovers it — it is dead code. Its `Up()` drops `rooms.allow_editing`,
> `furniture_definitions.product_name` / `public_name`, `furniture.stuff_data`, and renames
> `users_max` → `players_max`. All of that is already reflected in `20260208101510_InitialCreate`
> (grep for those four names there returns 0; `players_max` is created directly).
>
> Its id also sorts **before** `InitialCreate`, so if it were ever given an attribute it would fail on
> a fresh database.
>
> Leftover from a schema squash. Safe today, confusing forever.

So **130 migrations are discoverable**, not 131.

## Schema evolution, in waves

Reading the sequence, the schema grew by bounded context:

| Period | Theme |
|---|---|
| **Feb 2026** | foundation — `InitialCreate`: rooms, players, furniture, catalog |
| **Jun 2026** | hotel surface — navigator prefs and quick links; subscriptions and club (gifts, kickback, tiers, discounts); marketplace; messenger blocking and messages; observability (audit → ledger/item events → error tracking); accounts and permissions; groups; rentable space (Epic 4); then a long pets arc — `AddPets` → palettes → commands → levels → breeding → food → nest logic → monsterplant |
| **Jul 2026** | moderation, wired, progression — vouchers; account bans and trading lock; sanction presets; CFH tickets; builders club; room advertisements; wired permanent variables / room logs / room settings; achievements, quests, daily quests, targeted offers; account, wardrobe and effect preferences; `server_config` |
| **Aug 2026** | content depth — mystery box; prize pools + bindings + claims; bots + skills; hand items; NFT collections → store offers → claims → minting → provenance/editions; polls; community goals; daily tasks; account levels; achievement resolutions; help quizzes; purchasable clothing; NFT avatar wardrobe; wired chests → settings → transactions → contracts; TOTP; and finally the **commerce journal** (`AddCommerceOperationJournal`, `AddCommerceRelayColumns`) — the durability layer for value-moving flows |

Cross-cutting repairs get their own migrations: `FixDeletedAtColumn`, `FixPetFoodCompositeIndex`,
`FixGuildRoomUniqueness`, `FixCrackableAndHighscoreFamilies`, `RenameLtdTablesToCatalogPrefix`,
`RoomDecorationIdsAsStrings`, `WidenPlayerFigure`, `AllowCfhTicketWithoutReportedPlayer`,
`RemovePerformanceLogs`, `DropMysteryBoxDefinitions`.

## Seeding

Three mechanisms, all idempotent.

### 1 · Embedded SQL scripts

**23 files** under `Vortex.Database/Seeds/*.sql`, embedded via
`<EmbeddedResource Include="Seeds\*.sql" />`, read by `SeedScripts.Read(...)` and applied with
`migrationBuilder.Sql(...)`.

**All use `INSERT IGNORE`**, so re-running against a partially seeded database is safe.

Content is reference data: pet commands / levels / food / palettes / vocals, group colours and badge
parts, navigator configuration, hand items, quizzes, polls, account levels, achievement resolutions,
purchasable clothing, mystery box, collectibles currencies, crackable and highscore families, furni
logic bindings and stuff-data types, catalog pet products.

### 2 · Inline `InsertData`

Eight migrations: `SeedDashboardStaffActor`, `AddMarketplaceSettings`, `AddAchievements`,
`SeedMoreAchievementsFixCategories`, `AddQuests`, `AddDailyQuests`, `AddTargetedOffers`,
`AddServerConfig`.

> `SeedDashboardStaffActor` matters operationally: it creates the reserved `__dashboard_staff__`
> player. Without it **every room-scoped dashboard operation fails** with
> `dashboard_staff_actor_missing`. → [Dashboard operations](../08-dashboard/operations.md)

### 3 · Runtime seeders

Idempotent, additive-only, failure logged not fatal: `PermissionSeederService`,
`SanctionPresetSeederService`, `CfhCatalogSeederService`, `BuildersClubTierSeederService`.

## Plugin migrations

`MigrationHelper.MigrateAsync<TContext>` / `UninstallAsync<TContext>`, called only from
`PluginManager` via `IPluginDbModule`.

`UninstallAsync` is **the one raw-SQL site in production code**. It refuses an empty prefix — which
would drop the whole schema — and sanitises `\ ' % _` before the `LIKE`.

> Migrations are deliberately **not** compensated on a failed plugin activation. `PluginManager`'s
> comment: running `UninstallAsync` because a hosted service failed would turn a recoverable startup
> error into data loss. → [Plugins](../09-extensibility/plugins.md)

## Testing against the schema

`Vortex.Database.Tests` (34 files) uses **two providers deliberately**.

### EF InMemory — 14 files

For model-metadata assertions and simple round-trips. E.g.
`WiredPersistenceInvariantsTests.PermanentVariable_UniqueIndex_CoversTargetTypeTargetIdVariableId`
reads `ctx.Model.FindEntityType(...).GetIndexes()`.

> **Limitation to keep in mind:** these prove the *model declares* the constraint, not that anything
> enforces it. EF InMemory has no unique-index enforcement and **no `ExecuteUpdate` / `ExecuteDelete`
> at all**.

### SQLite in-memory — 5 files

Exactly where that limitation bites. `MarketplaceClaimRaceTests` says so in its own header:

> *"On SQLite rather than the in-memory provider, because the in-memory provider does not implement
> ExecuteUpdate at all — it would throw, and a test that cannot run the guard cannot vouch for it."*

Also `MarketplaceWindowTests`, `CommerceJournalTests`, `CommerceRelayTests`,
`ForensicsRetentionServiceTests`.

> **A second SQLite gotcha**, documented in the fixtures: `created_at` is
> `DatabaseGenerated(Identity)` and `updated_at` is `Computed`, so EF never writes either. MySQL fills
> them from migration-declared column defaults, but `EnsureCreated()` on SQLite makes the columns
> `NOT NULL` with **no default** — so any EF insert of a `VortexEntity` fails on SQLite. The fixtures
> seed rows in raw SQL for that reason. Updates are unaffected.

## Known unknowns

- **Unknown:** whether EF's applied list really is 130 and excludes `AddedModels`.
  - Inspected: the file has no `[Migration]` attribute and no Designer; EF discovery requires the
    attribute; `InitialCreate` already reflects its `Up()`.
  - Why unresolved: `dotnet ef migrations list` was not run. High confidence, one command short.
- **Unknown:** whether any deployed database has `AddedModels` in its `__EFMigrationsHistory`.
  - If an old database was migrated when the file *did* have an attribute, its history row is now an
    orphan id EF reports as "applied but not found".
  - What would resolve it: `SELECT MigrationId FROM __EFMigrationsHistory` on a long-lived database —
    worth checking **before** anyone tidies the file away.

## Sources

- `Vortex.Database/Migrations/**` — the 131 files, `VortexDbContextModelSnapshot.cs`
- `Vortex.Database/Migrations/MigrationHelper.cs`
- `Vortex.Database/Context/DesignTimeVortexDbContext.cs`
- `Vortex.Database/Seeds/*.sql`, `Vortex.Database/Seeds/SeedScripts.cs`
- `Vortex.Database/Vortex.Database.csproj` — the `EmbeddedResource` glob
- `Vortex.Database.Tests/Marketplace/MarketplaceClaimRaceTests.cs`
- `README.md` — the migration invocations
