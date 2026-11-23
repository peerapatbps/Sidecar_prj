# Handoff 23/11/2026 — สรุปงานวันนี้ (TruckSidecar → Aquadat2)

> **สถานะ:** โค้ดเสร็จ ทดสอบบนเครื่อง dev กับ stub ผ่าน deploy ลง `Sidecar_prj_deploy` และ push แล้ว
> **⚠️ ยังไม่ได้เทสหน้างาน** — ยังไม่เคย login AD หรือส่งยอดจริงเข้า aquadat2 PROD จาก Sidecar
> **ยังไม่ได้ลงเครื่องโรงงาน** (`D:\Sidecar_prj`)

รายละเอียดเชิงเทคนิค (ขั้นตอน login, payload, checklist เทสหน้างาน) อยู่ใน [handoff_aquadat2_ruby.md](handoff_aquadat2_ruby.md)
รายงานสรุปสำหรับทีม/ผู้บริหาร: [../docs/TruckSidecar_Aquadat2_Report_2026-11-23.docx](../docs/TruckSidecar_Aquadat2_Report_2026-11-23.docx)
ไฟล์นี้สรุปแค่ "วันนี้ทำอะไร ค้างอะไร" — ไม่ซ้ำเนื้อหาข้างต้น

## ที่มา
กปน. ย้าย Aquadat เป็น v2 (aquadat2.mwa.co.th): login เป็น AD ผ่าน ADFS SAML และการเขียนข้อมูลเปลี่ยนจาก GraphQL เป็น REST
ฝั่งเขียนแกะเสร็จแล้วในโปรเจกต์ Uroboros branch `Ruby` (`C:\Users\peera\OneDrive\Desktop\Uroboros\Uroboros\Uroboros\handoff\Aquadat2`)
ส่วน REST ฝั่งอ่าน Fab ยังไม่ออก doc ซึ่ง Sidecar ไม่ได้ใช้ (ระดับถังอ่านจาก `/api/chem/summary`) จึงไม่ติด

## ทำอะไรไปวันนี้ (ตามลำดับ)
1. **ตั้ง git ให้ Sidecar_prj ครั้งแรก** — เดิมไม่มี repo
   - `.gitignore` กัน bin/obj/publish, zip, `_backup`, `Sidecar_prj_deploy`, data/*.db/*.log/*.mdb, `*.ini`
   - `master` = baseline `2b68867` → แตก `Ruby`
   - remote: https://github.com/peerapatbps/Sidecar_prj (push ทั้ง `master` และ `Ruby`)
2. **`86cf4f8` — ส่งยอดเข้า Aquadat2** (พอร์ตจาก Uroboros: `Aquadat2LoginProbe` / `Aquadat2Session` / `Aquadat2WriteCollector`)
   - `Aquadat2.cs` ใหม่: login ADFS (ผู้ใช้กรอกเอง ครั้งเดียว ไม่วนรหัส), refresh token, POST `PTransJsonUpdate`
   - config `aquadatApi` (2 = ใหม่ ค่าเริ่มต้น / 1 = ระบบเดิม), `aquadat2BaseUrl`, `aquadat2Domain`
   - `mock/aquadat-stub.ps1` จำลอง ADFS + aquadat2 ครบ; `--aq2test` (ยอมเฉพาะ localhost)
   - Deploy รอบ 1 → backup ของเก่า `_backup\deploy_TruckSidecar_20261123-121619_pre_aquadat2\`
3. **`1292d13` — ปรับคำหน้าจอให้ผู้ใช้รู้ว่าต้องใช้บัญชี AD**
   - "เข้าสู่ระบบด้วยบัญชี AD", ช่อง "รหัสพนักงาน / รหัสผ่าน AD", ระบุ "ไม่ใช่รหัส Aquadat เดิม", เตือนล็อกบัญชี AD
   - ตั้ง `aquadatApi: 1` คำกลับเป็นแบบเดิม (ยืนยันด้วยภาพ `--preview` แล้ว) — ต้องเปิดโปรแกรมใหม่หลังแก้ config
   - รายงาน docx 4 หน้า (มีภาพหน้าจอก่อน/หลัง)
   - Deploy รอบ 2 → backup รอบ 1 ที่ `_backup\deploy_TruckSidecar_20261123-123051_pre_adwording\`

## ผลทดสอบ (เครื่อง dev เท่านั้น)
build 0 error · stub: รหัสผิดถูกปฏิเสธ / login / ส่ง 2 configID / 401→refresh→ส่งซ้ำ / success:false → error ✓ ·
`--aq2test` ชี้ PROD ถูกปฏิเสธ ✓ · ภาพหน้าจอ v1 และ v2 ถูกต้อง ✓

## งานค้าง (เรียงตามความสำคัญ)
- [ ] **ลงเครื่องโรงงาน** ด้วย `deploy.ps1` (เฉพาะ TruckSidecar เปลี่ยน)
- [ ] **เทสหน้างาน** ตาม checklist ใน [handoff_aquadat2_ruby.md](handoff_aquadat2_ruby.md) — จุดเสี่ยงสุดคือ `time:"24:00"` + `date:"yyyy-MM-ddT00:00:00"`
      ยังไม่เคยยืนยันบน aquadat2 (Uroboros ยืนยันแค่ `08:00`) ต้องดูหน้าเว็บว่ายอดลงช่อง 24:00 ของวันที่ถูก
- [ ] ยืนยันว่า "รหัสผ่าน AD" คือรหัสเดียวกับเข้าคอมพิวเตอร์ กปน. จริง (คำบนหน้าจออิงข้อนี้) ถ้าไม่ใช่ แก้คำใน `Ui/AquadatPanel.cs`
- [ ] ถ้าเทสหน้างานไม่ผ่าน: ตั้ง `"aquadatApi": 1` ที่ `D:\Sidecar_prj\TruckSidecar\config.json` แล้วเปิดโปรแกรมใหม่
- [ ] หลังเทสผ่าน: `code-review` แล้ว merge `Ruby` → `master`
- [ ] `config.json` และ handoff มีรหัสพนักงานอยู่ — ถ้า repo เป็น public พิจารณาว่าโอเคหรือไม่

## Gotchas ที่เจอวันนี้
- `TruckSidecar.exe` มี manifest `requireAdministrator` — รันทดสอบแบบไม่ยกสิทธิ์ด้วย `$env:__COMPAT_LAYER='RunAsInvoker'`
- เครื่อง dev ไม่มี LibreOffice — แปลง docx เป็น PDF ด้วย Word COM (`ExportAsFixedFormat`)
- สคริปต์สร้างรายงาน docx (docx-js) อยู่ใน scratchpad ของ session นี้ ไม่ได้เก็บใน repo — ถ้าจะแก้รายงานให้แก้ไฟล์ .docx โดยตรง

## Suggested skills
- **`run`** — เปิด sidecar / `--aq2test` กับ stub ตรวจหลังแก้
- **`code-review`** (level `medium`) — ก่อน merge `Ruby` → `master`
- **`anthropic-skills:docx`** — ถ้าต้องอัปเดตรายงานหลังได้ผลเทสหน้างาน
- **`handoff:handoff`** — สรุปอีกครั้งหลังเทสหน้างาน
