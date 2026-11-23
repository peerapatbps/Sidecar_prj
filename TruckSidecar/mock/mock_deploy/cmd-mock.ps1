# mock แบบ "cmd = โปรแกรมชั่ง": sidecar เกาะหน้าต่าง cmd (เมนูของ cmd-mock.bat) แทน Notepad
#   (ไม่ใส่)    : ล้างทั้งหมด (mock.mdb + data\ ของ sidecar) + เตรียม config (targetProcess=cmd) → เปิด stub + sidecar + watchdog
#   -WipeSidecar : ล้าง data\ ของ sidecar (record/outbox/audit) แล้วเปิด sidecar + watchdog ใหม่
# เรียกจาก cmd-mock.bat — ไม่ต้องรันเอง
param(
  [switch]$WipeSidecar,
  [int]$StubPort = 18080
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$bin  = Join-Path $here 'bin'      # TruckSidecar.exe / TruckWatchdog.exe / mock-template.mdb
$run  = Join-Path $here 'run'
$ps   = 'powershell.exe'

# ปิด watchdog ก่อน (ไม่งั้นมันเด้ง sidecar กลับ) แล้วปิด sidecar แบบเดียวกับกด Ctrl+Shift+F12:
#   set event Local\TruckSidecar.AdminExit → sidecar เขียนธง + คืนพื้นที่จอ (AppBar) + ปิดตัวเองเรียบร้อย
#   ไม่ปิดใน 5 วิ (sidecar รุ่นเก่าที่ไม่มี event / ค้าง) → kill
# sidecar/watchdog เป็น requireAdministrator → หน้าต่างที่ไม่ได้ elevate ทำไม่ได้ → ขอสิทธิ์ admin (UAC) แล้ว kill แทน
function Stop-SidecarProcs {
  $denied = $false
  foreach ($p in @(Get-Process -Name TruckWatchdog -ErrorAction SilentlyContinue)) {
    try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { $denied = $true }
  }
  $sc = @(Get-Process -Name TruckSidecar -ErrorAction SilentlyContinue)
  if ($sc.Count -gt 0 -and -not $denied) {
    try {
      $ev = [System.Threading.EventWaitHandle]::OpenExisting('Local\TruckSidecar.AdminExit')
      [void]$ev.Set(); $ev.Close()
      $sc | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
    } catch { }
  }
  foreach ($p in @(Get-Process -Name TruckSidecar -ErrorAction SilentlyContinue)) {
    try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { $denied = $true }
  }
  if ($denied) {
    Write-Host "   Admin rights needed to stop sidecar/watchdog - click Yes on the UAC prompt" -ForegroundColor Yellow
    try {
      Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command',
        "Stop-Process -Name TruckWatchdog -Force -ErrorAction SilentlyContinue; Stop-Process -Name TruckSidecar -Force -ErrorAction SilentlyContinue"
    } catch { }
    if (Get-Process -Name TruckWatchdog, TruckSidecar -ErrorAction SilentlyContinue) { throw "Cannot stop sidecar/watchdog (no admin rights)" }
  }
}

# เขียนไฟล์ข้อความ (UTF-8 ไม่มี BOM): ลบของเดิมก่อน + ลองซ้ำ
# Set-Content ของ PS 5.1 ต้องอ่านไฟล์เดิมก่อน → พังเป็น "Stream was not readable" ถ้าไฟล์เดิมถูกล็อก/อ่านไม่ได้
function Write-TextFile([string]$path, [string]$text) {
  for ($i = 1; ; $i++) {
    try {
      if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
      [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
      return
    } catch {
      if ($i -ge 5) { throw }
      Start-Sleep -Milliseconds 400
    }
  }
}

function Stop-Sidecar {
  Stop-SidecarProcs
  Start-Sleep -Milliseconds 600
}

function Start-Sidecar {
  Start-Process (Join-Path $run 'TruckSidecar.exe') -WorkingDirectory $run
  $wdSrc = Join-Path $bin 'TruckWatchdog.exe'
  if (-not (Test-Path $wdSrc)) { Write-Host "   Skipping watchdog: $wdSrc not found" -ForegroundColor Yellow; return }
  Copy-Item $wdSrc $run -Force
  $wd = [ordered]@{
    truckProcess        = 'cmd'                           # ตรงกับ targetProcess ของ sidecar ใน mock นี้
    sidecarProcess      = 'TruckSidecar'
    sidecarExe          = (Join-Path $run 'TruckSidecar.exe')
    stopFlagPath        = ''
    pollSeconds         = 3
    startupGraceSeconds = 30
  }
  try { Write-TextFile (Join-Path $run 'watchdog.json') ($wd | ConvertTo-Json -Depth 3) }
  catch { Write-Host "   Skipping watchdog: cannot write watchdog.json ($($_.Exception.Message))" -ForegroundColor Yellow; return }
  Start-Process (Join-Path $run 'TruckWatchdog.exe') -WorkingDirectory $run
}

if ($WipeSidecar) {
  Stop-Sidecar
  $data = Join-Path $run 'data'
  if (Test-Path $data) { Remove-Item $data -Recurse -Force }
  Start-Sidecar
  Write-Host "Sidecar data cleared - sidecar restarted"
  exit 0
}

if (-not (Test-Path (Join-Path $bin 'TruckSidecar.exe'))) {
  Write-Host "bin\TruckSidecar.exe not found" -ForegroundColor Red
  exit 1
}

Write-Host "[1] Stopping old sidecar/watchdog" -ForegroundColor Cyan
Stop-Sidecar      # exe ที่รันอยู่ถูก lock — ต้องปิดก่อน setup ก็อป exe ใหม่
# เริ่มใหม่ทุกครั้ง = ล้างทั้งหมด (เหมือนกดเมนู 1): ล้าง data\ ของ sidecar ตรงนี้, mock.mdb ล้างใน setup
$data = Join-Path $run 'data'
if (Test-Path $data) { Remove-Item $data -Recurse -Force }

Write-Host "[2] Preparing mock.mdb + config (sidecar attaches to cmd)" -ForegroundColor Cyan
& $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'sim.ps1') -Action setup -Target cmd -StubPort $StubPort | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "setup failed" -ForegroundColor Red; exit 1 }

Write-Host "[3] Starting Aquadat stub (localhost:$StubPort, minimized)" -ForegroundColor Cyan
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -like '*aquadat-stub*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
# หน้าต่าง stub เป็นของ powershell ไม่ใช่ cmd จึงไม่แย่งเป็นเป้าหมายของ sidecar
Start-Process $ps -WindowStyle Minimized -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $here 'aquadat-stub.ps1'),'-Port',"$StubPort"

Write-Host "[4] Starting sidecar + watchdog" -ForegroundColor Cyan
Start-Sidecar
exit 0
