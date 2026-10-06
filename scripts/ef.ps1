# Wrapper around `dotnet ef` for VortexDbContext.
#
# It exists because three things are easy to get wrong and none of them fail loudly:
#   - the working directory (VortexDbContextFactory reads appsettings.json from `cwd/..`);
#   - the connection string variable, which is UNPREFIXED here -- the runtime's `VORTEX__` prefix
#     is ignored by the design-time factory, so a `VORTEX__`-spelled variable silently does nothing;
#   - the server version, which must be pinned or `ServerVersion.AutoDetect` opens a real
#     connection and authoring a migration needs a running database.
#
#   scripts/ef.ps1 migrations add AddThing
#   scripts/ef.ps1 database update
#   scripts/ef.ps1 migrations list
#   scripts/ef.ps1 migrations script --idempotent --output artifacts/migrations.sql
#
# Override either value by setting it before the call:
#   $env:Vortex__Database__ConnectionString = "..."   # default: appsettings.Development.json
#   $env:Vortex__Database__ServerVersion    = "8.4-mysql"

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "Vortex.Cloud.sln"))) {
    throw "Expected the repository root at '$repoRoot'."
}

if (-not $env:Vortex__Database__ServerVersion) {
    $env:Vortex__Database__ServerVersion = "8.0-mysql"
}

Push-Location $repoRoot
try {
    dotnet tool restore | Out-Null

    Push-Location (Join-Path $repoRoot "Vortex.Database")
    try {
        dotnet ef @args
        $efExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    # A migration is discovered by the id in its [Migration] attribute, never by its file name.
    # Renaming a file leaves the two disagreeing, and EF then applies a migration the history table
    # records under a different id -- which is how `Duplicate column name 'ip_hash'` happened.
    Get-ChildItem (Join-Path $repoRoot "Vortex.Database/Migrations/*.Designer.cs") | ForEach-Object {
        $expected = $_.Name -replace '\.Designer\.cs$', ''
        $declared = (Select-String -Path $_.FullName -Pattern '\[Migration\("([^"]+)"\)\]' `
            | Select-Object -First 1).Matches.Groups[1].Value
        if ($declared -ne $expected) {
            Write-Warning "Migration id drift: file '$expected' declares '$declared'. Rename the file back or EF will apply it under the wrong id."
        }
    }

    exit $efExit
}
finally {
    Pop-Location
}
