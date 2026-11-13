# Fetches the redistributables GridShell.iss bundles into the installer - run this once before
# building the installer (installer\redist\ is gitignored, not committed, since it's just
# Microsoft's own unchanging download, not anything GridShell's repo needs to carry).
$ErrorActionPreference = "Stop"
$redistDir = Join-Path $PSScriptRoot "redist"
New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" `
    -OutFile (Join-Path $redistDir "VC_redist.x64.exe")

Write-Host "Fetched VC_redist.x64.exe into $redistDir"
