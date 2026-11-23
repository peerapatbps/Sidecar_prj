# TruckWatchdog

ตัวเฝ้า (watchdog) เล็กๆ ที่ **เปิด TruckSidecar กลับให้อัตโนมัติ** ถ้ามันถูกปิดโดยไม่ตั้งใจ/crash
**ขณะที่โปรแกรมชั่งเดิม (truck93a) ยังทำงานอยู่** — เพื่อบังคับให้ sidecar เปิดคู่กับตาชั่งเสมอ

## ทำงานอย่างไร
วนตรวจทุก `pollSeconds` วินาที:
- **truck93a ทำงาน + sidecar ไม่ทำงาน + ไม่มีธง `sidecar.stopped`** → เปิด sidecar ใหม่
- **truck93a ปิดแล้ว** → ลบธง แล้วจบตัวเอง (รอบถัดไป launcher จะเริ่มทุกอย่างใหม่)
- ตอนเปิดมาแล้ว truck93a ยังไม่ขึ้น → รอได้ถึง `startupGraceSeconds` ก่อนยอมแพ้

เป็น **single-instance** (mutex `TruckWatchdog.SingleInstance`) และเป็น WinExe จึงไม่มีหน้าต่าง console โผล่

## เคารพทางออกผู้ดูแลระบบ (Ctrl+Shift+F12)
เมื่อ admin กด `Ctrl+Shift+F12` เพื่อปิด sidecar โดยตั้งใจ sidecar จะเขียนธง
`<dataDir>/sidecar.stopped` ก่อนปิด → watchdog เห็นธงนี้แล้ว **จะไม่เปิด sidecar ใหม่**
sidecar จะลบธงเองทุกครั้งที่ถูกเปิดตามปกติ (ผ่าน launcher) เพื่อให้ watchdog กลับมาคุ้มครองอีกครั้ง

## watchdog.json
| key | ความหมาย | ค่า default |
|-----|----------|-------------|
| `truckProcess` | ชื่อ process โปรแกรมชั่ง (ไม่ใส่ .exe) | `truck93a` |
| `sidecarProcess` | ชื่อ process ของ sidecar | `TruckSidecar` |
| `sidecarExe` | path เต็มของ TruckSidecar.exe | — |
| `stopFlagPath` | path ธง admin-exit; **เว้นว่าง** = อ่าน `config.json` ข้าง sidecar.exe แล้วคำนวณ `dataDir/sidecar.stopped` เอง | `""` |
| `pollSeconds` | รอบตรวจ (วินาที) | `5` |
| `startupGraceSeconds` | รอ truck93a ขึ้นได้กี่วินาทีก่อนยอมแพ้ | `60` |

> ปกติ **ปล่อย `stopFlagPath` ว่างไว้** — watchdog จะซิงก์กับ `dataDir` ของ sidecar เองอัตโนมัติ
> ตั้งค่านี้เฉพาะเมื่อ `dataDir` เป็น path พิเศษที่คำนวณเองไม่ได้

## Build / Deploy
```
dotnet publish -c Release -o publish
```
ได้ single-exe x86 self-contained (เหมือน TruckSidecar/TruckLauncher)
ก็อป `TruckWatchdog.exe` + `watchdog.json` ไปวางข้าง `TruckSidecar.exe` (เช่น `C:\TruckSidecar\`)
แล้วตั้ง `watchdogExe` ใน `launcher.json` ให้ชี้มาที่ไฟล์นี้ — launcher จะเปิด watchdog ให้เองพร้อม truck + sidecar
