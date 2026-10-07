# Claude Adapter (Vortex Cloud)

Ce dépôt est le fork RPG de Vortex : le protocole diverge volontairement du client Habbo officiel. Spec : `docs/superpowers/specs/2026-10-05-habbo-rpg-design.md`.
Depuis la coupe au noyau (2026-10-07, `docs/superpowers/plans/2026-10-07-coupe-noyau.md`), le serveur ne garde que le réseau, la connexion, les zones (heightmap, pathfinding, avatars, meubles) et une web API minimale (inscription, connexion, avatar, ticket SSO). Pas de chat, d'économie, de social, de navigateur ni de dashboard : les systèmes RPG reviennent sur cette base.

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
- For `Revision<id>` parser/serializer work other than the embedded `Revision20260701`, edit `../turbo-sample-plugin/TurboSamplePlugin/Revision/**`, not `vortex-cloud`.
- `turbo` (MySQL) is the read-only source of reference data; never write to it. The fork runs on its own database (Vortex.Main user-secrets); never print its password.
- A grain interface whose implementation is gone still compiles and fails at runtime on the first call. Removing a feature means removing its callers and its `Get*Grain` accessor in `GrainFactoryExtensions`, not only the grain.

## Repository automations
Configured in `.claude/settings.json`; the scripts are plain node and run standalone too. Every
check below also runs inside the gate (`AGENTS.md`, "Required validation before completion") — a
hook fires for one tool and for the files it happens to touch, which is not the same as coverage.

| Automation | Fires on | Catches |
|---|---|---|
| `scripts/hooks/post-edit.mjs` (PostToolUse) | any `Edit`/`Write` | `csharpier` on the touched `.cs` — it **reformats** the file, so re-read it before the next `Edit` |
| `scripts/hooks/guard-emulator.mjs` (PreToolUse) | `Bash`/`PowerShell` | a command that would kill the running `Vortex.Main` |
| `scripts/hooks/guard-commit.mjs` (PreToolUse) | `Bash`/`PowerShell` | a `git commit` that names nothing (no `-o`/`--only`/`-- <paths>`) or a `git add` that sweeps the tree — the index is shared with the user and with any other session. A long path list goes through `--pathspec-from-file` |
| `scripts/hooks/check-architecture-walls.mjs` | FastCheck | the architecture walls, among them the **layer wall**: Core references no Platform/Web project, Platform no Web (`Main` exempt) — a shared type goes down into `Vortex.Primitives` |
| `scripts/hooks/check-webapi-forwarding.mjs` | FastCheck | a `[FromServices]` type absent from `WebApiWebHost.ForwardSingletons` or from `WebApiTestFactory`. The web API builds its **own** container inside the Orleans host; a `[FromServices]` type that is missing builds fine and answers **500 per request, forever** |
| `.claude/agents/grain-rules-reviewer.md` | on request | the Orleans rules in `AGENTS.md` that no analyzer enforces |
| `run-vortex-emulator` skill | on request | a change that builds but does not work: `smoke` logs a throwaway player into the start zone and walks one tile |
| `/sql-fix` | user-invoked | one-off data fixes as reviewable, re-runnable scripts in `scripts/sql/` |

`node scripts/hooks/__test/run.mjs` asserts the hooks still behave (exit 0 allow / exit 2 block); it
runs in the QualityGate too, because a hook that stops blocking fails open and says nothing.

## Validation commands
```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```
