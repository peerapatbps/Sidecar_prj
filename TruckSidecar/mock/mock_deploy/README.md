# mock_deploy — ตาชั่งจำลอง (cmd) + sidecar ชุดพร้อมใช้

ก็อปโฟลเดอร์นี้ทั้งโฟลเดอร์ไปเครื่องไหนก็ได้ (Windows 10/11) แล้ว **ดับเบิลคลิก `cmd-mock.exe`**
ไม่ต้องลง .NET — `TruckSidecar.exe` / `TruckWatchdog.exe` เป็น self-contained (ฝัง .NET 9 x86 ไว้ในตัว)

สิทธิ์ admin (sidecar/watchdog เป็น requireAdministrator):
- ครั้งแรกของแต่ละเครื่องจะถามรหัสผ่านบัญชี **Administrator** ของเครื่อง (ชื่อผู้ใช้กรอกไว้ให้แล้ว) → เครื่องจำไว้แบบเข้ารหัส ครั้งต่อไปไม่ถาม ไม่มี UAC
- รหัสผ่านไม่ได้อยู่ในโฟลเดอร์นี้ — เก็บที่ `%LOCALAPPDATA%\TruckSidecarMock\admin.cred` (DPAPI ถอดได้เฉพาะ user นั้นบนเครื่องนั้น)
- เปลี่ยนรหัส Administrator แล้ว: ครั้งถัดไปจะเด้งแจ้งแล้วถามใหม่เอง หรือลบเอง `powershell -ExecutionPolicy Bypass -File elevate.ps1 -Forget`
- บัญชี Administrator ถูกปิด / ใช้ไม่ได้ → ถอยไปขอผ่าน UAC แบบเดิม

| ไฟล์ | หน้าที่ |
|---|---|
| `cmd-mock.exe` | ตัวเปิด (ไอคอนเหมือน `TRUCK93A\1.exe`) → เรียก `elevate.ps1` |
| `elevate.ps1` | เปิด `cmd-mock.bat` ด้วยบัญชี Administrator ที่จำไว้ (ไม่มี UAC) |
| `cmd-mock.bat` | เปิดหน้าต่าง conhost ให้ sidecar เกาะ — ต้องเป็น **CRLF** และ **ASCII ล้วน** (ห้ามมีภาษาไทย) |
| `menu.ps1` | เมนูตาชั่งจำลองในหน้าต่างนั้น |
| `cmd-mock.ps1` | เริ่มใหม่ทุกครั้ง: ปิดของเดิม → ล้าง `run\data\` + `mock.mdb` → เปิด stub + sidecar + watchdog |
| `menu-guard.ps1` | เฝ้าหน้าต่างเมนู: ปิดเมื่อไร (กด 0 หรือกากบาท) → ปิด sidecar / watchdog / stub |
| `sim.ps1` | inject ข้อมูลตาชั่งลง `run\mock.mdb` (ชั่งเข้า/ออก/ล้างถัง/ดูค้าง/ล้าง) |
| `stop-mock.ps1` | ปิด watchdog → sidecar → stub |
| `aquadat-stub.ps1` | เซิร์ฟเวอร์ Aquadat จำลอง (localhost:18080) — payload ที่รับอยู่ใน `received.log` |
| `bin\TruckSidecar.exe`, `bin\TruckWatchdog.exe` | ตัวโปรแกรม (ก็อปไป `run\` ตอนเริ่ม) |
| `bin\mock-template.mdb` | โครงสร้าง `data.mdb` ของโปรแกรมชั่ง (ตาราง WDATA ว่าง) |

`run\` ถูกสร้างตอนเปิดครั้งแรก (config, mock.mdb, data ของ sidecar) — ลบทิ้งได้เสมอ

เมนู: `1` ล้างทั้งหมด · `2` ชั่งเข้า W1 · `3` ชั่งออก W2 · `4` ดูรถค้าง W1 · `5` ล้างถัง W1 · `6` ล้างถัง W2 · `0` ออก (ปิดทุกอย่าง)

ข้อควรรู้
- **ข้อความในหน้าต่าง cmd เป็นภาษาอังกฤษโดยตั้งใจ** — conhost วาดสระบน/ล่างและวรรณยุกต์ไทยไม่ได้ (ตัวหนังสือแตก) และ cmd อ่าน .bat ที่มีภาษาไทยเพี้ยนบน Windows ภาษาไทย; ภาษาไทยใช้ได้ที่ title และหน้าต่าง GUI (sidecar, กล่องข้อความ)
- วางโฟลเดอร์นี้ไว้นอก OneDrive บนเครื่องทดสอบ (เช่น `C:\mock_deploy`) — บัญชี Administrator อาจเปิดไฟล์ OneDrive ของ user อื่นที่ยังไม่ได้ดาวน์โหลดไม่ได้
- sidecar เกาะหน้าต่าง cmd ที่ **ใหญ่ที่สุด** — ปิดหน้าต่าง cmd (conhost) อื่นก่อน
- ต้องมี Jet OLEDB 4.0 (32 บิต) — มากับ Windows อยู่แล้ว

## อัปเดตหลัง build ใหม่ (เครื่อง dev)
- sidecar / watchdog: ก็อป `TruckSidecar\publish\TruckSidecar.exe` และ `TruckWatchdog\publish\TruckWatchdog.exe` มาทับใน `bin\`
- ไอคอน / ตัวเปิด: แก้ใน `mock\launcher\` แล้วรัน `mock\launcher\build.bat` (ได้ `cmd-mock.exe` ใหม่ในโฟลเดอร์นี้)
- `sim.ps1`, `stop-mock.ps1`, `aquadat-stub.ps1` เป็นสำเนาจาก `mock\` (ชี้ `bin\` แทน path ของเครื่อง dev) — แก้ที่ `mock\` แล้วต้องตามมาแก้ที่นี่ด้วย
