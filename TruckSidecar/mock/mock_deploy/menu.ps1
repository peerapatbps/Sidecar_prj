# เมนูตาชั่งจำลอง — รันใน cmd (conhost) ที่ cmd-mock.bat เปิด; sidecar เกาะหน้าต่างนี้
# (หน้าต่าง conhost ยังรายงานเจ้าของเป็น cmd.exe แม้ PowerShell นี้รันอยู่ข้างใน)
# ข้อความในคอนโซลเป็นอังกฤษ: conhost วาดสระ/วรรณยุกต์ไทยไม่ได้ (ตัวหนังสือแตก) — ภาษาไทยใช้ได้แค่ title และหน้าต่าง GUI
$here = $PSScriptRoot
$Host.UI.RawUI.WindowTitle = 'ตาชั่งจำลอง (cmd) - sidecar เกาะหน้าต่างนี้'

# ห้ามตั้งชื่อ Ps: เป็น alias ของ Get-Process (alias ชนะ function)
function Run-Script([string]$file, [string[]]$a) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here $file) @a }
function Sim([string]$action) { Run-Script 'sim.ps1' @('-Action', $action) }

# เฝ้าหน้าต่างนี้: ปิดเมื่อไร (กด 0 หรือกากบาท) → ปิด sidecar / watchdog / stub ให้เอง
Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass',
  '-File', "`"$(Join-Path $here 'menu-guard.ps1')`"", '-WaitPid', "$PID"

while ($true) {
  Write-Host ''
  Write-Host ' ======== Mock truck scale ========'
  Write-Host '  1  Clear all (mock.mdb + sidecar data)'
  Write-Host '  2  Weigh-in W1   - random plate (random ALUM/PACL)'
  Write-Host '  3  Weigh-out W2  - random truck waiting at W1'
  Write-Host '  4  List trucks waiting at W1'
  Write-Host '  5  Tank-wash W1  - empty pickup in, ~1 t'
  Write-Host '  6  Tank-wash W2  - random waiting tank-wash truck out, ~2.5 t'
  Write-Host '  0  Exit (stop sidecar / watchdog / stub + close this window)'
  switch (([string](Read-Host 'Choose')).Trim()) {
    '1' { Sim reset; Run-Script 'cmd-mock.ps1' @('-WipeSidecar') }
    '2' { Sim randin }
    '3' { Sim randout }
    '4' { Sim list }
    '5' { Sim washin }
    '6' { Sim washout }
    '0' { exit 0 }
    default { Write-Host 'Choose 0-6' }
  }
}
