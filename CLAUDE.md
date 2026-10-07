@AGENTS.md

# Claude Code specifics

Answer the owner in French. Work on `main`; the owner runs everything locally and never pushes.

## Skills (load the matching one before starting)

| Task | Skill |
|---|---|
| Add or change a packet (message, parser, serializer, handler) | `adding-a-packet` |
| Add or change an entity, a table, a seed | `changing-the-schema` |
| Give a furniture a behaviour | `adding-a-room-object-logic` |
| Delete a feature or hunt dead code | `removing-dead-code` |
| Run the emulator, log a player in, prove a change live | `run-vortex-emulator` |
| One-off data correction | `/sql-fix` (owner-invoked; propose it) |

Agent: `grain-rules-reviewer` reviews a diff under `Grains/` against `.claude/rules/grains.md`.

## Hooks (`.claude/settings.json`)

| Hook | Effect |
|---|---|
| `post-edit.mjs` (after `Edit`/`Write`) | runs `csharpier` on the touched `.cs` — it **rewrites the file**: re-read it before the next `Edit` |
| `guard-emulator.mjs` (before shell) | blocks a command that would kill a running `Vortex.Main` |
| `guard-commit.mjs` (before shell) | blocks `git commit` without explicit paths and `git add -A`/`.` |

`node scripts/hooks/__test/run.mjs` proves the hooks still block; it runs in the QualityGate.

## This machine (Windows)

- Git Bash rewrites `/api/...` arguments into `C:/Program Files/Git/api/...`: prefix `MSYS_NO_PATHCONV=1`
  or use PowerShell. PowerShell splits `-h127.0.0.1` into two tokens: write `--host=127.0.0.1`.
- A heredoc through Bash can drop backslashes from a script's regexes: write script files with the
  `Write` tool, then run them.
- Laragon MySQL takes more than 10 s to listen after a start (`driver.mjs mysql` waits up to 60 s).
- `dotnet build … | tail` reported success on a failed build. Redirect to a file, then read it.
- A commit with hundreds of paths overflows the command line: `git commit -o --pathspec-from-file=<file>`.
- Long builds and the gate (≈ 2 min) go to `run_in_background`; never `sleep` to wait.
