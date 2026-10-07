# Contributing

If this is your first time in the repo, run bootstrap:

PowerShell:

```powershell
pwsh -File scripts/bootstrap.ps1
```

bash/zsh:

```bash
sh scripts/bootstrap.sh
```

## Build and quality scope

| Command | Scope | Use when |
| --- | --- | --- |
| `dotnet build Vortex.Main/Vortex.Main.csproj` | Core emulator only | Default contributor build |
| `dotnet build Vortex.Cloud.sln` | Full integrated solution build (includes sample plugin project if present in solution) | One-window core+plugin workflow |
| `dotnet build ../turbo-sample-plugin/TurboSamplePlugin/TurboSamplePlugin.csproj` | Sample plugin only | Plugin iteration |

The repo's default quality/build contract is project-scoped to `Vortex.Main/Vortex.Main.csproj`.

### Fast check (pre-commit)

```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudFastCheck
```

This is the default local commit gate and should stay fast.

### Full gate (pre-push + CI)

```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate
```

This includes formatting/style/analyzer enforcement plus build and is required before pushing.

Policy phase controls:
- Current default: `VortexAIPolicyPhase=1` (warn-first rollout).
- Preview strict phase locally:

```bash
dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate -p:VortexAIPolicyPhase=2
```

## Integrated plugin workflow

Canonical integrated workflow is documented in `../turbo-sample-plugin/README.md`.
Run from `vortex-cloud` root:
- PowerShell: `pwsh -File ../turbo-sample-plugin/scripts/dev-integrated.ps1`
- bash/zsh: `sh ../turbo-sample-plugin/scripts/dev-integrated.sh`

## Toolchain pinning

Toolchain versions are pinned for consistent local and CI behavior:
- .NET SDK pin: `global.json`
- Local tools pin: `.config/dotnet-tools.json`

When updating these, include the version bump and a passing `VortexCloudQualityGate` run in the same change.

## Git hooks

Repository-managed hooks are in `.githooks`:
- `pre-commit` -> `VortexCloudFastCheck`
- `pre-push` -> `VortexCloudQualityGate`

Enable them if needed:

```bash
git config --local core.hooksPath .githooks
```

## AI-assisted contribution policy

All AI tools follow `AGENTS.md`; area rules are in `.claude/rules/`, procedures in
`.claude/skills/`, runtime flows in `docs/architecture.md`. When a recurring mistake shows up, fix the
contract in the same change: a rule in the area file (paired with what to do instead), a step in the
matching skill, or — if it must hold every time — a hook or a test.

## Local development config

`appsettings.Development.json` is local-only and must not be committed.

Recommended flow:
1. Keep shared defaults in `appsettings.json`.
2. Copy to `appsettings.Development.json` if missing.
3. Put machine-specific settings in `appsettings.Development.json`.

## Run in Development mode

PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT="Development"; dotnet run --project Vortex.Main/Vortex.Main.csproj
```

bash/zsh:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project Vortex.Main/Vortex.Main.csproj
```

## Troubleshooting MySQL connection

If you get `Unable to connect to any of the specified MySQL hosts`:
1. Verify `Vortex:Database:ConnectionString` in `appsettings.Development.json`.
2. Verify MySQL is listening on that host and port.
3. Verify no `VORTEX__...` environment variable overrides the connection string.

## Troubleshooting integrated solution build

If `dotnet build Vortex.Cloud.sln` fails due to plugin state but core work is unaffected, fall back to:

```bash
dotnet build Vortex.Main/Vortex.Main.csproj
```

## Orleans design and lifecycle checks

For the grain model and the session/presence flow, see `docs/architecture.md`.
