@echo off
rem คอมไพล์ cmd-mock.exe (ไอคอน truck1.ico ดึงจาก TRUCK93A\1.exe) → ..\mock_deploy\cmd-mock.exe
cd /d "%~dp0"
"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /platform:anycpu /codepage:65001 /win32icon:truck1.ico /out:..\mock_deploy\cmd-mock.exe CmdMockLauncher.cs
