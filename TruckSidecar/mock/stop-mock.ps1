# sidecar/watchdog เป็น requireAdministrator → หน้าต่างที่ไม่ได้ elevate ปิดไม่ได้ (Access is denied)
# ปิดเองก่อน ถ้าโดนปฏิเสธ → ขอสิทธิ์ admin (UAC) แล้วปิดแทน; watchdog "ก่อน" ไม่งั้นมันเด้ง sidecar กลับ
function Stop-SidecarProcs {
  $names = 'TruckWatchdog', 'TruckSidecar'
  $denied = $false
  foreach ($n in $names) {
    foreach ($p in @(Get-Process -Name $n -ErrorAction SilentlyContinue)) {
      try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { $denied = $true }
    }
  }
  if ($denied) {
    Write-Host "   ต้องใช้สิทธิ์ admin เพื่อปิด sidecar/watchdog — กด Yes ที่หน้าต่าง UAC" -ForegroundColor Yellow
    try {
      Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command',
        "Stop-Process -Name TruckWatchdog -Force -ErrorAction SilentlyContinue; Stop-Process -Name TruckSidecar -Force -ErrorAction SilentlyContinue"
    } catch { }
    if (Get-Process -Name $names -ErrorAction SilentlyContinue) { throw "ปิด sidecar/watchdog ไม่ได้ (ไม่ได้สิทธิ์ admin)" }
  }
}
Stop-SidecarProcs
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -like '*aquadat-stub*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host 'ปิด watchdog, sidecar และ stub แล้ว (Notepad ปล่อยให้คุณปิดเอง)'
