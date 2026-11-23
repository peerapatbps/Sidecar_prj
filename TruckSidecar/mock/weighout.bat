@echo off
chcp 65001 >nul
rem === จำลองรถชั่งออก ===
rem ใช้: weighout.bat [Ticket] [W2] [Ticket2] [W1]
rem ไม่ใส่ = ใช้ค่าเริ่มต้น ตั๋ว 90001 / W2 11000 / ตั๋วออก 80001 / W1 25000
cd /d "%~dp0"
set "TK=%~1"
set "W2=%~2"
set "TK2=%~3"
set "W1=%~4"
if "%TK%"==""  set "TK=90001"
if "%W2%"==""  set "W2=11000"
if "%TK2%"=="" set "TK2=80001"
if "%W1%"==""  set "W1=25000"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sim.ps1" -Action weighout -Ticket %TK% -W2 %W2% -Ticket2 %TK2% -W1 %W1%
echo.
pause
