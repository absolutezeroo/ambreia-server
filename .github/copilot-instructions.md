# Copilot instructions

The contract for every AI tool in this repository is `AGENTS.md` (commands, where code goes, rules,
gotchas, what "done" means). Read it first; `docs/architecture.md` maps the runtime flows.

Before editing these areas, read the matching rule file — they are written for Claude Code but hold
for any tool:

| Editing | Read |
|---|---|
| `Vortex.*/Grains/**`, `Vortex.Rooms`, `Vortex.Players` | `.claude/rules/grains.md` |
| `Vortex.Protocol`, `Vortex.Revisions`, `Vortex.PacketHandlers` | `.claude/rules/protocol.md` |
| `Vortex.Database`, `scripts/sql`, `scripts/*.ps1` | `.claude/rules/database.md` |
| tests | `.claude/rules/tests.md` |

Step-by-step procedures (new packet, schema change, furniture logic, removing a feature) are in
`.claude/skills/*/SKILL.md`.
