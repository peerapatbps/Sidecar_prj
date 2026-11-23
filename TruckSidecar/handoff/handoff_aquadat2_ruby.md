# Handoff — TruckSidecar ส่งยอดเข้า Aquadat2 (branch `Ruby`)

วันที่: 2026-11-23 · Repo: `Sidecar_prj` → https://github.com/peerapatbps/Sidecar_prj (`master` = baseline `2b68867`) · Branch: `Ruby`
อ้างอิงสเปก: Uroboros branch `Ruby` (`C:\Users\peera\OneDrive\Desktop\Uroboros\Uroboros\Uroboros`) →
`handoff/Aquadat2/01_login_flow.md`, `03_trans_update.md`

> ## ⚠️ REMARK: ต้องเทสหน้างาน (เครื่องโรงงาน) ก่อนถือว่าเสร็จ
> โค้ดนี้ผ่านแค่ build + stub ในเครื่อง dev — **ยังไม่เคย login AD หรือส่งยอดจริงเข้า aquadat2 PROD จาก Sidecar**
> ดู checklist "เทสหน้างาน" ด้านล่าง ถ้าไม่ผ่าน → ตั้ง `"aquadatApi": 1` ใน `D:\Sidecar_prj\TruckSidecar\config.json` เพื่อกลับไปใช้ระบบเดิมได้ทันที

## สถานะ deploy (2026-11-23)
- Publish Release (x86 single-file) แล้วคัดลอก `TruckSidecar.exe`, `.pdb`, `config.json` ไปที่
  `Sidecar_prj\Sidecar_prj_deploy\TruckSidecar\` (`data\` เดิมไม่ถูกแตะ)
- ของเดิมสำรองไว้ที่ `Sidecar_prj\_backup\deploy_TruckSidecar_20261123-121619_pre_aquadat2\` (rollback = คัดลอก 3 ไฟล์นี้กลับ)
- โปรเจกต์อื่น (TruckLauncher / TruckWatchdog / Uroboros_mini) ไม่มีการเปลี่ยนแปลง ไม่ได้ re-publish
- config.json ที่ deploy เดิม = master ทุกตัวอักษร จึงแทนที่ได้โดยไม่เสียค่าที่ปรับหน้างาน
- ยังไม่ได้นำไปลงเครื่องโรงงาน (`D:\Sidecar_prj`) — ใช้ `deploy.ps1` บนเครื่องปลายทางตามปกติ

## เทสหน้างาน (checklist)
1. [ ] เครื่องโรงงานเปิด `https://aquadat2.mwa.co.th` และ `https://madfed.mwa.co.th/adfs/ls/` ได้ (เครือข่าย/proxy)
2. [ ] แท็บ "ส่งยอดสารเคมี" แสดง "Aquadat2" → login ด้วยรหัสพนักงาน + รหัสผ่าน AD **ครั้งเดียว** (ห้ามลองหลายรอบ — ล็อกบัญชี AD)
3. [ ] เลือกวันที่มียอดน้อย/แก้กลับได้ → ยืนยันส่ง → ได้ "บันทึกสำเร็จ [n] รายการ"
4. [ ] เปิดหน้าเว็บ aquadat2 ตรวจ configID 8036/8037/8423/8424 ว่ายอดลง **ช่อง 24:00 ของวันที่ถูก** (ไม่เลื่อนวัน)
5. [ ] ถ้ายอดไปผิดช่อง/ผิดวัน → แก้ `Aquadat2Client.BuildPayload` (`date`/`time`) แล้ว deploy ใหม่; ระหว่างนั้นตั้ง `aquadatApi: 1`
6. [ ] ทิ้งไว้ > 12 ชม. แล้วส่งอีกรอบ → ควร refresh token เองหรือขึ้นให้ login ใหม่ ไม่ค้าง

## ทำอะไรไป
ฟีเจอร์ "ส่ง Chemical income รายวัน" ย้ายจาก aquadat เดิม → **aquadat2** (เฉพาะฝั่ง **เขียน**; Sidecar ไม่อ่านข้อมูลจาก Aquadat
— ระดับถังมาจาก `/api/chem/summary` เหมือนเดิม จึงไม่ติดเรื่อง REST read ที่ Feb ยังไม่ออก doc)

| | เดิม (v1) | ใหม่ (v2, ค่าเริ่มต้น) |
|---|---|---|
| Login | `POST :12007/api/aquaDATService/Enroll` | ADFS SAML (HTTP ล้วน) → `/api/Authen/Saml/loginbypass` → JWT + refreshToken |
| ชื่อผู้ใช้ | `00102616` | `water.mwa\00102616` (เติมโดเมนให้อัตโนมัติ) |
| ส่งยอด | GraphQL `update_transaction_datavalues` | `POST /apibackend/api/Trans/PTransJsonUpdate` `{transUpdateItems:[…], apiPlantId:0}` |
| `date` | `yyyy-MM-dd` | `yyyy-MM-ddT00:00:00` (`time` ยังเป็น `"24:00"`) |
| สำเร็จเมื่อ | `return_status != false` | `success == true` และ `data.statusCode == "C"` |
| token หมดอายุ | ล้าง token ให้ login ใหม่ | refresh อัตโนมัติ 1 ครั้ง (`verify-refresh-token`) ไม่ได้ → ให้ login ใหม่ |

Login ยังเป็นแบบ**ผู้ใช้กรอกเอง ครั้งเดียว** ไม่มีวน/เดารหัส (สำคัญ: aquadat2 ผูก AD — กรอกผิดนับรวมล็อกบัญชี)
รหัสผ่าน/token อยู่ในหน่วยความจำเท่านั้น

## ไฟล์
- ใหม่ `Aquadat2.cs` — `Aquadat2Login` (SAML), `Aquadat2Session` (Bearer + refresh), `Aquadat2Client` (PTransJsonUpdate)
- ใหม่ `Aq2StubTest.cs` — `--aq2test <base> <user> <pass> <out>` ทดสอบครบวง **ปฏิเสธ host ที่ไม่ใช่ localhost**
- แก้ `Config.cs` / `config.json` — `aquadatApi` (2=ใหม่, 1=เดิม), `aquadat2BaseUrl`, `aquadat2Domain`
- แก้ `SidecarController.cs` — login/push แยกตาม `aquadatApi`, `AqLogout()`
- แก้ `Ui/AquadatPanel.cs` — หัวข้อ/ปุ่มเป็น "Aquadat2" + คำเตือนบัญชี AD
- แก้ `AqSelfTest.cs` — `--aqtest` พิมพ์ payload v2
- แก้ `mock/aquadat-stub.ps1` — จำลอง aquadat2 ครบทุก hop (v1 ยังใช้ได้)

## ผลทดสอบ (2026-11-23)
- `dotnet build` 0 error
- `--aq2test` กับ stub: รหัสผิด → ปฏิเสธพร้อมข้อความ ADFS ✓, login ✓ (refresh token ✓), ส่ง 2 configID ✓,
  401 → refresh → ส่งซ้ำสำเร็จ ✓, `success:false` → throw ✓; ชี้ PROD → ปฏิเสธ ✓
- `--preview` แท็บส่งยอดแสดง "Aquadat2" + คำเตือน ✓

## ยังไม่ได้ทำ / ต้องยืนยัน
- [ ] **ยังไม่เคยส่งจริงเข้า PROD จาก Sidecar** — Uroboros พิสูจน์ payload แบบ `time:"08:00"` ไว้แล้ว แต่ `time:"24:00"`
      (ยอดตัดรายวัน) + `date` แบบ `T00:00:00` ยังไม่ยืนยันบน aquadat2 → ทดสอบส่ง 1 วันที่แก้กลับได้ แล้วเช็คหน้าเว็บ aquadat2
      ว่ายอดลงช่อง 24:00 ของวันที่ถูกต้อง (ไม่เลื่อนไปวันถัดไป)
- [ ] บัญชีที่ใช้ส่งต้องมีสิทธิ์เขียน configID 8036/8037/8423/8424 ใน aquadat2
- [ ] เครื่องโรงงานต้องเข้าถึง `aquadat2.mwa.co.th` และ `madfed.mwa.co.th` (ADFS) ได้
- [ ] ถ้ามีปัญหาหน้างาน: ตั้ง `"aquadatApi": 1` ใน config.json เพื่อกลับไปใช้ระบบเดิม (จนกว่า กปน. ปิด v1)

## Suggested skills สำหรับ session ถัดไป
- **`run`** — เปิด sidecar / `--aq2test` กับ stub เพื่อดูว่าทำงานจริงหลังแก้
- **`code-review`** (level `medium`) — ก่อน merge `Ruby` → `master` (ดู `Aquadat2.cs`, การจัดการ session/refresh)
- **`handoff:handoff`** — อัปเดตไฟล์นี้หลังได้ผลเทสหน้างาน

## ทดสอบซ้ำ
```powershell
powershell -ExecutionPolicy Bypass -File mock\aquadat-stub.ps1 -Port 18099     # เทอร์มินอลแยก
$env:__COMPAT_LAYER='RunAsInvoker'                                                # exe ขอ admin ตาม manifest
.\bin\Debug\net9.0-windows\win-x86\TruckSidecar.exe --aq2test http://localhost:18099 00102616 any out.txt
```
