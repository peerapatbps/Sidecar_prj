#Requires -Version 5.1
<#
.SYNOPSIS
    Assemble a clean, copy-ready deploy package on THIS (dev) machine.

.DESCRIPTION
    Runs on the dev machine after "dotnet publish" for all 4 projects. Builds:

        <SrcRoot>\_deploy\
            TruckSidecar\      (publish, minus dev runtime state)
            TruckLauncher\
            TruckWatchdog\
            Uroboros_mini\     (publish, minus dev logs; WITH credentials.json + token_store\)
            deploy.ps1         (optional safe re-deploy script, for the target machine)

    Then copy the CONTENTS of _deploy\ onto the target machine's D:\Sidecar_prj\
    (drag _deploy\* into D:\Sidecar_prj\).

    What is stripped from each publish (dev-only runtime state, must not ship):
        data\  logs\  media\  _bad\  *.stopped  uploader-state.json

    Secrets ARE included in the package (credentials.json + token_store\). Note: if the target
    already ran and refreshed its Google token, dragging the package over it replaces token_store
    with the dev copy; the app just re-refreshes on next run (same account/credentials), so this is
    harmless. If you prefer to preserve the target token, exclude token_store\ when you drag, OR
    run the bundled deploy.ps1 ON THE TARGET (it seeds secrets only when missing).

    Each _deploy\<project> is rebuilt clean (mirrored) every run, so stale files are removed.
    _deploy\ holds no runtime state of its own, so mirroring it is safe.

.PARAMETER SrcRoot
    Folder holding the 4 project folders (default: this script's own folder).

.PARAMETER StageRoot
    Output package folder (default: <SrcRoot>\_deploy).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\stage.ps1
#>
[CmdletBinding()]
param(
    [string]$SrcRoot   = '',
    [string]$StageRoot = ''
)

$ErrorActionPreference = 'Stop'

# Resolve the script's own folder robustly ($PSScriptRoot can be empty in some -File hosts).
if ([string]::IsNullOrWhiteSpace($SrcRoot)) {
    if ($PSScriptRoot)      { $SrcRoot = $PSScriptRoot }
    elseif ($PSCommandPath) { $SrcRoot = Split-Path -Parent $PSCommandPath }
    else                    { $SrcRoot = (Get-Location).Path }
}
if ([string]::IsNullOrWhiteSpace($StageRoot)) { $StageRoot = Join-Path $SrcRoot '_deploy' }

function Write-Step($m)  { Write-Host ("==> " + $m) -ForegroundColor Cyan }
function Write-Ok($m)    { Write-Host ("    [ok] " + $m) -ForegroundColor Green }
function Write-Warn2($m) { Write-Host ("    [warn] " + $m) -ForegroundColor Yellow }

$projects     = 'TruckSidecar', 'TruckLauncher', 'TruckWatchdog', 'Uroboros_mini'
$excludeDirs  = @('data', 'logs', 'media', '_bad')     # dev runtime dirs: never ship
$excludeFiles = @('uploader-state.json', '*.stopped')  # dev runtime files: never ship

Write-Step ("Stage (SrcRoot=" + $SrcRoot + "  StageRoot=" + $StageRoot + ")")

foreach ($p in $projects) {
    $src = Join-Path $SrcRoot ($p + '\publish')
    if (-not (Test-Path $src)) { throw ("Missing publish folder: " + $src + "  (run 'dotnet publish' first)") }
}
Write-Ok 'publish folders present for all 4 projects'

foreach ($p in $projects) {
    $src = Join-Path $SrcRoot ($p + '\publish')
    $dst = Join-Path $StageRoot $p
    Write-Step ("Stage " + $p + "  ->  " + $dst)

    # /MIR = clean rebuild of the package subfolder (safe: no runtime state lives here).
    $rc = @($src, $dst, '/MIR', '/R:2', '/W:2', '/NFL', '/NDL', '/NP', '/NJH', '/NJS')
    foreach ($d in $excludeDirs)  { $rc += '/XD'; $rc += (Join-Path $src $d) }
    foreach ($f in $excludeFiles) { $rc += '/XF'; $rc += $f }

    & robocopy @rc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ("robocopy failed for " + $p + " (exit " + $LASTEXITCODE + ")") }
    Write-Ok ("staged (robocopy code " + $LASTEXITCODE + ")")
}

# Bundle the target-side safe re-deploy script (optional to use on the target).
$deploySrc = Join-Path $SrcRoot 'deploy.ps1'
if (Test-Path $deploySrc) {
    Copy-Item $deploySrc (Join-Path $StageRoot 'deploy.ps1') -Force
    Write-Ok 'bundled deploy.ps1 (optional target-side safe re-deploy)'
}

# Warn if Uroboros secrets did not make it into the package.
$uro = Join-Path $StageRoot 'Uroboros_mini'
if (-not (Test-Path (Join-Path $uro 'credentials.json'))) { Write-Warn2 'Uroboros_mini\credentials.json NOT in package - add it before deploying.' }
if (-not (Test-Path (Join-Path $uro 'token_store')))      { Write-Warn2 'Uroboros_mini\token_store\ NOT in package - add it before deploying.' }

Write-Step 'Done.'
Write-Host ''
Write-Host 'Next:' -ForegroundColor Cyan
Write-Host ('  * Package ready at: ' + $StageRoot)
Write-Host '  * Copy its CONTENTS into the target D:\Sidecar_prj\ (drag _deploy\* -> D:\Sidecar_prj\)'
Write-Host '  * On the target ensure D:\TRUCK93A has truck93a.exe + data.mdb'
Write-Host '  * Point the operator shortcut at D:\Sidecar_prj\TruckLauncher\TruckLauncher.exe'

exit 0
