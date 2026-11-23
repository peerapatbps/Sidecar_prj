# สคริปต์เดียวจบ — เตรียม + เปิด stub + Notepad + sidecar + จำลองรถชั่งเข้า 1 คัน
# ใช้:  powershell -ExecutionPolicy Bypass -File mock\start-mock.ps1
#      -Rebuild     : publish exe ใหม่ก่อน
#      -Truck/-Ticket/-W1 : กำหนดรถคันแรก (ดีฟอลต์ 70-9999 / 90001 / 25000)
#      -StubPort    : พอร์ต stub (ดีฟอลต์ 18080)
param(
  [switch]$Rebuild,
  [string]$Truck = '70-9999',
  [string]$Ticket = '90001',
  [double]$W1 = 25000,
  [int]$StubPort = 18080
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$run  = Join-Path $here 'run'
$ps   = "powershell.exe"

function Step($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }

# 0) build ถ้าสั่ง หรือยังไม่มี exe
if ($Rebuild -or -not (Test-Path (Join-Path $root 'publish\TruckSidecar.exe'))) {
  Step 0 "publish exe..."
  Push-Location $root
  try { & dotnet publish -c Release -o publish -nologo -v q } finally { Pop-Location }
}

# 1) เตรียม mock.mdb + config (ผ่าน sim.ps1 ซึ่งจัดการ x86 ให้เอง)
Step 1 "เตรียม mock.mdb + config"
& $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'sim.ps1') -Action setup -StubPort $StubPort | Out-Null
Write-Host "   OK: $run"

# 2) เปิด Aquadat stub (หน้าต่างแยก ปล่อยค้าง)
Step 2 "เปิด Aquadat stub ที่ localhost:$StubPort"
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -like '*aquadat-stub*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Process $ps -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $here 'aquadat-stub.ps1'),'-Port',"$StubPort"

# 3) เปิด Notepad (แทนโปรแกรมชั่ง) — ใช้ full path ให้เกิด process ชื่อ "notepad" ที่ detect ได้ชัวร์
#    (Start-Process notepad แบบ alias บน Win11 บางเครื่องไม่สร้าง process ที่ GetProcessesByName เห็น)
Step 3 "เปิด Notepad (แทนโปรแกรมชั่ง)"
Start-Process "$env:SystemRoot\System32\notepad.exe"
Start-Sleep -Seconds 2
if (-not (Get-Process notepad -ErrorAction SilentlyContinue)) {
  Write-Host "   เตือน: ยังไม่พบ process 'notepad' — watchdog/sidecar จะคิดว่าโปรแกรมชั่งไม่ทำงาน" -ForegroundColor Yellow
}

# 4) เปิด sidecar — ปิด instance เก่าก่อน (single-instance mutex กันเปิดซ้ำ)
Step 4 "ปิด TruckSidecar เก่า (ถ้ามี) แล้วเปิดใหม่"
Get-Process TruckSidecar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Start-Process (Join-Path $run 'TruckSidecar.exe') -WorkingDirectory $run
Start-Sleep -Milliseconds 1200

# 4b) เปิด watchdog — เฝ้าเปิด sidecar กลับถ้าหลุด ขณะ "โปรแกรมชั่ง" (Notepad) ยังเปิดอยู่
Step '4b' "ปิด TruckWatchdog เก่า (ถ้ามี) แล้วเปิดใหม่"
Get-Process TruckWatchdog -ErrorAction SilentlyContinue | Stop-Process -Force
$wdSrc = Join-Path (Split-Path $root -Parent) 'TruckWatchdog\publish\TruckWatchdog.exe'
if (Test-Path $wdSrc) {
  Copy-Item $wdSrc $run -Force
  $wd = [ordered]@{
    truckProcess        = 'notepad'                       # ตรงกับ targetProcess ของ sidecar ใน mock
    sidecarProcess      = 'TruckSidecar'
    sidecarExe          = (Join-Path $run 'TruckSidecar.exe')
    stopFlagPath        = ''                              # เว้นว่าง = อ่าน dataDir จาก run\config.json เอง
    pollSeconds         = 3
    startupGraceSeconds = 30
  }
  ($wd | ConvertTo-Json -Depth 3) | Set-Content (Join-Path $run 'watchdog.json') -Encoding utf8
  Start-Process (Join-Path $run 'TruckWatchdog.exe') -WorkingDirectory $run
  Write-Host "   watchdog เปิดแล้ว (ลอง 'ปิด sidecar' ดู แล้วมันจะเด้งกลับใน ~3 วิ)"
} else {
  Write-Host "   ข้าม watchdog: ไม่พบ $wdSrc (สั่ง 'dotnet publish' ที่ TruckWatchdog ก่อน)" -ForegroundColor Yellow
}
Start-Sleep -Milliseconds 800

# 5) จำลองรถชั่งเข้า 1 คัน
Step 5 "จำลองรถชั่งเข้า: $Truck ตั๋ว $Ticket W1=$W1"
& $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'sim.ps1') -Action weighin -Truck $Truck -Ticket $Ticket -W1 $W1

Write-Host "`n=== พร้อมทดสอบ ===" -ForegroundColor Green
Write-Host "ในโปรแกรม:  กด 'ดึงจากตาชั่ง' -> เลือกตั๋ว $Ticket -> กรอกฟอร์ม (ถ.พ./อุณหภูมิ/ใบนำส่ง/ถัง) -> บันทึก"
Write-Host "จำลองชั่งออก (คัดลอกไปวางในอีกเทอร์มินอล):"
Write-Host "  powershell -ExecutionPolicy Bypass -File mock\sim.ps1 -Action weighout -Ticket $Ticket -W2 11000 -Ticket2 80001 -W1 $W1" -ForegroundColor Yellow
Write-Host "จากนั้น: ใส่ระดับหลังเติม -> บันทึก -> login (รหัสอะไรก็ได้) -> 'ส่ง Chemical income'"
Write-Host "payload ที่ส่งดูได้ที่: $here\received.log"
