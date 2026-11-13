# Re-fetches the redistributables GridShell.iss bundles into the installer (installer\redist\
# VC_redist.x64.exe is committed directly - run this only to pull a newer release from
# Microsoft, then commit the updated file).
$ErrorActionPreference = "Stop"
$redistDir = Join-Path $PSScriptRoot "redist"
New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" `
    -OutFile (Join-Path $redistDir "VC_redist.x64.exe")

Write-Host "Fetched VC_redist.x64.exe into $redistDir"
