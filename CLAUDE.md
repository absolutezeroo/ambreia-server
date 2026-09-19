# Claude Adapter (Vortex Cloud)

This adapter points Claude to the canonical AI contract for this repository.

## Required context load order
1. `AGENTS.md`
2. `CONTEXT.md`
3. One relevant sample in `docs/patterns/`
4. `.github/copilot-instructions.md` (tool adapter parity rules)

## Non-negotiable constraints
- **Any change to Habbo protocol behaviour starts at the specs**, not at the code:
  `dotnet run --project Vortex.Specs.Cli -- analyze <feature-id|PacketName>`. Existing emulator
  behaviour is evidence, not authority; reference emulator behaviour is evidence, not authority;
  unknown official behaviour must stay explicitly unknown. Full contract in AGENTS.md
  ("Habbo protocol behaviour: consult the specs first").
- Keep packet handlers orchestration-only.
- Do not query database contexts/repositories from packet handlers.
- Do not send composers directly to sockets/sessions from handlers; route via `PlayerPresenceGrain.SendComposerAsync`.
- For `Revision<id>` parser/serializer work, edit `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`, not `vortex-cloud`.
- A new dashboard capability must be added to **all four** files listed in `AGENTS.md`
  ("Add dashboard capability or admin page"). The build and the tests do not catch a missing copy;
  `scripts/hooks/check-dashboard-capabilities.mjs` does, and runs automatically after any edit to
  one of those files.

## Repository automations
Configured in `.claude/settings.json`; the scripts are plain node and run standalone too. Every
check below also runs inside the gate (`AGENTS.md`, "Required validation before completion") — a
hook fires for one tool and for the files it happens to touch, which is not the same as coverage.

| Automation | Fires on | Catches |
|---|---|---|
| `scripts/hooks/post-edit.mjs` (PostToolUse) | any `Edit`/`Write` | dashboard capability/route/locale parity; header ids the client's registry does not contain; `eslint` on touched `.svelte`/front-end `.js` (`npm run build` misses an undefined identifier in markup); `csharpier` on the touched `.cs` — it **reformats** the file, so re-read it before the next `Edit` |
| `scripts/hooks/guard-emulator.mjs` (PreToolUse) | `Bash`/`PowerShell` | a command that would kill the running `Vortex.Main` |
| `scripts/hooks/guard-commit.mjs` (PreToolUse) | `Bash`/`PowerShell` | a `git commit` that names nothing (no `-o`/`--only`/`-- <paths>`) or a `git add` that sweeps the tree — the index is shared with the user and with any other session |
| `scripts/hooks/check-header-registry.mjs` | FastCheck, hook | a mapped header id absent from the client's message registry — it registers cleanly and can never fire; 14 known, baselined |
| `scripts/hooks/check-logic-groups.mjs` | FastCheck | a `[RoomObjectLogic("...")]` key missing from the admin's furniture logic dropdown — the behaviour exists but nobody can pick it; `--write` regenerates |
| `scripts/hooks/check-variable-capabilities.mjs` | FastCheck | a wired variable declaring a `WiredVariableFlags` capability nothing implements — every picker in the wired editor filters on one flag, so the box is offered, picked, saved, and silent. Catches a named-but-unimplemented capability only: a flag copied from another variable at runtime (the echo box) and a flag that is true over a missing store (the room, context and shared variables) are both invisible to it |
| `scripts/hooks/check-webapi-forwarding.mjs` | FastCheck | a `[FromServices]` type absent from `WebApiWebHost.ForwardSingletons` or from `WebApiTestFactory`. The web API builds its **own** container inside the Orleans host; an unannotated parameter that is missing is read as the request body and kills the route table at startup (loud), but a `[FromServices]` one builds fine and answers **500 per request, forever** — and no test can reach it either. Three occurrences: the chain, the audit sink, a room photo |
| `scripts/hooks/check-wire-conflicts.mjs` | QualityGate | two ratchets, one baseline each. (a) a NEW field-count disagreement with the official client; the known ones are baselined. (b) a NEW packet where our parser reads **fewer** values than the client hands its composer — the case where the client scanner failed to resolve the composer body, so (a) is structurally blind to it and the call-site arity in the spec evidence is the only witness. Needs the client sources beside the repo, and says so instead of passing quietly |
| `.claude/agents/wire-truth-auditor.md` | on request | serializer-vs-AS3-client drift, fabricated header ids |
| `.claude/agents/grain-rules-reviewer.md` | on request | the Orleans rules in `AGENTS.md` that no analyzer enforces |
| `/new-dashboard-page`, `/sql-fix`, `/habbo-spec` | user-invoked | the checklists that get half-remembered |
| `dashboard-render` skill | after any visual dashboard edit | shipping a redesign without once looking at it — the CSS an operator sees is embedded in the assembly, so a green build proves nothing |
| `mysql` MCP (`.mcp.json`) | on request | read-only queries against the `turbo` DB; writes stay in reviewed `scripts/sql/` scripts |
| `Vortex.Specs.Cli` | on request | what is actually known about a packet or feature, and what is only assumed |

`node scripts/hooks/__test/run.mjs` asserts the hooks still behave (exit 0 allow / exit 2 block); it
runs in the QualityGate too, because a hook that stops blocking fails open and says nothing.

## Habbo Specs
`docs/habbo-specs/` is generated from evidence — the official client, the reference emulators, and
any captures present — never hand-authored. It answers "what do we actually know about this packet"
and, just as importantly, "what do we not". Read `docs/habbo-specs/README.md` before relying on it,
and `docs/walkthroughs/use-the-habbo-specs.md` before adding to it.

```bash
dotnet run --project Vortex.Specs.Cli -- analyze room.move_floor_item_in_room
dotnet run --project Vortex.Specs.Cli -- conflicts --kind field_count --limit 200
dotnet run --project Vortex.Specs.Cli -- unknowns --severity critical
dotnet run --project Vortex.Specs.Cli -- headers        # per-revision registries and their authority
dotnet run --project Vortex.Specs.Cli -- diff
dotnet run --project Vortex.Specs.Cli -- bootstrap      # regenerate; unchanged checkout = identical tree
dotnet run --project Vortex.Specs.Cli -- validate       # also runs in VortexCloudFastCheck
```

`conflicts` prints 20 entries per kind unless `--limit` says otherwise, and the list mixes authorities:
a disagreement with a reference emulator is evidence, one with `client_code` is a bug in one of the
two. `check-wire-conflicts.mjs` is that filter, already applied. Re-scan sources with
`scan-emulator` / `scan-client` / `scan-references`, import a capture with `import-capture`.

Hand-written `verified:` and `manual:` blocks in a spec file survive regeneration. An edit inside
`generated:` blocks the next regeneration instead of being reverted.

## Validation commands
```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```
