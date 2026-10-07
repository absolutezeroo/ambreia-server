# Builds a throwaway database from scratch and fills it: drop/create, InitialCreate, reference data
# from turbo (read only), development zone. Prints table counts, never the password.
#
#   pwsh .claude/skills/changing-the-schema/scripts/fresh-db.ps1 -Name vortex_check
#
# The connection string comes from Vortex.Main's user-secrets, retargeted at -Name. The emulator
# itself is not touched: point it at the new database with
# VORTEX__Vortex__Database__ConnectionString (see the run-vortex-emulator skill).
param([Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_]+$')][string]$Name)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../../..")
Push-Location $repoRoot
try {
    if ($Name -eq "turbo") { throw "turbo is read-only reference data; pick another name." }

    $line = dotnet user-secrets list --project Vortex.Main | Select-String -Pattern '^Vortex:Database:ConnectionString = (.*)$'
    if (-not $line) { throw "No Vortex:Database:ConnectionString in Vortex.Main user-secrets." }
    $cs = $line.Matches[0].Groups[1].Value
    $env:Vortex__Database__ConnectionString = ($cs -replace '(?i)Database=[^;]*', "Database=$Name")
    $env:MYSQL_PWD = ([regex]::Match($cs, '(?i)Password=([^;]*)')).Groups[1].Value
    $mysql = Get-ChildItem "C:\laragon\bin\mysql\*\bin\mysql.exe" | Select-Object -First 1 -ExpandProperty FullName

    & $mysql --host=127.0.0.1 --user=root -e "DROP DATABASE IF EXISTS $Name; CREATE DATABASE $Name CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
    if ($LASTEXITCODE -ne 0) { throw "Could not (re)create $Name (is MySQL up? driver.mjs mysql)." }

    ./scripts/ef.ps1 database update
    if ($LASTEXITCODE -ne 0) { throw "InitialCreate failed on $Name." }

    ./scripts/import-reference-data.ps1 -Source turbo -Target $Name
    & $mysql --host=127.0.0.1 --user=root -N -B $Name -e "SELECT COUNT(*) AS tables_ FROM information_schema.tables WHERE table_schema = DATABASE(); SELECT config_key, config_value FROM server_config;"
}
finally {
    Pop-Location
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
}
