@echo off
rem === mock: this cmd window = the weighing program (sidecar attaches to it) + scale data injector ===
rem Double-click cmd-mock.exe (or this file). Keep this file pure ASCII and keep console output English:
rem cmd misreads UTF-8 batch files on Thai Windows, and conhost cannot draw Thai vowel/tone marks.
cd /d "%~dp0"
if /i "%~1"=="menu" goto menu

rem sidecar/watchdog are requireAdministrator -> run the whole mock elevated (elevate.ps1: built-in
rem Administrator account, password asked once per machine, so no UAC prompt every time)
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0elevate.ps1"
  exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0cmd-mock.ps1"
if errorlevel 1 (
  pause
  exit /b 1
)
rem force conhost: the window must belong to cmd.exe (inside Windows Terminal the sidecar cannot find it)
start "" "%SystemRoot%\System32\conhost.exe" cmd.exe /c ""%~f0" menu"
exit /b 0

:menu
mode con cols=100 lines=40
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0menu.ps1"
exit
