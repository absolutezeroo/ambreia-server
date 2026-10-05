# Claude Adapter (Vortex Cloud)

Ce dépôt est le fork RPG de Vortex : le protocole diverge volontairement du client Habbo officiel. Spec : `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md`.

This adapter points Claude to the canonical AI contract for this repository.

## Required context load order
1. `AGENTS.md`
2. `CONTEXT.md`
3. One relevant sample in `docs/patterns/`
4. `.github/copilot-instructions.md` (tool adapter parity rules)

## Non-negotiable constraints
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
| `scripts/hooks/post-edit.mjs` (PostToolUse) | any `Edit`/`Write` | dashboard capability/route/locale parity; `eslint` on touched `.svelte`/front-end `.js` (`npm run build` misses an undefined identifier in markup); `csharpier` on the touched `.cs` — it **reformats** the file, so re-read it before the next `Edit` |
| `scripts/hooks/guard-emulator.mjs` (PreToolUse) | `Bash`/`PowerShell` | a command that would kill the running `Vortex.Main` |
| `scripts/hooks/guard-commit.mjs` (PreToolUse) | `Bash`/`PowerShell` | a `git commit` that names nothing (no `-o`/`--only`/`-- <paths>`) or a `git add` that sweeps the tree — the index is shared with the user and with any other session |
| `scripts/hooks/check-logic-groups.mjs` | FastCheck | a `[RoomObjectLogic("...")]` key missing from the admin's furniture logic dropdown — the behaviour exists but nobody can pick it; `--write` regenerates |
| `scripts/hooks/check-variable-capabilities.mjs` | FastCheck | a wired variable declaring a `WiredVariableFlags` capability nothing implements — every picker in the wired editor filters on one flag, so the box is offered, picked, saved, and silent. Catches a named-but-unimplemented capability only: a flag copied from another variable at runtime (the echo box) and a flag that is true over a missing store (the room, context and shared variables) are both invisible to it |
| `scripts/hooks/check-webapi-forwarding.mjs` | FastCheck | a `[FromServices]` type absent from `WebApiWebHost.ForwardSingletons` or from `WebApiTestFactory`. The web API builds its **own** container inside the Orleans host; an unannotated parameter that is missing is read as the request body and kills the route table at startup (loud), but a `[FromServices]` one builds fine and answers **500 per request, forever** — and no test can reach it either. Three occurrences: the chain, the audit sink, a room photo |
| `.claude/agents/grain-rules-reviewer.md` | on request | the Orleans rules in `AGENTS.md` that no analyzer enforces |
| `/new-dashboard-page`, `/sql-fix` | user-invoked | the checklists that get half-remembered |
| `dashboard-render` skill | after any visual dashboard edit | shipping a redesign without once looking at it — the CSS an operator sees is embedded in the assembly, so a green build proves nothing |
| `mysql` MCP (`.mcp.json`) | on request | read-only queries against the `turbo` DB; writes stay in reviewed `scripts/sql/` scripts |

`node scripts/hooks/__test/run.mjs` asserts the hooks still behave (exit 0 allow / exit 2 block); it
runs in the QualityGate too, because a hook that stops blocking fails open and says nothing.

## Validation commands
```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```
