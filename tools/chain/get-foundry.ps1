# =================================================================================================
# Fetches Foundry (anvil / forge / cast) into tools/foundry, for working on the hotel's chain from a
# machine that has no Docker.
#
# A DEPLOYMENT DOES NOT NEED THIS. docker-compose runs ghcr.io/foundry-rs/foundry instead, which is
# the same programs from the same release; this script exists because the binaries are 230 MB and
# therefore gitignored, so a fresh checkout has to fetch them, and because "download a zip from
# GitHub" is the kind of setup step that otherwise survives only in somebody's memory.
#
#   pwsh tools/chain/get-foundry.ps1
#   tools/foundry/anvil.exe --version
#
# The version is pinned. Anvil's genesis accounts are fixed by its default mnemonic and not by its
# version, so an upgrade does not move anybody's address -- but forge's solc resolution and the
# bytecode it produces do move, and a deployment that compiled elsewhere should compile here the
# same way. Bump it deliberately, together with the image tag in docker-compose.yml.
# =================================================================================================

$ErrorActionPreference = 'Stop'

$version = 'v1.8.3'
$asset = "foundry_${version}_win32_amd64"
$base = "https://github.com/foundry-rs/foundry/releases/download/$version"

$root = Join-Path $PSScriptRoot '..\foundry'
$zip = Join-Path $env:TEMP "$asset.zip"

if (Test-Path (Join-Path $root 'anvil.exe'))
{
    $installed = & (Join-Path $root 'anvil.exe') --version

    Write-Host "Already present: $installed"
    Write-Host "Delete tools/foundry to force a re-fetch."

    exit 0
}

New-Item -ItemType Directory -Force $root | Out-Null

Write-Host "Fetching Foundry $version (about 105 MB)..."
Invoke-WebRequest -Uri "$base/$asset.zip" -OutFile $zip
Invoke-WebRequest -Uri "$base/$asset.sha256" -OutFile "$zip.sha256"

# Checked, not assumed. These are executables that will hold the hotel's treasury key in memory, and
# the release publishes a digest precisely so that nobody has to trust the transfer.
$expected = (Get-Content "$zip.sha256" -Raw).Trim().Split()[0]
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()

if ($expected -ne $actual)
{
    throw "SHA256 mismatch for $asset.zip (expected $expected, got $actual). Not extracting."
}

Expand-Archive -Path $zip -DestinationPath $root -Force
Remove-Item $zip, "$zip.sha256" -Force

Write-Host "Installed: $(& (Join-Path $root 'anvil.exe') --version)"
