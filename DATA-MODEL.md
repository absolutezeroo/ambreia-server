# Vortex RPG — Data Model

The schema after the core cut (2026-10-07): accounts, players, zones and their furniture, roles,
configuration and the forensic trail. Every RPG system (characters, combat, items, quests…) adds its
tables on this base, following the conventions below. The schema itself is one `InitialCreate`
migration (`Vortex.Database/Migrations/`), regenerated rather than stacked.

---

## 0. Conventions & naming rule

### 0.1 Base entity (inherited from `VortexEntity`, DO NOT redeclare)

| Column | Type | Role |
|---|---|---|
| `id` | int, PK identity | primary key |
| `created_at` | datetime | created (auto) |
| `updated_at` | datetime | modified (auto, computed) |
| `deleted_at` | datetime? | **soft delete** |

### 0.2 Style (to reproduce)

- `[Table("snake_case_plural")]`, `[Column("snake_case")]`.
- FK: `[Column("x_id")] public required int XEntityId;` + `[ForeignKey(nameof(XEntityId))]
  public required XEntity XEntity;` (nullable nav if FK nullable).
- Composite uniqueness: `[Index(nameof(A), nameof(B), IsUnique = true)]`.
- Required fields: `required`; optional nullable where appropriate. Enums: direct type, `[DefaultValue]` +
  `DatabaseGeneratedOption.None`, located in `Vortex.Primitives.<Domain>.Enums`.
- Soft delete everywhere; never hard delete.
- A persisted enum value is never renumbered: a removed member leaves its number retired.

### 0.3 Prefix rule

**Prefix = owning bounded context. Shared reference tables stay neutral (no prefix).** A new RPG
domain prefixes its tables (`character_*`, `quest_*`…); a table every domain reads stays neutral.

---

## 1. Tables

| Table | Entity | Owner | Notes |
|---|---|---|---|
| `player_accounts` | `PlayerAccountEntity` | web API | e-mail + bcrypt hash; one account, several players |
| `players` | `PlayerEntity` | `PlayerGrain` | name, motto, figure, gender; `__world__` owns the zones |
| `security_tickets` | `SecurityTicketEntity` | authentication | SSO tickets (valid 5 min from issue, sliding on reuse; see `AuthenticationConfig`) |
| `roles`, `role_permissions`, `player_account_roles` | `Role*Entity` | `PermissionService` | capabilities per role; seeded by `PermissionSeederService` |
| `rooms` | `RoomEntity` | `RoomGrain` | a zone; `model_id` → `room_models` |
| `room_models` | `RoomModelEntity` | reference data | heightmaps, imported from `turbo` |
| `room_rights` | `RoomRightEntity` | `RoomGrain` | build rights in a room |
| `room_entry_logs` | `RoomEntryLogEntity` | observability | visits, swept by retention |
| `furniture_definitions` | `FurnitureDefinitionEntity` | reference data | imported from `turbo`; `logic` binds a `[RoomObjectLogic]` |
| `furniture` | `FurnitureEntity` | `RoomGrain` → `RoomPersistenceGrain` | placed items |
| `server_config` | `ServerConfigEntity` | `ServerConfigGrain` | runtime settings (`world.start_room_id`, `motd.lines`) |
| `audit_events`, `item_events` | `AuditEventEntity`, `ItemEventEntity` | observability | written off the hot path by `AuditWriterService` |
| `error_groups`, `error_occurrences` | `Error*Entity` | observability | grouped exceptions |

---

## 2. Anti-error checklist (review before adding a table)

- [ ] Inherits `VortexEntity`; do not redeclare id/created_at/updated_at/deleted_at.
- [ ] “Delete” means `deleted_at`, never hard delete.
- [ ] FK uses `[Column("x_id")] int` + `[ForeignKey]` nav pair, nullable configured correctly.
- [ ] Relational pairs use `[Index(..., IsUnique = true)]` composite constraints.
- [ ] Enums in `Vortex.Primitives.<Domain>.Enums`, `[DefaultValue]` + `DatabaseGeneratedOption.None`.
- [ ] **Naming:** prefix = owning bounded context; shared reference = neutral.
- [ ] Circular relations use non-cascade `OnDelete` (Fluent API).
- [ ] Separate cold/optional/evolving config → one 1:1 table; one-field config is not over-expanded.
- [ ] Instance state ≠ config (type).
- [ ] Grain-owned rows are written by their grain (timer flush, see `RoomPersistenceGrain`), never
  by a script while the emulator runs.
- [ ] A seed the schema needs on every install goes in `Vortex.Database/Seeds/` **and** is applied by
  `InitialCreate` (`SeedCatalogTests` fails otherwise); a one-off fix goes in `scripts/sql/`.
