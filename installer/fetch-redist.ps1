# Fetches the redistributables GridShell.iss bundles into the installer - run this once before
# building the installer. These are hosted on this project's own "redist-assets" GitHub
# Release (https://github.com/theskysanctum/gridshell/releases/tag/redist-assets), not
# committed to the repo tree - StoreCatalogSync does a full clone of this repo on every Store
# open, and these binaries are too large for that to stay cheap. Re-run this and re-upload to
# that release if Microsoft ever ships newer versions.
$ErrorActionPreference = "Stop"
$redistDir = Join-Path $PSScriptRoot "redist"
New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

Invoke-WebRequest -Uri "https://github.com/theskysanctum/gridshell/releases/download/redist-assets/VC_redist.x64.exe" `
    -OutFile (Join-Path $redistDir "VC_redist.x64.exe")

Invoke-WebRequest -Uri "https://github.com/theskysanctum/gridshell/releases/download/redist-assets/directx_redist.exe" `
    -OutFile (Join-Path $redistDir "directx_redist.exe")

Write-Host "Fetched VC_redist.x64.exe and directx_redist.exe into $redistDir"
