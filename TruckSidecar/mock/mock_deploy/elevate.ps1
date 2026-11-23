# เปิด cmd-mock.bat ด้วยสิทธิ์ admin โดยไม่ต้องตอบ UAC ทุกครั้ง
# ใช้บัญชี Administrator ในตัวของเครื่อง (ค่าเริ่มต้น Windows ไม่กรองสิทธิ์บัญชีนี้ → ได้ admin เต็มโดยไม่มี UAC)
# รหัสผ่าน: ถามครั้งแรกครั้งเดียวต่อเครื่อง แล้วเก็บแบบเข้ารหัส DPAPI ที่ %LOCALAPPDATA%\TruckSidecarMock\admin.cred
#   ถอดได้เฉพาะ user นี้บนเครื่องนี้ — ในโฟลเดอร์ mock_deploy ไม่มีรหัสผ่าน ก็อปไปเครื่องอื่นต้องกรอกใหม่ 1 ครั้ง
# ใช้ไม่ได้ (รหัสผิด / บัญชีถูกปิด) → ลบที่จำไว้ แล้วขอผ่าน UAC แทน
#   -Forget : ลบรหัสผ่านที่จำไว้ (เช่น เปลี่ยนรหัส Administrator)
param([switch]$Forget)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$bat  = Join-Path $PSScriptRoot 'cmd-mock.bat'
$dir  = Join-Path $env:LOCALAPPDATA 'TruckSidecarMock'
$file = Join-Path $dir 'admin.cred'
function Say([string]$m) { [void][System.Windows.Forms.MessageBox]::Show($m, 'ตาชั่งจำลอง') }

if ($Forget) {
  if (Test-Path $file) { Remove-Item $file -Force }
  Say 'ลบรหัสผ่าน Administrator ที่จำไว้แล้ว'
  exit 0
}

# admin.cred = 2 บรรทัด: ชื่อผู้ใช้ / รหัสผ่านที่เข้ารหัส DPAPI
$cred = $null
if (Test-Path $file) {
  try {
    $l = @(Get-Content $file)
    $cred = New-Object System.Management.Automation.PSCredential($l[0], ($l[1] | ConvertTo-SecureString))
  } catch { $cred = $null }
}
$fresh = $false
if (-not $cred) {
  $cred = Get-Credential -UserName "$env:COMPUTERNAME\Administrator" -Message 'รหัสผ่านบัญชี Administrator ของเครื่องนี้ (ถามครั้งเดียว เครื่องจะจำไว้แบบเข้ารหัส)'
  if (-not $cred) { exit 1 }   # กดยกเลิก
  $fresh = $true
}

try {
  Start-Process cmd.exe -ArgumentList '/c', "`"$bat`"" -Credential $cred -WorkingDirectory $PSScriptRoot
  if ($fresh) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    @($cred.UserName, ($cred.Password | ConvertFrom-SecureString)) | Set-Content $file
  }
} catch {
  if (Test-Path $file) { Remove-Item $file -Force }
  Say ("เปิดด้วยบัญชี $($cred.UserName) ไม่ได้: $($_.Exception.Message)`n`nจะขอสิทธิ์ผ่าน UAC แทน (รอบหน้าจะถามรหัสผ่านใหม่)")
  try { Start-Process $bat -Verb RunAs -WorkingDirectory $PSScriptRoot } catch { }
}
