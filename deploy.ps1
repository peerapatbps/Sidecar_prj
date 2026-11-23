#Requires -Version 5.1
<#
.SYNOPSIS
    Deploy the 4 published projects to the plant layout on D:\Sidecar_prj.

.DESCRIPTION
    Copies each <project>\publish to D:\Sidecar_prj\<project> (additive):
      TruckSidecar, TruckLauncher, TruckWatchdog, Uroboros_mini

    Safety rules:
      * Never mirrors / deletes at the destination. Runtime state that lives in the
        deployed folder survives a re-deploy (sidecar data\, all logs\, uploader-state.json,
        media\, a token_store refreshed on the plant PC).
      * Never ships dev/mock runtime state (data\, logs\, media\, *.stopped, uploader-state.json).
      * Program files + config files (config.json / launcher.json / watchdog.json /
        mini-uploader.json / config_\config.ini) are authoritative from dev and get overwritten.
        (Overwriting Uroboros_mini config_\config.ini resets its timestamp= so the next run does
        one extra MDB re-sync/upload. Harmless.)
      * Secrets (credentials.json, token_store\) are copied ONLY if missing at the destination,
        so a token refreshed on the plant PC is preserved on re-deploy. First deploy seeds them.
      * Running instances are stopped first (watchdog first so it won't relaunch the sidecar),
        because a running exe is locked.

    Prereqs: run "dotnet publish" for all 4 first (use C:\Program Files\dotnet\dotnet.exe on the
    dev machine; the PATH dotnet is x86 and cannot run the x64 Uroboros build). Writing to D:\ may
    require running this script as Administrator depending on the drive's permissions.

.PARAMETER SrcRoot
    Folder holding the 4 project folders (default: this script's own folder).

.PARAMETER DestRoot
    Deploy root (default: D:\Sidecar_prj).

.PARAMETER NoStop
    Do not stop running instances before copying (copy will fail on locked exes).

.PARAMETER DryRun
    List what would be copied (robocopy /L); do not stop processes or copy secrets.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [string]$SrcRoot  = '',
    [string]$DestRoot = 'D:\Sidecar_prj',
    [switch]$NoStop,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Resolve the script's own folder robustly ($PSScriptRoot can be empty in some -File hosts).
if ([string]::IsNullOrWhiteSpace($SrcRoot)) {
    if ($PSScriptRoot)      { $SrcRoot = $PSScriptRoot }
    elseif ($PSCommandPath) { $SrcRoot = Split-Path -Parent $PSCommandPath }
    else                    { $SrcRoot = (Get-Location).Path }
}

function Write-Step($m)  { Write-Host ("==> " + $m) -ForegroundColor Cyan }
function Write-Ok($m)    { Write-Host ("    [ok] " + $m) -ForegroundColor Green }
function Write-Warn2($m) { Write-Host ("    [warn] " + $m) -ForegroundColor Yellow }

$projects     = 'TruckSidecar', 'TruckLauncher', 'TruckWatchdog', 'Uroboros_mini'
$excludeDirs  = @('data', 'logs', 'media', '_bad')     # runtime dirs: keep at dest, never ship from dev
$excludeFiles = @('uploader-state.json', '*.stopped')  # runtime files
$secretFiles  = @('credentials.json')                  # copy only if missing at dest
$secretDirs   = @('token_store')                       # copy only if missing at dest

# --- Preflight ---------------------------------------------------------------
$mode = ''
if ($DryRun) { $mode = '  [DRY RUN]' }
Write-Step ("Preflight (SrcRoot=" + $SrcRoot + "  DestRoot=" + $DestRoot + $mode + ")")

if (-not (Test-Path 'D:\TRUCK93A\truck93a.exe')) {
    Write-Warn2 'D:\TRUCK93A\truck93a.exe not found - legacy program; system will not run without it.'
}
if (-not (Test-Path 'D:\TRUCK93A\data.mdb')) {
    Write-Warn2 'D:\TRUCK93A\data.mdb not found - sidecar + uroboros_mini read this file.'
}
foreach ($p in $projects) {
    $src = Join-Path $SrcRoot ($p + '\publish')
    if (-not (Test-Path $src)) { throw ("Missing publish folder: " + $src + "  (run 'dotnet publish' first)") }
}
Write-Ok 'publish folders present for all 4 projects'

# --- Stop running instances --------------------------------------------------
if (-not $NoStop -and -not $DryRun) {
    Write-Step 'Stopping running instances'
    foreach ($proc in 'TruckWatchdog', 'TruckLauncher', 'TruckSidecar', 'Uroboros.MiniUploader') {
        $ps = Get-Process -Name $proc -ErrorAction SilentlyContinue
        if ($ps) { $ps | Stop-Process -Force; Write-Ok ("stopped " + $proc) }
    }
    Start-Sleep -Seconds 1
}
elseif ($NoStop) {
    Write-Warn2 'NoStop set - copy will fail if any target exe is running/locked.'
}

# --- Copy --------------------------------------------------------------------
foreach ($p in $projects) {
    $src = Join-Path $SrcRoot ($p + '\publish')
    $dst = Join-Path $DestRoot $p
    Write-Step ("Deploy " + $p + "  ->  " + $dst)

    if (-not $DryRun) { New-Item -ItemType Directory -Force -Path $dst | Out-Null }

    # robocopy: recursive, additive (no /MIR), quiet. Exclude runtime state + secrets.
    $rc = @($src, $dst, '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NP', '/NJH', '/NJS')
    if ($DryRun) { $rc += '/L' }
    foreach ($d in $excludeDirs)  { $rc += '/XD'; $rc += (Join-Path $src $d) }
    foreach ($d in $secretDirs)   { $rc += '/XD'; $rc += (Join-Path $src $d) }
    foreach ($f in $excludeFiles) { $rc += '/XF'; $rc += $f }
    foreach ($f in $secretFiles)  { $rc += '/XF'; $rc += $f }

    & robocopy @rc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ("robocopy failed for " + $p + " (exit " + $LASTEXITCODE + ")") }
    Write-Ok ("program + config copied (robocopy code " + $LASTEXITCODE + ")")

    if ($DryRun) { continue }

    # Secrets: seed only if missing at dest (preserve a token refreshed on the plant PC).
    foreach ($f in $secretFiles) {
        $s = Join-Path $src $f; $t = Join-Path $dst $f
        if ((Test-Path $s) -and -not (Test-Path $t)) { Copy-Item $s $t -Force; Write-Ok ("seeded " + $f + " (first deploy)") }
        elseif (Test-Path $t) { Write-Warn2 ("kept existing " + $f + " at dest (not overwritten)") }
    }
    foreach ($d in $secretDirs) {
        $s = Join-Path $src $d; $t = Join-Path $dst $d
        if ((Test-Path $s) -and -not (Test-Path $t)) { Copy-Item $s $t -Recurse -Force; Write-Ok ("seeded " + $d + "\ (first deploy)") }
        elseif (Test-Path $t) { Write-Warn2 ("kept existing " + $d + "\ at dest (not overwritten)") }
    }
}

Write-Step 'Done.'
if (-not $DryRun) {
    Write-Host ''
    Write-Host 'Next:' -ForegroundColor Cyan
    Write-Host '  * Ensure D:\TRUCK93A has truck93a.exe + data.mdb'
    Write-Host '  * Point the operator desktop shortcut at D:\Sidecar_prj\TruckLauncher\TruckLauncher.exe'
    Write-Host '  * Verify Uroboros_mini has credentials.json + token_store\ beside its exe'
}

exit 0   # normalize exit code (robocopy leaves 0-7 on success)
