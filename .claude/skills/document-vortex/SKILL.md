---
name: document-vortex
description: Build and maintain evidence-backed technical documentation for the Vortex Cloud Habbo emulator by reconstructing architecture, runtime flows, Orleans ownership, protocol behavior, persistence, dashboard APIs, and gameplay domains from the current repository. Use when asked to document Vortex globally or a specific subsystem.
argument-hint: "[full|update|validate|topic <domain>] [--docs-dir docs/codebase]"
allowed-tools: Read, Grep, Glob, Bash, Task, Write, Edit
---

# Document Vortex Cloud

Generate technical documentation for the **current Vortex Cloud codebase**. The documentation must explain how the system actually works, not merely summarize folders or source files.

## Invocation

```text
/document-vortex full
/document-vortex update
/document-vortex validate
/document-vortex topic rooms
/document-vortex topic protocol
/document-vortex topic dashboard
/document-vortex topic economy
/document-vortex topic wireds
/document-vortex topic orleans
/document-vortex topic database
/document-vortex topic networking
/document-vortex topic moderation
/document-vortex topic plugins
/document-vortex full --docs-dir docs/internal/codebase
```

Default output directory: `docs/codebase/`.

## Repository authority and context order

Before analysis, read these files in order when present:

1. `AGENTS.md` — canonical AI/code contract.
2. `CONTEXT.md` — architecture and placement boundaries.
3. `CLAUDE.md` — Claude adapter and required repo automations.
4. `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`.
5. Relevant examples under `docs/patterns/`.
6. Existing documentation related to the selected subsystem.

Treat these categories differently:

- **Code + runtime registration + tests**: primary evidence for implementation facts.
- **AGENTS.md / CONTEXT.md**: primary evidence for intended architectural constraints.
- **README, audits, ROADMAP, old docs**: orientation and historical evidence only; verify against current code before repeating claims.
- **Reference emulators**: evidence, never authority.
- **Assumptions**: must remain explicit assumptions.

## Non-negotiable evidence policy

Read `references/evidence-policy.md` before writing documentation.

Every material technical claim must be supported by one or more concrete repository references such as:

- source file + type/method,
- registration site,
- project reference,
- test,
- migration/entity mapping,
- configuration binding,
- generated Habbo spec with confidence metadata.

If evidence is incomplete, write `Unverified` or `Unknown`, then explain what was inspected.

Do not infer official Habbo server behavior merely because Vortex implements it.

## Goal

Produce documentation useful to a developer who needs to:

- understand the complete architecture,
- locate ownership boundaries quickly,
- follow a client packet end-to-end,
- understand Orleans grain responsibilities and state ownership,
- understand room lifecycle and room tick behavior,
- understand persistence and EF Core mappings,
- understand catalog/economy/inventory/marketplace flows,
- understand dashboard API operations and their relationship to live grain state,
- understand protocol revisions and specs,
- identify extension/plugin points,
- understand observability, startup, deployment and testing,
- safely modify the project without violating repository invariants.

## Mandatory first phase: inventory

Run:

```bash
python .claude/skills/document-vortex/scripts/inventory.py --root . --output .claude/document-vortex-inventory.json
```

If Python is unavailable, reconstruct the same information using `Glob`, `Grep`, `dotnet sln list`, and project files.

Inventory must cover at least:

- solution projects and project references,
- package references,
- executable/host projects,
- Orleans grain interfaces and implementations,
- persistent state usage,
- packet handlers,
- incoming message contracts,
- outgoing composers/serializers,
- revision parsers/serializers/header registries,
- database contexts/entities/configurations/migrations,
- Web API and Dashboard API endpoints,
- configuration keys,
- tests and benchmarks,
- plugins/extension registration,
- relevant generated protocol specs.

The inventory is an index, not documentation. Researchers must still read implementation code.

## Research architecture

Use specialized subagents in parallel when the requested scope spans multiple domains. Researchers are read-only and return structured findings. The coordinator is responsible for final synthesis and documentation writes.

Recommended agents:

- `vortex-architecture-researcher`
- `vortex-runtime-network-researcher`
- `vortex-protocol-researcher`
- `vortex-orleans-researcher`
- `vortex-rooms-researcher`
- `vortex-gameplay-economy-researcher`
- `vortex-database-researcher`
- `vortex-dashboard-api-researcher`
- `vortex-plugins-observability-researcher`
- `vortex-doc-validator`

Existing repo agents are complementary and may be invoked when relevant:

- `grain-rules-reviewer`

Do not duplicate their responsibilities when validation can reuse them.

## Required researcher response format

Every researcher must return:

```markdown
# Findings

## Scope inspected
- paths/files

## Confirmed architecture
- Claim
  - Evidence: `path/File.cs` — `Type.Member`

## Runtime flows
### Flow: <name>
1. ...
2. ...
Evidence:
- ...

## Ownership / invariants
- ...

## Public contracts
- grain interfaces / message contracts / endpoints / service interfaces

## Configuration
- key -> consumer -> behavior

## Persistence
- state -> owner -> persistence mechanism

## Tests / validation
- relevant tests and what they prove

## Cross-domain dependencies
- domain -> domain -> reason

## Unknowns / ambiguities
- ...

## Suggested documentation pages
- path -> purpose
```

Researchers must not write final documentation files.

## Domain boundaries to preserve

### Hosting / runtime

Distinguish clearly between:

- process startup,
- DI/service registration,
- Orleans silo/client setup,
- SuperSocket/network gateway,
- runtime/session abstractions,
- packet pipeline,
- dashboard/API hosting,
- supervisor/load generator/benchmark utilities.

Do not call every project a "service" if it is only a library.

### Networking vs protocol

Keep these concepts separate:

- network transport/session framing,
- packet decoding/dispatch pipeline,
- typed incoming messages,
- orchestration handlers,
- domain/grain calls,
- outgoing composer contracts,
- revision-specific serialization and header IDs.

A packet flow should be reconstructed from actual registrations, not guessed from folder names.

### Habbo protocol truth

For any protocol page:

1. read the revision parsers/serializers and handlers,
2. distinguish `client_confirmed`, reference-emulator evidence, captures, verified/manual blocks, assumptions, conflicts, and unknowns,
3. never promote a numeric header to global protocol truth because IDs are revision-specific,
4. document the embedded default revision separately from external/custom plugin revisions.


### Orleans ownership

For every important grain, document:

- grain key semantics,
- responsibility,
- mutable live state,
- persistent state and backing store if applicable,
- activation/deactivation behavior,
- timers/reminders,
- cross-grain dependencies,
- outbound composer behavior,
- database access,
- cache coherence expectations,
- concurrency properties from Orleans single-threaded execution,
- callers and public methods.

Do not assume a grain owns data merely because its name matches the entity.

Explicitly verify repository rules such as:

- `PlayerPresenceGrain` outbound routing,
- `RoomDirectoryGrain` room discovery/membership,
- dedicated persistence grains where used,
- no raw DB mutation of grain-owned live state,
- no manual locking inside grains,
- timer-based flush patterns where applicable.

### Rooms and Wireds

Room documentation must reconstruct the actual room lifecycle:

```text
enter request
  -> authorization/discovery
  -> room grain/live state
  -> hydration
  -> unit/session membership
  -> room data/composers
  -> movement/tick/events
  -> furniture/wired/effects
  -> persistence
  -> leave/disconnect/deactivation
```

Only keep steps demonstrated by code.

For Wireds, document:

- trigger/effect/condition model,
- registration/discovery,
- execution context,
- selector/target resolution if present,
- transactions/batches if present,
- recursion/cycle/limit protection,
- room-state mutation boundaries,
- persistence format,
- packet configuration flow,
- serialization back to the client.

Do not flatten distinct Wired abstractions into one generic "engine" unless the implementation does.

### Economy and inventory

Trace money/item ownership changes end-to-end. Cover, where present:

- catalog purchase,
- credits/currencies,
- inventory insertion/removal,
- furniture placement/pickup,
- marketplace,
- collectibles,
- progression rewards,
- limited items,
- transaction boundaries,
- concurrency serialization via grains,
- DB writes and live-state synchronization,
- outgoing wallet/inventory updates.

Pages must identify the authoritative mutation path and not just the UI/handler entrypoint.

### Database

Document:

- DbContexts and responsibility,
- major entity groups,
- entity relationships,
- model configurations,
- important indexes/uniqueness constraints,
- migrations,
- grain-owned vs DB-owned state,
- stores used by Orleans persistent state,
- write patterns and transaction boundaries.

Do not generate a giant property dump as the main database documentation. Prefer bounded-context views, then generated indexes for exhaustive lists.

### Dashboard API

The Dashboard API is not merely CRUD documentation. For each capability, determine whether it:

- queries database read models,
- invokes Orleans grains,
- mutates grain-owned state,
- performs an operation/command,
- relies on live runtime state,
- emits audit events,
- requires a permission/capability,
- has a corresponding dashboard route/UI.

Flag documentation when an admin operation could cause DB/live-state divergence if implemented incorrectly. Do not claim an endpoint is safe merely because it persists successfully.

### Plugins and revisions

Explain:

- plugin discovery/loading,
- extension contracts,
- lifecycle,
- revision registration,
- default embedded revision vs custom revision plugins,
- assembly isolation or loading behavior if implemented,
- configuration and failure handling.

## Required end-to-end flows

A `full` run should reconstruct and document, if implemented:

1. process startup to accepting a client connection,
2. connection -> SSO/authentication -> player presence,
3. incoming packet -> parser -> typed message -> handler -> domain/grain -> composer -> revision serializer -> session,
4. room enter -> hydration -> active membership -> leave,
5. movement/tick update path,
6. furniture placement/pickup,
7. wired execution,
8. catalog purchase -> wallet/inventory mutation,
9. marketplace listing/purchase,
10. friend/social action,
11. dashboard admin mutation -> live system effect,
12. persistence flush/deactivation,
13. plugin/revision load.

If a flow is not implemented or cannot be proven, mark it accordingly.

## Documentation output schema

Read `references/documentation-schema.md`.

Recommended tree:

```text
docs/codebase/
├── README.md
├── 00-overview/
│   ├── system-overview.md
│   ├── solution-map.md
│   ├── architecture-boundaries.md
│   └── glossary.md
├── 01-runtime/
│   ├── startup.md
│   ├── hosting.md
│   ├── configuration.md
│   └── deployment.md
├── 02-network-protocol/
│   ├── networking.md
│   ├── packet-pipeline.md
│   ├── protocol-model.md
│   ├── revisions.md
├── 03-orleans/
│   ├── overview.md
│   ├── grain-map.md
│   ├── presence-routing.md
│   ├── persistence.md
│   └── lifecycle-concurrency.md
├── 04-rooms/
│   ├── room-architecture.md
│   ├── lifecycle.md
│   ├── movement-and-tick.md
│   ├── furniture.md
│   └── wireds.md
├── 05-gameplay/
│   ├── players.md
│   ├── social.md
│   ├── navigator.md
│   ├── progression.md
│   └── collectibles.md
├── 06-economy/
│   ├── overview.md
│   ├── catalog.md
│   ├── inventory.md
│   ├── marketplace.md
│   └── transactions.md
├── 07-database/
│   ├── overview.md
│   ├── ownership-boundaries.md
│   ├── entities-and-relationships.md
│   └── migrations.md
├── 08-dashboard/
│   ├── api-architecture.md
│   ├── authentication-authorization.md
│   ├── capabilities.md
│   ├── read-models.md
│   └── operations.md
├── 09-extensibility/
│   ├── plugins.md
│   └── protocol-revision-plugins.md
├── 10-operations/
│   ├── observability.md
│   ├── logging.md
│   ├── testing.md
│   └── performance.md
├── flows/
│   ├── authentication.md
│   ├── packet-roundtrip.md
│   ├── room-entry.md
│   ├── catalog-purchase.md
│   └── dashboard-live-mutation.md
└── generated/
    ├── project-index.md
    ├── project-dependencies.md
    ├── grain-index.md
    ├── packet-handler-index.md
    ├── revision-index.md
    ├── endpoint-index.md
    ├── entity-index.md
    └── configuration-index.md
```

Adapt the tree when the actual implementation makes a different grouping clearer.

## Page requirements

Each architecture/domain page should normally contain:

1. Purpose
2. Scope
3. Key types/projects
4. Architecture
5. Runtime flow(s)
6. State ownership
7. Persistence
8. Cross-domain dependencies
9. Configuration
10. Failure/concurrency considerations
11. Extension points
12. Tests/validation
13. Known unknowns
14. Sources

Do not force empty sections.

## Mermaid requirements

Use Mermaid only when it increases comprehension. Preferred diagrams:

- project dependency overview,
- request/packet sequence diagrams,
- Orleans ownership map,
- room lifecycle state diagram,
- DB/live-state ownership diagram,
- plugin/revision loading flow.

Every Mermaid node and edge representing implementation behavior must be traceable to evidence.

Avoid unreadable 50-node diagrams. Split by domain.

## Generated indexes

Generated index pages may be exhaustive. They should be clearly labeled as generated/reference material and not substitute for explanatory documentation.

Recommended tables:

### Project index

| Project | Kind | Responsibility | Direct project references | Key packages |

### Grain index

| Interface | Implementation | Key type | State/persistence | Responsibility |

### Handler index

| Incoming message | Handler | Domain call | Main outgoing composer(s) | Revision mapping |

### Endpoint index

| Method | Route | Operation | Authorization | Data/live-state dependencies |

### Entity index

| Entity | Table | Domain | Key relationships | Important constraints |

## `full` mode

1. Load repository contracts.
2. Run inventory.
3. Inspect current docs to avoid duplicate/conflicting pages.
4. Spawn all required research agents in parallel.
5. Synthesize domain boundaries and end-to-end flows.
6. Write generated indexes.
7. Write explanatory documentation.
8. Create or update `docs-dir/.documentation-state.json` containing:
   - documented commit SHA,
   - timestamp,
   - generator version `document-vortex-v1`,
   - output directory,
   - domains generated.
9. Run the validator agent.
10. Fix evidence/consistency issues found by validation.

## `update` mode

1. Read `.documentation-state.json`.
2. Resolve its prior commit SHA and current `HEAD`.
3. Run:

```bash
git diff --name-status <documented-sha>..HEAD
```

4. Map changed files to impacted documentation domains using `references/domain-map.md`.
5. Re-run inventory.
6. Re-run only relevant researchers plus architecture researcher if project references/hosting/contracts changed.
7. Update affected generated indexes.
8. Revalidate links and cross-domain flows.
9. Update state SHA only after successful documentation validation.

If the prior commit no longer exists, fall back to `full` analysis rather than guessing.

## `topic <domain>` mode

Document only the selected domain plus the minimum neighboring systems necessary to explain its flows correctly.

Examples:

- `topic wireds`: rooms, relevant packets, furniture/wired model, persistence, revision serializers.
- `topic dashboard`: Dashboard API, auth/capabilities, read models, grain operations, DB/live ownership.
- `topic protocol`: networking/pipeline/messages/revisions/specs; do not expand all gameplay domains.
- `topic orleans`: grain topology, ownership, persistence, routing, lifecycle, concurrency.

## `validate` mode

Do not rewrite the whole documentation. Validate:

- source references still exist,
- named symbols still exist when practical,
- project names match current solution,
- diagrams do not contradict prose,
- links between pages are valid,
- protocol claims preserve confidence/unknown distinctions,
- grain ownership claims are backed by implementation,
- no docs claim direct handler socket sends when repository contract forbids them,
- no docs encourage raw DB writes for grain-owned state,
- no stale file paths from refactors,
- generated indexes reflect current inventory.

Use `vortex-doc-validator`. For grain/protocol-sensitive docs, also consider the repository's existing `grain-rules-reviewer`.

## Quality bar

Bad documentation:

> `Vortex.Rooms contains room-related classes.`

Good documentation:

> `Vortex.Rooms owns the live room domain. Room membership/discovery is coordinated through the directory grain, while room-specific state and mutations are handled by room grains and supporting services/persistence components. Packet handlers should only orchestrate entry requests and route through canonical grain APIs rather than maintaining an independent room registry.`
>
> Sources: concrete files/types are then listed and verified for the current revision.

The goal is **operational understanding**.

## Anti-patterns

Do not:

- document every class individually as the primary output,
- copy XML comments into Markdown,
- derive architecture only from `.csproj` references,
- equate database schema ownership with live-state ownership,
- treat packet handler behavior as the protocol specification,
- hide contradictory evidence,
- erase known unknowns from Habbo specs,
- invent server behavior from client packet structure,
- describe stale audits as current state without verification,
- modify production code while documenting unless explicitly requested,
- overwrite unrelated existing `.claude` agents or skills.

## Completion report

At completion, report succinctly:

- mode executed,
- commit documented,
- pages generated/updated,
- domains covered,
- unresolved unknowns that materially affect understanding,
- validation status.
