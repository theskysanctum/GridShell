# Re-fetches the redistributables GridShell.iss bundles into the installer (installer\redist\
# VC_redist.x64.exe is committed directly - run this only to pull a newer release from
# Microsoft, then commit the updated file).
$ErrorActionPreference = "Stop"
$redistDir = Join-Path $PSScriptRoot "redist"
New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" `
    -OutFile (Join-Path $redistDir "VC_redist.x64.exe")

Invoke-WebRequest -Uri "https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe" `
    -OutFile (Join-Path $redistDir "directx_redist.exe")

Write-Host "Fetched VC_redist.x64.exe and directx_redist.exe into $redistDir"
