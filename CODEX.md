# Codex Adapter (Vortex Cloud)

This adapter points Codex to the canonical AI contract for this repository.

## Workspace context
When this repository is opened through the parent multi-root workspace, also load
`../docs/AI_CONTEXT.md` before making workspace-level or cross-project changes.
That file defines the Vortex Core product identity, the Vortex technical naming
boundary, and the rule that the server and client roots must stay separate.

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

## Validation commands
```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```
