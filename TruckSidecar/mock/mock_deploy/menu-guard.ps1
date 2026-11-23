# เฝ้าหน้าต่างเมนู (menu.ps1): หน้าต่างปิด (กด 0 หรือกดกากบาท) → ปิด sidecar / watchdog / stub
# ปิดด้วยกากบาทไม่มี event ให้ดัก จึงเป็น process ซ่อนแยกที่รอ PID ของเมนูจบแล้วค่อยเก็บกวาด
# menu.ps1 เปิดให้เอง — ไม่ต้องรันเอง
param([Parameter(Mandatory)][int]$WaitPid)

# ตัวเฝ้าค้างจากรอบก่อน → ปิดทิ้ง ให้เหลือตัวเดียว
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*menu-guard*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Wait-Process -Id $WaitPid -ErrorAction SilentlyContinue
& (Join-Path $PSScriptRoot 'stop-mock.ps1')
