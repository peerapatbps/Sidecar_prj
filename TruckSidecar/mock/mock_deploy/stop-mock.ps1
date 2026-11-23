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
Stop-SidecarProcs
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -like '*aquadat-stub*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host 'Stopped watchdog, sidecar and stub'
