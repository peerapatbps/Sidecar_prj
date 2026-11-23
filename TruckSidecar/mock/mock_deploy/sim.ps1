# ตัวจำลองตาชั่ง + config สำหรับ mock test
#   setup    : สร้าง mock\run\ (exe + mock.mdb ว่าง + config ชี้ไป stub)
#   weighin  : จำลองรถชั่งเข้า (STAT=1)         -Truck 70-9999 -Ticket 90001 -W1 25000 -Product 03
#              Product: 01 = ALUM, 03 = PACL, 02 = ทั่วไป (sidecar ไม่สนใจ)
#   weighout : จำลองรถชั่งออก (STAT=2)          -Ticket 90001 -W2 11000 -Ticket2 80001
#   reset    : ล้างแถวทั้งหมดใน mock.mdb
#   randin   : ชั่งเข้ารถทะเบียนสุ่ม (ตั๋วจากเวลา ไม่ซ้ำ) -Product 01/03 (ไม่ใส่ = สุ่ม ALUM/PACL)
#   randout  : ชั่งออกรถที่ค้าง W1 (STAT=1) แบบสุ่ม 1 คัน (เฉพาะเที่ยวรับสาร)
#   washin   : ชั่งเข้าเที่ยวล้างถัง — รถกระบะเปล่า W1 ~1 ตัน (0.9–1.2) (สุ่ม ALUM/PACL เหมือน randin)
#   washout  : ชั่งออกเที่ยวล้างถัง — W2 ~2.5 ตัน (2.3–2.7) (นำสารออก → W2 > W1)
#   list     : แสดงรถที่ค้าง W1 (STAT=1)
#   เที่ยวล้างถังทำเครื่องหมาย SUBCON='9' (sidecar ไม่อ่าน) ไว้แยกคิว randout / washout เท่านั้น
#   setup -Target cmd : ให้ sidecar เกาะหน้าต่าง cmd แทน Notepad (ใช้กับ cmd-mock.bat)
# Jet เป็น x86 สคริปต์จะรีสตาร์ตตัวเองใน PowerShell x86 อัตโนมัติ
param(
  [Parameter(Mandatory)][ValidateSet('setup','weighin','weighout','reset','randin','randout','washin','washout','list')][string]$Action,
  [string]$Truck = '70-9999',
  [string]$Ticket = '90001',
  [string]$Ticket2 = '80001',
  [double]$W1 = 25000,
  [double]$W2 = 11000,
  [ValidateSet('01','02','03')][string]$Product = '03',
  [int]$StubPort = 18080,
  [string]$Target = 'notepad',   # setup: process ที่ sidecar เกาะ (notepad / cmd)
  [int]$DaysAgo = 0          # weighin/weighout ย้อนวัน (ทดสอบงานคงค้างข้ามวัน / ตัดยอดตามวันชั่งเข้า)
)

# --- รีสตาร์ตใน x86 ถ้าจำเป็น (Jet OLEDB มีเฉพาะ 32 บิต) ---
if ([Environment]::Is64BitProcess) {
  $ps86 = "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
  $a = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath)
  foreach ($k in $PSBoundParameters.Keys) { $a += "-$k"; $a += "$($PSBoundParameters[$k])" }
  & $ps86 @a
  exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$run    = Join-Path $PSScriptRoot 'run'
$mdb    = Join-Path $run 'mock.mdb'
$realDb = Join-Path $PSScriptRoot 'bin\mock-template.mdb'   # โครงสร้าง data.mdb ของโปรแกรมชั่ง (WDATA ว่าง)
$exeSrc = Join-Path $PSScriptRoot 'bin\TruckSidecar.exe'
$pad    = { param($t) ([string]$t).PadLeft(10,'0') }   # TICKET เก็บเป็น 10 หลักเติมศูนย์

function Conn { $c = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$mdb"); $c.Open(); $c }
function Query($sql) {
  $c = Conn; try { $da = New-Object System.Data.OleDb.OleDbDataAdapter($sql, $c); $t = New-Object System.Data.DataTable; [void]$da.Fill($t); ,$t }
  finally { $c.Close() }
}
# ตั๋วจากเวลา (prefix 9 = เข้า, 8 = ออก) → ไม่ซ้ำกับ record เก่าใน sidecar แม้ล้าง mdb แล้ว; ชนในวินาทีเดียวกัน → +1
function NewTicket($prefix, $col) {
  $n = [long]($prefix + (Get-Date).ToString('ddHHmmss'))
  while ((Query "SELECT COUNT(*) AS N FROM WDATA WHERE $col='$(& $pad $n)'").Rows[0].N -gt 0) { $n++ }
  & $pad $n
}
$chemName = @{ '01' = 'ALUM'; '02' = 'GENERAL'; '03' = 'PACL' }
$ProductGiven = $PSBoundParameters.ContainsKey('Product')
$washMark = '9'   # SUBCON ของเที่ยวล้างถัง
function RandIn([bool]$wash) {
  $prod = if ($script:ProductGiven) { $Product } else { @('01','03') | Get-Random }
  $truck = "{0}-{1}" -f (Get-Random -Minimum 60 -Maximum 90), (Get-Random -Minimum 1000 -Maximum 10000)
  $w = if ($wash) { Get-Random -Minimum 900 -Maximum 1200 } else { Get-Random -Minimum 22000 -Maximum 32000 }
  $w = [math]::Round($w / 10) * 10
  $sub = if ($wash) { $washMark } else { '0' }
  $t1 = NewTicket '9' 'TICKET1'
  [void](Exec "INSERT INTO WDATA (STAT,TRUCK,CARTYPE,COMPANY,PRODUCT,SUBCON,TICKET1,DAYIN,TMIN,W1,RATE) VALUES ('1','$truck','1','05','$prod','$sub','$t1',#$today#,'$now',$w,1)")
  Write-Host ("Weigh-in (W1){0}: {1}  ticket {2}  W1={3:#,0} kg  {4}" -f $(if ($wash) { ' tank-wash' } else { '' }), $truck, $t1.TrimStart('0'), $w, $chemName[$prod])
}
function RandOut([bool]$wash) {
  $cond = if ($wash) { "SUBCON='$washMark'" } else { "(SUBCON IS NULL OR SUBCON<>'$washMark')" }
  $rows = Query "SELECT TRUCK, TICKET1, W1, PRODUCT FROM WDATA WHERE STAT='1' AND $cond"
  if ($rows.Rows.Count -eq 0) { Write-Host $(if ($wash) { "No tank-wash truck waiting at W1 - weigh in first (menu 5)" } else { "No truck waiting at W1 - weigh in first (menu 2)" }); return }
  $r = $rows.Rows | Get-Random
  $w1 = [double]$r.W1
  $w2 = if ($wash) { Get-Random -Minimum 2300 -Maximum 2700 } else { $w1 - (Get-Random -Minimum 8000 -Maximum 16000) }
  $w2 = [math]::Round($w2 / 10) * 10
  $t1 = [string]$r.TICKET1; $t2 = NewTicket '8' 'TICKET2'
  [void](Exec "UPDATE WDATA SET STAT='2', TICKET2='$t2', DAYOUT=#$today#, TMOUT='$now', W2=$w2 WHERE TICKET1='$t1' AND STAT='1'")
  $net = if ($wash) { "taken out {0:#,0}" -f ($w2 - $w1) } else { "net {0:#,0}" -f ($w1 - $w2) }
  Write-Host ("Weigh-out (W2){0}: {1}  ticket {2} -> {3}  W1={4:#,0} W2={5:#,0} {6} kg  {7}" -f $(if ($wash) { ' tank-wash' } else { '' }), $r.TRUCK, $t1.TrimStart('0'), $t2.TrimStart('0'), $w1, $w2, $net, $chemName[[string]$r.PRODUCT])
  Write-Host ("{1} {0} truck(s) still waiting at W1" -f $(if ($wash) { 'tank-wash' } else { 'delivery' }), ($rows.Rows.Count - 1))
}
function Exec($sql) {
  $c = Conn; try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.ExecuteNonQuery() }
  finally { $c.Close() }
}
$today = (Get-Date).AddDays(-$DaysAgo).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)     # Jet literal date = #yyyy-MM-dd#
$now   = (Get-Date).ToString('HH:mm:ss')

switch ($Action) {
  'setup' {
    New-Item -ItemType Directory -Force $run | Out-Null
    Copy-Item $exeSrc $run -Force
    Copy-Item $realDb $mdb -Force            # ก็อปโครงสร้างจริงมา แล้วล้างแถว
    [void](Exec "DELETE FROM WDATA")
    $cfg = [ordered]@{
      targetProcess = $Target               # ใช้ Notepad / cmd แทนโปรแกรมชั่งจริง
      mdbPath = $mdb
      scalePollSeconds = 5
      watchSeconds = 3
      forceCheckSeconds = 30
      inWindowHours = 24
      productMap = @{ '01' = 'ALUM'; '03' = 'PACL' }
      glass = 'blur'
      apiUrl = 'http://172.16.198.150:443/api/chem/summary'
      apiPollSeconds = 60
      centralApiUrl = ''
      aquadatUrl = "http://localhost:$StubPort/graphql"
      aquadatEnrollUrl = "http://localhost:$StubPort/api/aquaDATService/Enroll"
      aquadatUsername = '00102616'
      aquadatPushSeconds = 30
      dataDir = 'data'
      sidecarWidth = 400
      lowLevelPercent = 20
      # อัตราจ่ายสารจริง = flow × dose × factor (ดู config.json หลัก)
      phaseFlow = @(
        @{phase=1;id=1;divisor=1}
        @{phase=2;id=2;divisor=1}
        @{phase=3;id=39;divisor=3600}
        @{phase=4;id=40;divisor=3600}
      )
      dosing = [ordered]@{
        ALUM = @{factor=5.4135;doseIds=[ordered]@{'1'=89;'2'=92;'3'=151;'4'=154}}
        PACL = @{factor=8.6434;doseIds=[ordered]@{'1'=8402;'2'=8404;'3'=8416;'4'=8418}}
      }
      tanks = @(
        @{name='Alum A';chemical='ALUM';levelId=93;statusId=95;capacity=750;incomeConfigId=8037;phases=@(1,2)}
        @{name='Alum B';chemical='ALUM';levelId=96;statusId=98;capacity=750;incomeConfigId=8037;phases=@(1,2)}
        @{name='Alum C';chemical='ALUM';levelId=155;statusId=157;capacity=750;incomeConfigId=8036;phases=@(3,4)}
        @{name='Alum D';chemical='ALUM';levelId=158;statusId=160;capacity=750;incomeConfigId=8036;phases=@(3,4)}
        @{name='PACL A';chemical='PACL';levelId=8405;statusId=8406;capacity=25;incomeConfigId=8423;phases=@(1,2)}
        @{name='PACL B';chemical='PACL';levelId=8407;statusId=8408;capacity=25;incomeConfigId=8423;phases=@(1,2)}
        @{name='PACL C';chemical='PACL';levelId=8419;statusId=8420;capacity=25;incomeConfigId=8424;phases=@(3,4)}
        @{name='PACL D';chemical='PACL';levelId=8421;statusId=8422;capacity=23;incomeConfigId=8424;phases=@(3,4)}
      )
    }
    $cfgPath = Join-Path $run 'config.json'
    if (Test-Path -LiteralPath $cfgPath) { Remove-Item -LiteralPath $cfgPath -Force -ErrorAction Stop }   # Set-Content พังถ้าไฟล์เดิมอ่านไม่ได้
    [IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Ready: $run"
    Write-Host "  1) start the stub:  powershell -ExecutionPolicy Bypass -File aquadat-stub.ps1"
    Write-Host "  2) open the target window, then run $run\TruckSidecar.exe"
    Write-Host "  3) sim weighin -> pull from scale in sidecar -> fill form -> sim weighout -> login -> send"
  }
  'weighin' {
    $t1 = & $pad $Ticket
    [void](Exec "INSERT INTO WDATA (STAT,TRUCK,CARTYPE,COMPANY,PRODUCT,SUBCON,TICKET1,DAYIN,TMIN,W1,RATE) VALUES ('1','$Truck','1','05','$Product','0','$t1',#$today#,'$now',$W1,1)")
    Write-Host "Weigh-in: $Truck ticket $t1 W1=$W1 PRODUCT=$Product (STAT=1)"
  }
  'weighout' {
    $t1 = & $pad $Ticket; $t2 = & $pad $Ticket2
    $n = Exec "UPDATE WDATA SET STAT='2', TICKET2='$t2', DAYOUT=#$today#, TMOUT='$now', W2=$W2 WHERE TICKET1='$t1' AND STAT='1'"
    if ($n -gt 0) { Write-Host "Weigh-out: ticket $t1 -> $t2  W2=$W2 net $([math]::Abs($W1-$W2)) (STAT=2)" }
    else { Write-Host "Ticket $t1 with STAT=1 not found" }
  }
  'reset' { [void](Exec "DELETE FROM WDATA"); Write-Host "mock.mdb cleared" }
  'randin'  { RandIn $false }
  'randout' { RandOut $false }
  'washin'  { RandIn $true }
  'washout' { RandOut $true }
  'list' {
    $rows = Query "SELECT TRUCK, TICKET1, W1, PRODUCT, SUBCON, TMIN FROM WDATA WHERE STAT='1' ORDER BY TICKET1"
    Write-Host ("Trucks waiting at W1 (not weighed out): {0}" -f $rows.Rows.Count)
    foreach ($r in $rows.Rows) {
      $k = if ([string]$r.SUBCON -eq $washMark) { 'tank-wash' } else { 'delivery ' }
      Write-Host ("  {0,-9} ticket {1}  W1={2:#,0}  {3}  {4}  in {5}" -f $r.TRUCK, ([string]$r.TICKET1).TrimStart('0'), [double]$r.W1, $k, $chemName[[string]$r.PRODUCT], $r.TMIN)
    }
  }
}
