@echo off
chcp 65001 >nul
rem === เปิดชุด mock test ทั้งหมด (stub + Notepad + sidecar + จำลองรถชั่งเข้า) ===
rem ดับเบิลคลิกไฟล์นี้ได้เลย
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-mock.ps1" %*
echo.
echo กด Enter เพื่อปิดหน้าต่างนี้ (stub กับ sidecar จะยังทำงานต่อ)
pause >nul
