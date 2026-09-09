<#
.SYNOPSIS
    Builds the plugin and assembles a Thunderstore-ready zip in dist/.

.DESCRIPTION
    The archive contains ONLY files this project owns:
        ValheimAutoCleanup.dll  README.md  CHANGELOG.md  manifest.json  icon.png

    It must never contain assembly_valheim.dll, Unity assemblies, BepInEx binaries
    or any other Valheim game file. Those are copyrighted third-party binaries.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number
$stage = Join-Path $root 'dist/stage'
$out = Join-Path $root "dist/ValheimAutoCleanup-$version.zip"

Write-Host "Building ValheimAutoCleanup $version"

dotnet build (Join-Path $root 'src/ValheimAutoCleanup/ValheimAutoCleanup.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $root 'src/ValheimAutoCleanup/bin/Release/ValheimAutoCleanup.dll'
if (-not (Test-Path $dll)) { throw "Build output not found at $dll" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $out) { Remove-Item $out -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item $dll                                   $stage
Copy-Item (Join-Path $root 'README.md')          $stage
Copy-Item (Join-Path $root 'CHANGELOG.md')       $stage
Copy-Item (Join-Path $root 'manifest.json')      $stage
Copy-Item (Join-Path $root 'icon.png')           $stage

# Guard against ever shipping a game binary.
$forbidden = Get-ChildItem $stage -Recurse -File |
    Where-Object { $_.Name -like 'assembly_*' -or $_.Name -like 'UnityEngine*' -or
                   $_.Name -like 'BepInEx*'   -or $_.Name -like '0Harmony*' }
if ($forbidden) {
    throw "Refusing to package: a game or loader binary ended up in the staging folder."
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $out -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

Write-Host "Package written to $out"
Get-ChildItem $root/dist
