# เก็บข้อมูลชุดจำลองเดิมไว้ก่อนเริ่มทดสอบรอบใหม่ (ไม่ลบอะไรจนกว่าจะคัดลอกครบ)
#   - mock\run\data\      → mock\_dump\<เวลา>\data\   (sidecar.db, outbox, audit, daycut ...)
#   - mock\run\mock.mdb   → mock\_dump\<เวลา>\mock.mdb + WDATA.csv
#   - mock\received.log   → mock\_dump\<เวลา>\received.log
# -Reset : หลังเก็บแล้ว ล้าง mock.mdb (DELETE FROM WDATA), ย้าย data\ ออก และล้าง received.log
# Jet เป็น x86 สคริปต์จะรีสตาร์ตตัวเองใน PowerShell x86 อัตโนมัติ
param([switch]$Reset)

if ([Environment]::Is64BitProcess) {
  $ps86 = "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
  $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath)
  if ($Reset) { $a += '-Reset' }
  & $ps86 @a
  exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$run  = Join-Path $PSScriptRoot 'run'
$mdb  = Join-Path $run 'mock.mdb'
$dest = Join-Path $PSScriptRoot ('_dump\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $dest | Out-Null

if (Test-Path (Join-Path $run 'data')) { Copy-Item (Join-Path $run 'data') (Join-Path $dest 'data') -Recurse }
$log = Join-Path $PSScriptRoot 'received.log'
if (Test-Path $log) { Copy-Item $log $dest }
Copy-Item $mdb $dest

$c = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$mdb")
$c.Open()
try {
  $da = New-Object System.Data.OleDb.OleDbDataAdapter("SELECT * FROM WDATA", $c)
  $t = New-Object System.Data.DataTable
  [void]$da.Fill($t)
  $t | Export-Csv (Join-Path $dest 'WDATA.csv') -NoTypeInformation -Encoding UTF8
  Write-Host "เก็บแล้ว: $dest (WDATA $($t.Rows.Count) แถว)"
  if ($Reset) {
    $cmd = $c.CreateCommand(); $cmd.CommandText = 'DELETE FROM WDATA'; [void]$cmd.ExecuteNonQuery()
  }
} finally { $c.Close() }

if ($Reset) {
  if (Test-Path (Join-Path $run 'data')) { Remove-Item (Join-Path $run 'data') -Recurse -Force }
  if (Test-Path $log) { Clear-Content $log }
  Write-Host "ล้างชุดจำลองแล้ว (mock.mdb ว่าง, data\ ใหม่, received.log ว่าง)"
}
