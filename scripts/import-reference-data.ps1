# Fills a fork database with the hotel's reference content (room models, furniture definitions,
# catalogue, base currencies) copied from an older Vortex/Turbo database, then re-applies the seeds
# that only match rows once furniture definitions exist.
#
#   scripts/import-reference-data.ps1                                  # turbo -> vortex_m0_check
#   scripts/import-reference-data.ps1 -Source turbo -Target vortex
#
# The source is only read. The target must already carry the InitialCreate migration
# (scripts/ef.ps1 database update). Re-runnable: see the header of scripts/sql/import_reference_data.sql
# for what a re-run replaces. Nothing a player owns is copied -- no account, room, furniture item,
# wallet or log.
#
# Credentials: the password comes from $env:MYSQL_PWD when set, otherwise from the connection string
# in Vortex.Main's user-secrets (Vortex:Database:ConnectionString). It is never printed.
# The mysql client is $env:VORTEX_MYSQL, else the first one under C:\laragon\bin\mysql, else PATH.

param(
    [string]$Source = "turbo",
    [string]$Target = "vortex_m0_check",
    [string]$Server = "127.0.0.1",
    [int]$Port = 3306,
    [string]$User = "root"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

foreach ($name in $Source, $Target) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') { throw "Database name '$name' is not a plain identifier." }
}
if ($Source -eq $Target) { throw "Source and target are the same database." }

$mysql = $env:VORTEX_MYSQL
if (-not $mysql) {
    $mysql = Get-ChildItem "C:\laragon\bin\mysql\*\bin\mysql.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $mysql) { $mysql = (Get-Command mysql -ErrorAction Stop).Source }

$hadPassword = [bool]$env:MYSQL_PWD
if (-not $hadPassword) {
    $secret = dotnet user-secrets list --project (Join-Path $repoRoot "Vortex.Main") |
        Select-String -Pattern 'Password=([^;]*)'
    if (-not $secret) { throw "No MYSQL_PWD and no password in Vortex.Main user-secrets." }
    $env:MYSQL_PWD = $secret.Matches[0].Groups[1].Value
}

# Applied by InitialCreate on an empty furniture_definitions, where they matched nothing. Same order
# as InitialCreate. Each is keyed on name or outcome, so replaying them is safe.
$seeds = @(
    "catalog_pet_products.sql",
    "guild_furni_logic.sql",
    "furni_logic_bindings.sql",
    "furni_stuff_data_types.sql",
    "highscore_families.sql",
    "purchasable_clothing.sql",
    "furni_logic_self_named_rebind.sql",
    "categorize_room_decorations.sql"
)

$batch = @("SET @src = '$Source';", "source scripts/sql/import_reference_data.sql")
$batch += $seeds | ForEach-Object { "source Vortex.Database/Seeds/$_" }
$batch += "SELECT 'room_models', COUNT(*) FROM room_models UNION ALL
SELECT 'furniture_definitions', COUNT(*) FROM furniture_definitions UNION ALL
SELECT 'furniture_purchasable_clothing', COUNT(*) FROM furniture_purchasable_clothing UNION ALL
SELECT 'catalog_pages', COUNT(*) FROM catalog_pages UNION ALL
SELECT 'catalog_offers', COUNT(*) FROM catalog_offers UNION ALL
SELECT 'catalog_products', COUNT(*) FROM catalog_products UNION ALL
SELECT 'catalog_club_offers', COUNT(*) FROM catalog_club_offers UNION ALL
SELECT 'catalog_club_gifts', COUNT(*) FROM catalog_club_gifts UNION ALL
SELECT 'catalog_frontpage_items', COUNT(*) FROM catalog_frontpage_items UNION ALL
SELECT 'currency_types', COUNT(*) FROM currency_types;"

Push-Location $repoRoot  # `source` paths are relative to the client's working directory
try {
    $guard = & $mysql "-h$Server" "-P$Port" "-u$User" -N -B $Target `
        -e "SELECT COUNT(*) FROM __efmigrationshistory WHERE MigrationId LIKE '%_InitialCreate'"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read the target's migration history." }
    if ("$guard".Trim() -ne "1") {
        throw "'$Target' is not a fork database (no InitialCreate in its history)."
    }

    & $mysql "-h$Server" "-P$Port" "-u$User" --default-character-set=utf8mb4 -N -B $Target `
        -e ($batch -join "`n")
    if ($LASTEXITCODE -ne 0) { throw "Import failed (mysql exit $LASTEXITCODE)." }
}
finally {
    Pop-Location
    if (-not $hadPassword) { Remove-Item Env:MYSQL_PWD }
}
