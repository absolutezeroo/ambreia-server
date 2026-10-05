# Vortex documentation evidence policy

## Evidence classes

### A — implementation truth
Current source code, runtime registrations, project references, tests, migrations, configuration binding, and generated outputs produced by current tools.

Use for claims such as:
- a service is registered,
- a handler calls a grain,
- a grain persists a particular state,
- an endpoint exists,
- a DB index is configured.

### B — repository architectural contract
`AGENTS.md`, `CONTEXT.md`, `CLAUDE.md`, build targets, quality gates.

Use for intended invariants such as:
- handlers remain orchestration-only,
- player outbound routing goes through presence,
- grain-owned state is not mutated directly in DB.

If implementation violates the contract, document **both** rather than rewriting history.

### C — Habbo protocol evidence
Never turn an unknown into a confirmed behavior. Never treat a reference emulator as authority.

### D — historical/orientation evidence
README, audits, roadmap, consolidation documents, old walkthroughs.

Use to locate areas worth inspecting, not as sole proof of current behavior.

## Claim rules

For each material claim, ask:

1. What file/type/method proves this?
2. Is this implementation, intended architecture, or external protocol behavior?
3. Is there contradictory evidence?
4. Does the claim depend on a runtime registration not yet inspected?
5. Does it depend on a generated spec with lower confidence?

If no answer exists, label the claim `Unverified`.

## Source format

Prefer:

```markdown
## Sources
- `Vortex.Rooms/.../RoomGrain.cs` — `RoomGrain.OnActivateAsync`, `EnterAsync`
- `Vortex.Primitives/.../IRoomGrain.cs` — public grain contract
- `Vortex.Rooms.Tests/...` — verifies ...
```

For line numbers, include them only when generated reliably. Symbol names are usually more stable across edits.

## Contradictions

When sources disagree:

```markdown
> **Conflict:** The architectural contract requires X, while current implementation Y performs Z. This page describes current behavior and flags the divergence rather than assuming intent has already been implemented.
```

For protocol conflicts, preserve the confidence terminology used by the Habbo specs.

## Negative claims

Claims like "there is no cache", "this never writes DB", or "only one caller exists" require broader search than positive claims. State the search scope.

## Generated pages

Generated indexes can rely on static scans but must be labeled as inventories. Do not infer runtime semantics from an index alone.
