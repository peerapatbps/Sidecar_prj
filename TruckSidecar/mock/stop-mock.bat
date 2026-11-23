@echo off
chcp 65001 >nul
rem === ปิดทุกอย่างของ mock test (sidecar + stub) ===
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop-mock.ps1"
echo.
pause
