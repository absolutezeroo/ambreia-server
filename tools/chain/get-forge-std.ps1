# =================================================================================================
# Fetches forge-std (Foundry's test standard library) into chain/lib/forge-std.
#
#   pwsh tools/chain/get-forge-std.ps1
#
# From foundry-rs' own GitHub release, and NOT from npm. There is a `forge-std` package on npm, it
# installs without complaint, and it is an "Unofficial NPM distribution" published by a third party
# and frozen at 1.1.2 against an upstream that is on 1.16 — an out-of-date mirror of a test library,
# from a repository that is not Foundry's. OpenZeppelin comes from npm because @openzeppelin
# publishes it there itself; this does not, so it comes from the source.
#
# Test-only: nothing deployed depends on it, which is why chain/lib is gitignored and this exists.
# =================================================================================================

$ErrorActionPreference = 'Stop'

$version = 'v1.16.2'
$root = Join-Path $PSScriptRoot '..\..\chain\lib\forge-std'
$tarball = Join-Path $env:TEMP "forge-std-$version.tar.gz"
$staging = Join-Path $env:TEMP "forge-std-$version"

if (Test-Path (Join-Path $root 'src\Test.sol'))
{
    Write-Host "forge-std $version already present. Delete chain/lib/forge-std to re-fetch."

    exit 0
}

Write-Host "Fetching forge-std $version..."
Invoke-WebRequest -Uri "https://github.com/foundry-rs/forge-std/archive/refs/tags/$version.tar.gz" -OutFile $tarball

Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

# GitHub's tag archives wrap everything in one directory named after the repo and tag, so the
# contents are lifted out of it rather than nested under it.
tar -xzf $tarball -C $staging
$extracted = (Get-ChildItem $staging -Directory | Select-Object -First 1).FullName

New-Item -ItemType Directory -Force (Split-Path $root) | Out-Null
Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
Move-Item $extracted $root

Remove-Item $tarball, $staging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Installed forge-std $version into chain/lib/forge-std."
