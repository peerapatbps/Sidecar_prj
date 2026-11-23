# สรุปรวม Handoff — โครงการ TruckSidecar (Sidecar_prj)

> **วันที่สรุป:** 2026-11-06
> **ใช้แทนไฟล์:** `handoff2509.md`, `qa-watchdog-2509.md`, `sequence_qa_testcase.md`, `handoff2609.md`, `handoff0310.md`, `handoff0810.md`, `handoff1210.md` (ครอบคลุม 25/09/2026 – 12/10/2026)
> **วัตถุประสงค์ของไฟล์:** เป็น reference สำหรับเขียนรายงาน — จัดตามประเด็น/feature โดยแต่ละหัวข้อมี ความเป็นมา → วัตถุประสงค์ → workflow ก่อน/หลัง → รายละเอียดปัญหาที่แก้ → ผลทดสอบ
> **หมายเหตุ:** สคริปต์ทดสอบแบบเต็ม (`ui.ps1`, `jet.ps1`, `nag.ps1`, `h.sh`) ที่อยู่ในภาคผนวกของ `sequence_qa_testcase.md` ไม่ได้คัดลอกมาที่นี่ (เป็นเครื่องมือทดสอบ ไม่ใช่เนื้อหารายงาน) — ถ้าจะลบ handoff เดิมและยังต้องใช้สคริปต์เหล่านี้ ให้เก็บไฟล์นั้นไว้ หรือย้ายสคริปต์ไปไว้ใน `mock\`

---

## สารบัญ

- [ภาค 1 ภาพรวมโครงการ](#ภาค-1-ภาพรวมโครงการ)
  - 1.1 ความเป็นมาของปัญหา · 1.2 วัตถุประสงค์ · 1.3 ข้อจำกัดบังคับ · 1.4 องค์ประกอบระบบ · 1.5 Workflow ก่อน/หลัง (ภาพรวม) · 1.6 ลำดับเหตุการณ์
- [ภาค 2 รายละเอียดแยกตาม Feature](#ภาค-2-รายละเอียดแยกตาม-feature)
  - F1 Sidecar v1 + TruckLauncher
  - F2 TruckWatchdog + Admin exit
  - F3 Sidecar v2 — ตรวจจับรถอัตโนมัติ (กฎ 5 ข้อ) + UI WPF
  - F4 ส่งยอดเข้า Aquadat
  - F5 รวมข้อมูลเข้า chem.db (Uroboros_mini) + เปลี่ยน JSON → SQLite
  - F6 แก้ปัญหาติดตั้ง/เปิดโปรแกรมที่เครื่องใช้งาน
  - F7 คาดการณ์ระดับถัง (Predictor) + อัตราจ่ายสารจริง
  - F8 ปรับ UI ฟอร์มรับสาร / แท็บส่งยอด + Mock แบบ cmd
  - F9 Uroboros_mini ไม่ upload ของใหม่ (mtime ไม่ขยับ)
- [ภาค 3 ข้อมูลอ้างอิงรวม](#ภาค-3-ข้อมูลอ้างอิงรวม)
  - 3.1 ตารางบั๊กทั้งหมด · 3.2 สรุปผลทดสอบ · 3.3 Edge cases · 3.4 ข้อเท็จจริงทางเทคนิค · 3.5 Config · 3.6 Build/Deploy · 3.7 บทเรียน · 3.8 งานค้าง · 3.9 แผนที่ไฟล์ source

---

# ภาค 1 ภาพรวมโครงการ

## 1.1 ความเป็นมาของปัญหา

- โรงงานผลิตน้ำของ กปน. (MWA) รับสารเคมี (**ALUM** = อลูมิเนียมซัลเฟต, **PACL** = PACl) ทางรถบรรทุก รถต้องชั่งน้ำหนักเข้า–ออกด้วยโปรแกรมชั่งเดิม **`truck93a.exe`** (Truck Scale V9.3A, เขียนด้วย VB6, ใช้ HASP dongle, **ผ่านการรับรองทางกฎหมาย — ห้ามแก้ไข**) ซึ่งเก็บข้อมูลใน **`data.mdb`** (Access/Jet)
- ข้อมูลการรับสารที่โปรแกรมชั่งไม่มี (ถ.พ. (SG), อุณหภูมิ, เลขใบนำส่ง, ลงถังไหน, ระดับถังก่อน/หลังเติม) เดิมจด**บนกระดาษ**ต่อเที่ยว → ตรวจสอบย้อนหลังยาก ไม่เชื่อมกับข้อมูลชั่ง ไม่อยู่บนหน้าจอเดียวกัน
- ยอดรับสารรายวัน (Chemical income) ต้องส่งเข้าระบบ **Aquadat** ของ กปน. — เดิมทำด้วยมือ
- ระดับสารในถังมาจาก API กลาง (`/api/chem/summary`) เป็น snapshot ราย 2 ชม. และ**ออกช้า ~1 ชม.** → หน้างานไม่เห็นระดับปัจจุบัน ตัดสินใจสั่งเติมยาก

## 1.2 วัตถุประสงค์ (ระดับโครงการ)

1. เพิ่มขั้นตอนบันทึกการรับสาร **โดยไม่แตะโปรแกรมชั่งเดิม** (ไม่แก้ exe, ไม่เขียน data.mdb, ไม่พิมพ์/คลิกเข้าโปรแกรมเดิม)
2. แทนกระดาษ: บันทึก 1 ครั้งต่อเที่ยว ข้อมูลผูกกับตั๋วชั่ง (Track ID = `TICKET1`) ตรวจสอบย้อนหลังได้ (audit)
3. อยู่บนหน้าจอเดียวกับโปรแกรมชั่ง (เกาะข้างหน้าต่าง) ทำงานในขั้นตอนเดียว
4. ส่งยอดรับสารรายวันเข้า Aquadat
5. รวมข้อมูลเข้า `chem.db` บน Google Drive (ผ่าน Uroboros_mini) เพื่อใช้ต่อในระบบกลาง
6. แสดงระดับถังพร้อม**ค่าคาดการณ์**ระหว่างรอบ API

## 1.3 ข้อจำกัดบังคับ (Hard constraints)

| ข้อจำกัด | เหตุผล |
|---|---|
| ห้ามแก้ `truck93a.exe` | โปรแกรมรับรองทางกฎหมาย |
| ห้ามเขียน `data.mdb` — อ่านจาก **สำเนาชั่วคราว** เท่านั้น | ไม่ให้ล็อก/ทำไฟล์ต้นฉบับเสีย |
| Sidecar อ่านอย่างเดียว ห้ามพิมพ์/คลิกเข้าโปรแกรมชั่ง และห้ามบังจอน้ำหนัก | ไม่รบกวนการชั่ง |
| ทะเบียนรถ + Track ID แก้ไม่ได้ (ReadOnly) มาจากตาชั่งเท่านั้น | กันพิมพ์ผิด/ปลอม |
| ทุกโปรแกรมเป็น **x86 self-contained** | เครื่องโรงงานมีแต่ .NET x86 และ Jet OLEDB เป็น 32 บิตเท่านั้น |
| เครื่องโรงงานเป็น **Windows 10**, culture **th-TH** (ปี พ.ศ.) | กระทบเรื่อง glass UI และการเขียนวันที่ |
| Login Aquadat ต้องให้คนพิมพ์รหัสเอง ห้ามวนเดารหัส | ดู F4 |

## 1.4 องค์ประกอบระบบ (4 โปรแกรม + ระบบภายนอก)

| โปรแกรม | หน้าที่ | เทคโนโลยี |
|---|---|---|
| **TruckSidecar** | หน้าต่างบันทึกการรับสาร เกาะข้างโปรแกรมชั่ง ตรวจจับรถจาก data.mdb, บันทึก, กราฟระดับถัง, ส่ง Aquadat | C# .NET 9 — v1 WinForms → **v2 WPF** |
| **TruckLauncher** | ไอคอนเดียวกับ truck93a — เปิด truck93a + sidecar + watchdog + uroboros แล้วปิดตัวเอง (บังคับให้เปิดโปรแกรมชั่งพร้อม sidecar เสมอ) | C# WinExe |
| **TruckWatchdog** | เปิด sidecar กลับเมื่อถูกปิด/crash **เฉพาะขณะโปรแกรมชั่งทำงาน** | C# WinExe |
| **Uroboros_mini** | แปลง data.mdb → `chem.db` + merge ข้อมูล sidecar → upload Google Drive | C# .NET 10, WinExe |

ระบบภายนอก: `truck93a.exe` + `data.mdb` (`D:\TRUCK93A\`), Chem summary API (`http://172.16.198.150:443/api/chem/summary`), Aquadat GraphQL (`https://aquadat.mwa.co.th/graphql`), Google Drive, Uroboros backend (ดึง chem.db แล้ว**แทนที่ทั้ง DB**)

**ตำแหน่งไฟล์:**
- dev: `C:\Users\peera\OneDrive\Desktop\Sidecar_prj\{TruckSidecar, TruckLauncher, TruckWatchdog, Uroboros_mini}` (25/09 เดิมอยู่บน Desktop แยกกัน — ย้ายมารวม 26/09)
- เครื่องโรงงาน: `D:\TRUCK93A\` (โปรแกรมชั่ง) และ `D:\Sidecar_prj\<4 โปรเจกต์>\`
- โฟลเดอร์ deploy บนเครื่อง dev: `Sidecar_prj_deploy\` (ช่วงแรกเรียก bundle `Sidecar_prj\Sidecar_prj\`), backup อยู่ `Sidecar_prj\_backup\`

## 1.5 Workflow ภาพรวม ก่อน / หลัง

**ก่อน**
```
รถมาถึง → ชั่งเข้าใน truck93a → ลงสารลงถัง → จดกระดาษ (SG, อุณหภูมิ, ใบนำส่ง, ถัง, ระดับ)
       → ชั่งออกใน truck93a → รวมยอดเอง → กรอก Aquadat ด้วยมือ
ระดับถัง: ดูจาก API ราย 2 ชม. (ช้า ~1 ชม.) ไม่มีค่าระหว่างรอบ
```

**หลัง**
```
เปิดโปรแกรมผ่าน TruckLauncher (UAC ครั้งเดียว) → truck93a + Sidecar + Watchdog + Uroboros_mini
รถชั่งเข้า (truck93a เขียน data.mdb)
  → Sidecar ตรวจ data.mdb ทุก 3 วิ (+บังคับ query ทุก 30 วิ) → เติมทะเบียน/Track ID อัตโนมัติ + เลือกแท็บสารตาม PRODUCT
  → พนักงานกรอก SG / อุณหภูมิ / ใบนำส่ง / ถัง → บันทึก (record ถูกสร้างแม้ยังไม่ครบ; ตรวจไขว้กับ data.mdb ทุกครั้ง)
  → กรอกระดับก่อน/หลังเติม
รถชั่งออก → Sidecar จับคู่ด้วย TICKET1 แนบ TICKET2/W2 อัตโนมัติ
  → ครบ → แบ่งน้ำหนักสุทธิตามปริมาตรที่เติมแต่ละถัง → outbox Aquadat
  → พนักงาน login Aquadat เลือกวัน → ส่งยอดรายวัน (time = 24:00)
Uroboros_mini ทุก 300 วิ: data.mdb + sidecar.db → chem.db → Google Drive
กราฟถัง: แท่ง = API ล่าสุด, เส้นประ/ตัวเลข = ค่าคาดการณ์ (ฐานจาก API หรือระดับหลังเติม − อัตราใช้)
ถ้า Sidecar ถูกปิดขณะโปรแกรมชั่งยังเปิด → Watchdog เปิดกลับ; ถ้ามีตั๋วค้างแล้วปิดโปรแกรมชั่ง → Sidecar ลอยค้าง (nag)
```

## 1.6 ลำดับเหตุการณ์ (Timeline)

| วันที่ | งาน | Feature |
|---|---|---|
| 25/09/2026 | Sidecar v1 (WinForms) + TruckLauncher + Aquadat push/login | F1, F4 |
| 25/09/2026 | TruckWatchdog + stop-flag protocol (QA 8/8 ผ่าน) | F2 |
| 25/09/2026 (ดึก) | Sidecar v2: กฎตรวจจับ 5 ข้อ + UI WPF liquid glass; QA เต็มชุด, แก้ 10 บั๊ก + 2 ปรับปรุง | F3 |
| 26/09/2026 รอบ 1 | รวม record เข้า `chem.db`, ย้าย 4 โปรเจกต์มาโฟลเดอร์เดียว, launcher เปิดครบ 4 ตัว, สคริปต์ deploy | F5, F6 |
| 26/09/2026 รอบ 2 | แก้ปัญหาที่เครื่องใช้งาน (path, admin, self-contained), JSON → SQLite `sidecar.db`, Predictor | F5, F6, F7 |
| 03/10/2026 | อัตราใช้สารจาก "อัตราจ่ายจริง" (flow × dose) | F7 |
| 08/10/2026 | UI: เลื่อน/focus ฟอร์ม, เลือกวันส่ง Aquadat, login ว่าง + Mock cmd | F8 |
| 12/10/2026 | Uroboros_mini: ตัดสินการเปลี่ยนแปลงด้วย hash แทน mtime | F9 |

---

# ภาค 2 รายละเอียดแยกตาม Feature

## F1 — Sidecar v1 + TruckLauncher (25/09)

### ความเป็นมา
ต้องการหน้าต่างบันทึกการรับสารที่อยู่คู่กับโปรแกรมชั่ง และต้องกันไม่ให้พนักงานเปิดโปรแกรมชั่งโดยไม่มี sidecar

### วัตถุประสงค์
- Sidecar เกาะข้างหน้าต่างโปรแกรมชั่ง (ตามเปิด/ย้าย/ย่อ/ขยาย/ปิด)
- ปุ่มเปิดโปรแกรมชั่งตัวเดียวที่เปิด sidecar ไปด้วยเสมอ

### สิ่งที่ทำ
- **Sidecar v1** (WinForms): อ่าน WDATA จากสำเนาชั่วคราวของ data.mdb, ปุ่ม "ดึงจากตาชั่ง" (picker), บันทึกเป็น JSON, เทียบระดับถังกับ API, แบ่งน้ำหนักเข้าถัง, ส่ง Aquadat
- **การเกาะหน้าต่าง (docking):** ตามหน้าต่างโปรแกรมชั่งทุก 150 ms; เมื่อโปรแกรมชั่ง maximize → ลงทะเบียนเป็น **AppBar ขอบขวา** (โปรแกรมชั่งหดหลบ); ย่อ → sidecar ซ่อน (ถ้าไม่มีตั๋วค้าง)
- **TruckLauncher:** อ่าน `launcher.json` (`truckExe`, `sidecarExe`, `sidecarProcess`, ภายหลังเพิ่ม `watchdogExe`, `uroborosExe`) → เปิด truck → sidecar (ข้ามถ้ารันอยู่แล้ว) → ปิดตัวเอง; ไอคอน `truck.ico` ดึงจาก truck93a.exe จริง (byte-identical) → ตั้ง shortcut ของพนักงานไปที่ TruckLauncher.exe
- **Mock harness** (`mock\`): ใช้ Notepad แทน truck93a, `mock.mdb` แทน data.mdb, stub Aquadat ที่ `localhost:18080` — `start-mock.bat`, `weighout.bat`, `stop-mock.bat`, `sim.ps1` (setup/weighin/weighout/reset), `aquadat-stub.ps1` (log ที่ `mock\received.log`)

### Workflow ก่อน / หลัง
- ก่อน: พนักงานเปิด truck93a ตรงๆ จดกระดาษ
- หลัง: เปิดผ่าน TruckLauncher → truck93a + sidecar ขึ้นพร้อมกัน sidecar อยู่ข้างหน้าต่างชั่งตลอด

### ผลทดสอบ
Launcher ปิดตัวเองและเปิดลูกครบ ✅

---

## F2 — TruckWatchdog + Admin exit (25/09)

### ความเป็นมา
พนักงานอาจปิด sidecar หรือ sidecar crash → บันทึกหาย; แต่ผู้ดูแลระบบต้องมีทางปิด sidecar ได้จริง

### วัตถุประสงค์
1. เปิด sidecar กลับเมื่อถูกปิด/crash **เฉพาะขณะโปรแกรมชั่งทำงาน**
2. เคารพการปิดโดยผู้ดูแล (`Ctrl+Shift+F12`) — ไม่เปิดกลับ
3. กลับมาคุ้มครองเมื่อ sidecar เปิดตามปกติ
4. จบตัวเองเมื่อโปรแกรมชั่งปิด และ**ไม่ฆ่า sidecar เอง**

### การออกแบบ (เลือกทาง (a) = process แยก)
- `TruckWatchdog` (C# WinExe x86) poll ทุก `pollSeconds: 5`; single-instance ด้วย mutex
- **Stop-flag protocol:** sidecar เขียน `<dataDir>\sidecar.stopped` เมื่อกด `Ctrl+Shift+F12` และ**ลบเมื่อเปิดตามปกติ**; watchdog อ่าน `config.json` ของ sidecar เพื่อหา `dataDir` (หรือ override `stopFlagPath`)
- ไม่พบโปรแกรมชั่งภายใน `startupGraceSeconds` (60) → ปิดตัวเอง; sidecarExe ผิด path → MessageBox เตือนแล้วหยุด
- พนักงานปิด sidecar ไม่ได้ (Alt+F4 / ปุ่ม X ถูกยกเลิก)
- Launcher เปิด watchdog ด้วย (`watchdogExe`/`watchdogProcess`)

### Workflow ก่อน / หลัง
- ก่อน: sidecar ถูกปิด = หายไปจนกว่าจะมีคนเปิดใหม่
- หลัง: ปิด/crash → เด้งกลับภายใน 1 poll; ผู้ดูแลกด Ctrl+Shift+F12 → ปิดจริงและไม่เด้งกลับ; เปิดใหม่ตามปกติ → คุ้มครองต่อ

### ปัญหาที่พบ
| ID | อาการ | สาเหตุ | แก้ |
|---|---|---|---|
| BUG-1 (mock เท่านั้น) | `Start-Process notepad` บน Win11 ตรวจไม่เจอ process → watchdog หมดเวลา, sidecar เข้า nag | Win11 ใช้ Store Notepad ผ่าน alias | `start-mock.ps1` เปิดด้วย full path `%SystemRoot%\System32\notepad.exe` + รอ 2 วิ; **ไม่กระทบเครื่องจริง** (truck93a เป็น Win32 แท้) |
| หมายเหตุ | ทดสอบแล้ว watchdog ตัวใหม่ออกทันที | watchdog ตัวเก่ายังถือ mutex | ต้อง kill ให้หมดก่อนทดสอบ (ยืนยันว่า single-instance ถูก) |
| BUG-9 (พบใน F3) | `sidecar.stopped` เป็น JSON ไม่ถูกต้อง (`\p`) | ต่อ string โดยไม่ escape `\` ใน `DOMAIN\user` | ใช้ `JsonSerializer` |

### ผลทดสอบ (8/8 ผ่าน)
- Unit (ตัวแทน: ping = truck, charmap = sidecar): A1 relaunch ✅, A2 relaunch ซ้ำ ✅, A3 เคารพธง ✅, A4 จบเอง + ล้างธง ✅, path ผิด → MessageBox ✅, single-instance ✅
- Integration (sidecar จริง + Notepad): B1 relaunch (PID 11592→30564) ✅, B2 เคารพ admin-exit ✅, B3 ล้างธงตอน startup + คุ้มครองต่อ ✅, B4 จบเมื่อ truck ปิด ไม่ฆ่า sidecar ✅
- ทดสอบซ้ำใน F3 ด้วยการกดคีย์จริงเข้าหน้าต่าง sidecar (F1–F4) ✅ — **ยังไม่ได้ทดสอบกับหน้าต่าง truck93a จริง**

---

## F3 — Sidecar v2: ตรวจจับรถอัตโนมัติ (กฎ 5 ข้อ) + UI WPF (25/09 ดึก)

### ความเป็นมา
v1 ต้องกด "ดึงจากตาชั่ง" เองทุกครั้ง → ลืม/เลือกรถผิด; ไม่มีการกันบันทึกซ้ำ; UI WinForms ทำ glass ไม่ได้ และ sidecar ไม่ hook UI ของโปรแกรมเดิม จึงต้องอาศัยการเฝ้า DB

### วัตถุประสงค์ (ความต้องการของผู้ใช้)
1. ตรวจ data.mdb ทุก 3 วิ — มีรถชั่งเข้า (`STAT=1`, `TICKET2` ว่าง) → เติมฟอร์มอัตโนมัติ
2. หลังตรวจเจอ พนักงานกรอก ถ.พ./อุณหภูมิ/ใบนำส่ง/ถัง → สร้าง record ได้แม้ระดับก่อน/หลังยังว่าง
3. ชั่งออก (`STAT=2`) ทำงานแบบเดียวกัน
4. ตรวจไขว้ทุกครั้งที่บันทึก กันประมวลผลซ้ำ
5. UI 3 แท็บ (ALUM / PACL / ส่งยอดสารเคมี) สไตล์ iOS "liquid glass"

### การตัดสินใจร่วมกับผู้ใช้
| หัวข้อ | ตัดสินใจ | เหตุผล |
|---|---|---|
| UI | **WPF** (เขียนใหม่เฉพาะชั้น UI; v1 เก็บใน `_backup_v1\`) | WinForms ทำ glass จริงไม่ได้; WebView2 อาจไม่มีบน Win10 โรงงาน |
| Glass | `glass: "blur"` เป็นค่าเริ่มต้น (`acrylic`/`off` ได้) | acrylic หน่วงบน Win10 ตอนหน้าต่างขยับ (sidecar ขยับตามทุก 150 ms) |
| รถหลายคัน | "both": `max(TICKET1)` เติมฟอร์ม + คันอื่นเป็น chip ในรายการ "รถรอบันทึก" | ข้อมูลจริงมีรถ ALUM 2 คันห่างกัน 4 นาที |
| เลือกแท็บ | อัตโนมัติจาก `WDATA.PRODUCT`: `01`→ALUM, `03`→PACL, `02` (สินค้าทั่วไป) → ไม่สนใจ | จากตาราง PRODUCT ใน data.mdb |
| กฎ 4 | `STAT='2' AND DAYOUT=วันนี้` และ**จับคู่ด้วย TICKET1** | ชั่งออกข้ามเที่ยงคืน + TICKET1/TICKET2 เป็นเลขรันแยกกัน (เข้าก่อนอาจออกทีหลัง) |
| กฎ 2 (ขยาย) | รถ STAT=1 ที่ชั่งเข้าภายใน **24 ชม.** (`inWindowHours`) | รถชั่งเข้าก่อนเที่ยงคืนต้องตรวจเจอด้วย |
| สลับแท็บ | เฉพาะเมื่อฟอร์มแท็บปัจจุบันว่าง | ไม่ดึงแท็บไปขณะพนักงานพิมพ์ |

### Workflow ก่อน / หลัง
- **ก่อน (v1):** รถชั่งเข้า → พนักงานกด "ดึงจากตาชั่ง" → เลือกแถว → กรอก → บันทึก → รอชั่งออกแล้วกดดึงอีกรอบ
- **หลัง (v2):** รถชั่งเข้า → ~3 วิ แท็บสารถูกเลือก + ทะเบียน/Track ID เติมเอง + toast → กรอกเอกสาร → บันทึก (stepper ①–⑤) → รถชั่งออก → ~3 วิ TICKET2/W2/น้ำหนักสุทธิเติมเอง → กรอกระดับ → ครบ → outbox Aquadat

### กลไกและกฎ 5 ข้อ (decision tree)

**Heartbeat (`ScaleWatcher`)** — ทุก `watchSeconds` (3 วิ):
- `mdbPath` ว่าง → ไม่ทำอะไร (pill เทา); ไฟล์ไม่มี → pill แดง ลองใหม่รอบหน้า
- stamp (LastWriteTime+Length) ไม่เปลี่ยน และยังไม่ถึง `forceCheckSeconds` (30 วิ) → ข้าม
- เปลี่ยนหรือครบ 30 วิ → copy ไป temp + query Jet → ล้ม (ไฟล์เขียนไม่เสร็จ) ไม่เก็บ stamp ลองใหม่ใน 3 วิ / สำเร็จ → `Process(Ins, Outs)` → `CatchUpOldWaiting()` → วาดใหม่

**กฎ 1 — ทะเบียน/Track ID มาจากตาชั่งเท่านั้น** (ช่อง ReadOnly) ได้ 2 ทาง:
- (a) ตรวจจับอัตโนมัติ (กฎ 2) — ไม่นับเป็น "พิมพ์แล้ว" (รถใหม่กว่าแทนที่ได้)
- (b) ปุ่ม "ดึงจากตาชั่ง":
  - TICKET1 มี record แล้ว → ถ้าเป็นสารอื่น สลับไปแท็บของมันแล้วเปิด (ไม่เปิดใน 2 ฟอร์ม) / สารเดียวกัน → เปิด
  - PRODUCT เป็นสารอื่น → "รถคันนี้เป็น X — ไปบันทึกที่แท็บ X"
  - ฟอร์มกำลังแก้ record อื่น → "ผูกกับ Track ID … แล้ว เปลี่ยนไม่ได้"

**กฎ 2 — ตรวจจับชั่งเข้า (IN)**
- `Ins = WDATA WHERE STAT='1' AND TICKET2 ว่าง AND DAYIN >= เมื่อวาน ORDER BY TICKET1 DESC` แล้วกรองใน C# ด้วย `DAYIN+TMIN >= now − 24 ชม.`; เก็บเฉพาะ PRODUCT ใน `productMap`
- แถวที่มี record แล้ว / อยู่ใน `dismissed.json` → ไม่แสดง; อื่นๆ → **chip สีน้ำเงิน** "ชั่งเข้า hh:mm · รอบันทึก" (รถเมื่อวานแสดงวันที่)
- คันหลัก (max TICKET1) ใหม่ → ถ้าฟอร์มสารนั้นว่าง → เติมฟอร์ม; สลับแท็บเฉพาะเมื่อแท็บปัจจุบันว่าง/เป็นแท็บนั้น/เป็นแท็บส่งยอด มิฉะนั้น toast "เติมฟอร์มไว้ที่แท็บ X"; ถ้าฟอร์มไม่ว่าง → chip + toast
- ฟอร์มอัตโนมัติที่ยังไม่ถูกแตะ แต่ตั๋วหายจากตาชั่ง (ถูกยกเลิก) → ล้างฟอร์ม + toast แดง
- ชั่งเข้าเกิน 24 ชม. → ไม่ตรวจจับ ต้องใช้ picker

**กฎ 3 — บันทึกครั้งแรกสร้าง record แม้ยังไม่ครบ**
- Stepper (คำนวณจากข้อมูล ไม่ได้เก็บ): ① ชั่งเข้า ② ใบนำส่ง (SG+อุณหภูมิ+เล่ม+เลข+≥1 ถัง) ③ ระดับถัง (ทุกแถวมีก่อน+หลัง) ④ ชั่งออก ⑤ ครบ
- ยังไม่ครบ → อยู่ในรายการ "รถรอบันทึก" (สีส้ม) และนับเข้า nag mode

**กฎ 4 — ตรวจจับชั่งออก (OUT)**
- `Outs = WDATA WHERE STAT='2' AND DAYOUT=วันนี้`
- มี record ของ TICKET1 นี้และยังไม่มี TICKET2 → TICKET2 ซ้ำกับ record อื่น / ทะเบียนไม่ตรง → **ปฏิเสธ + audit ครั้งเดียว + toast แดง**; ผ่าน → แนบ TICKET2/W2/เวลา → ครบ? → แบ่งน้ำหนัก → outbox Aquadat (ถ้า record เปิดอยู่ เปลี่ยนเฉพาะข้อมูลชั่ง ไม่ทิ้งที่พิมพ์ค้าง)
- ไม่มี record → **chip สีแดง** "ชั่งออกแล้วแต่ยังไม่มีบันทึก"
- Catch-up: record ที่รอชั่งออกแต่ไม่อยู่ในรายการวันนี้ → ค้นด้วย TICKET1 (กรณี sidecar ปิดไปข้ามวัน)

**กฎ 5 — ตรวจไขว้ก่อนบันทึก (`CrossCheckAsync`)**
| รหัส | เงื่อนไข | ผล |
|---|---|---|
| B1 | ไม่มี Track ID | BLOCK "ยังไม่ได้เลือกรถ" |
| B2 | TICKET1 อยู่ใน record อื่นแล้ว | BLOCK "ถูกบันทึกแล้ว" |
| — | อ่าน data.mdb ไม่ได้ | ถามพนักงาน "บันทึกต่อโดยไม่ตรวจกับตาชั่ง?" (audit) |
| — | ไม่พบแถวใน data.mdb | BLOCK "ไม่พบ Track ID … (อาจถูกยกเลิก)" |
| B3 | ทะเบียนไม่ตรง | BLOCK |
| B4 | วันชั่งเข้าไม่ตรง | BLOCK |
| B5 | ชั่งออกแล้วระหว่างกรอก และ TICKET2 ผูกกับ record อื่น | BLOCK (ไม่ซ้ำ → แนบผลชั่งออกก่อนบันทึก) |
| B6 | มีถังของสารอื่น | BLOCK "รถ X เลือกถัง Y ไม่ได้" |
| W1 | PRODUCT ไม่ตรงกับสารของ record | WARN |
| W2–W5 | SG นอก 1.0–1.6 · อุณหภูมิ นอก 5–60 °C · ระดับเกินความจุ · หลัง ≤ ก่อน | WARN |

BLOCK = ปฏิเสธ (dialog แดง) · WARN = ขอยืนยัน · ทุก BLOCK/WARN บันทึก audit (`crosscheck`)

**Nag mode + Admin exit**
- โปรแกรมชั่งถูกย่อ/ปิด ขณะมีตั๋วค้าง → sidecar ไม่ซ่อน **ลอยบนสุด (topmost)** หัวแดง "⚠ บันทึกไม่ครบ" + "มีตั๋วค้าง N คัน — บันทึกให้ครบก่อนปิดโปรแกรมชั่ง"; หายเมื่อโปรแกรมชั่งกลับมา
- `Ctrl+Shift+F12` = ทางออกผู้ดูแล (ปลด AppBar, เขียนธง, ปิด)

### ปัญหาที่พบและแก้ไข (10 บั๊ก + 2 ปรับปรุง)
ระดับ: 🔴 ร้ายแรง (ข้อมูลผิด/หาย หรือใช้ไม่ได้) · 🟠 สำคัญ (กลไกไม่ทำงานตามออกแบบ) · 🟡 UX/เล็กน้อย

| ID | ระดับ | อาการ | สาเหตุ | การแก้ |
|---|---|---|---|---|
| BUG-1 | 🟡 | รถใหม่ดึงแท็บไปขณะพนักงานพิมพ์ | ตรวจแค่แท็บปลายทางว่าง | ตรวจแท็บปัจจุบันด้วย ถ้าไม่ว่างไม่สลับ + toast |
| BUG-2 | 🔴 | เปิดใหม่แล้วไม่เติมฟอร์มอัตโนมัติ, API "—" โดยไม่มี error | เริ่ม async ใน constructor ก่อน `app.Run()` → continuation รันบน thread pool → แตะ UI ผิด thread แล้ว exception หายเงียบ (และรถถูกจำว่าประกาศแล้ว) | เริ่มผ่าน `Dispatcher.BeginInvoke` |
| BUG-3 | 🟠 | ชั่งออกขัดแย้ง (TICKET2 ซ้ำ/ทะเบียนไม่ตรง) มองไม่เห็น + audit ซ้ำทุก 30 วิ | `ApplyWeighOut` log ทุกรอบ ไม่มีทางแจ้ง UI | `Conflict()` log ครั้งเดียวต่อกรณี + event `ScaleConflict` → toast แดง 15 วิ |
| BUG-4 | 🔴 | record PACL เปิดในแท็บ ALUM → record เดียวเปิดใน 2 ฟอร์ม (บันทึกทับกัน) | กิ่ง "มี record แล้ว" ไม่ตรวจสาร | `openInOwnTab` สลับไปแท็บของมัน |
| IMP-1 | 🟡 | แถวถังสารอื่นใน record v1 ลบไม่ได้ → ติด BLOCK ไปต่อไม่ได้ | ไม่มี pill ให้ปิด | ปุ่มแดง "✕ ถัง"; fills สร้างจากแถวบนจอเท่านั้น |
| BUG-5 | 🔴 | ไฟล์ record เสีย 1 ไฟล์ → sidecar crash วน (watchdog เปิดซ้ำไม่จบ) | constructor ของ `RecordStore` ไม่มี try/catch และรันก่อน `app.Run` | ย้ายไฟล์ไป `records\_bad\` + audit + toast แดง แล้วทำงานต่อ |
| BUG-6 | 🟡 | ปี 2569 ในชื่อไฟล์ / เสี่ยง parse วันที่เป็น พ.ศ. | culture th-TH ใช้ปฏิทินพุทธ | ใช้ `InvariantCulture` / `TryParseExact` |
| BUG-7 | 🟠 | Nag mode ไม่ลอยบนสุด (ถูกบังได้) | WPF `Window.Topmost` ไม่มีผลกับหน้าต่างนี้ | เรียก `SetWindowPos(HWND_TOPMOST)` ตรงและตรวจทุก tick (ซ่อมตัวเอง) |
| BUG-8 | 🟡 | หัว nag ตัดบรรทัด + คำบรรยายว่าง 30 วิ | ชื่อยาวเกิน 400 px; รอ timer | ชื่อสั้น + คำบรรยายละเอียด, `RefreshAll` ทันที |
| BUG-9 | 🟡 | `sidecar.stopped` เป็น JSON ไม่ถูกต้อง | ไม่ escape `\` | `JsonSerializer` |
| BUG-10 | 🟠 | คลิก chip หายแบบสุ่ม → พิมพ์ลงรถผิดคันได้ | `RefreshQueue` สร้างปุ่มใหม่ทุก 3 วิ | สร้างใหม่เฉพาะเมื่อ signature ของข้อมูลเปลี่ยน (ทำเหมือนกันกับตาราง Aquadat) |
| IMP-2 | 🟡 | ฟอร์มอัตโนมัติของตั๋วที่ถูกยกเลิกค้างบนจอ | ไม่มีการเทียบ draft กับตาชั่ง | `DropIfCancelled()` ล้างเฉพาะเมื่อยังไม่ถูกแตะ + toast แดง (ถ้าพิมพ์แล้ว เก็บไว้และ BLOCK ตอนบันทึก) |

ล้างเล็กน้อย: เอา emoji 🚚/⚖ ออกจาก toast (แสดงผลไม่ดีกับ Leelawadee UI); เพิ่ม `AutomationProperties.Name` ให้ช่อง/แท็บ/ปุ่ม ✕

### ผลทดสอบ (ทั้งหมดผ่าน — ใช้ mock + UI Automation)
- **A Build/Render:** build ไม่มี warning (~58 MB), `--preview` 3 แท็บ, `--aqtest` ค่าเท่า v1 (8424=14.2 · 8423=3 + 8424=9 · 8037=19) ✅
- **B Workflow:** B1 ตรวจจับชั่งเข้า ~3 วิ ✅ · B2 หลัง restart (❌→แก้ BUG-2→✅) · B3 บันทึกระดับว่าง ✅ · B4 รถคันที่ 2 ขณะพิมพ์ (แก้ BUG-1) ✅ · B5 PRODUCT 02 ไม่สนใจ ✅ · B6 ชั่งออกอัตโนมัติ ✅ · B7 ครบ → outbox `8424 = 14.0` ✅
- **C กฎ 5:** ทะเบียนไม่ตรง ✅ · แถวถูกลบ ✅ · TICKET1 ซ้ำผ่าน picker ✅ · เปลี่ยนตั๋วขณะแก้ ✅ · record สารอื่น (แก้ BUG-4) ✅ · TICKET2 ซ้ำตอนชั่งออก (แก้ BUG-3) / ตอนบันทึก ✅ · ถังสารอื่น + ปุ่ม ✕ ✅ · `--cctest` **7/7 PASS**
- **D คิว/ซ่อน:** chip เมื่อฟอร์มไม่ว่าง ✅ · "ไม่ใช่รถสารเคมี" → `dismissed.json` ✅ · คงอยู่หลัง restart ✅
- **E Docking:** ตามการย้าย ✅ · maximize → AppBar ✅ · restore ✅ · minimize → ซ่อน ✅ · API ล่ม → pill แดง ส่วนอื่นทำงานปกติ ✅
- **F Nag + Admin exit:** N0–N5 (แก้ BUG-7/8) ✅ · F1–F4 Ctrl+Shift+F12 ปกติ/หลัง restart/ระหว่าง nag/ระหว่าง AppBar ✅
- **G ลำดับรถ 17 สถานการณ์:**

| สถานการณ์ | ผล |
|---|---|
| IN 1 คัน → บันทึก → OUT → ระดับ → ครบ | ✅ IN/OUT อัตโนมัติ ~3 วิ, เข้า outbox |
| IN1 → IN2 ติดกัน (ฟอร์มไม่ถูกแตะ) | ✅ ฟอร์มเปลี่ยนเป็น IN2, IN1 เป็น chip |
| IN1 (กำลังพิมพ์) → IN2 | ✅ ฟอร์มไม่ถูกแตะ, IN2 เป็น chip |
| IN1 → IN2 → OUT1 → OUT2 | ✅ แต่ละ record ได้ TICKET2 ของตัวเอง |
| **IN1 → IN2 → OUT2 → OUT1 (ออกสลับลำดับ)** | ✅ ถูกเพราะจับคู่ด้วย TICKET1 |
| IN → OUT ก่อนบันทึก | ✅ chip แดง → เปิด → บันทึกพร้อม TICKET2 |
| ALUM แล้ว PACL ติดกัน | ✅ แยกแท็บ |
| สินค้าทั่วไป (02) | ✅ ไม่แสดง |
| ทะเบียนเดิมกลับมาวันเดียวกัน | ✅ record แยก (TICKET1 ใหม่) |
| IN เมื่อวาน → OUT วันนี้ | ✅ (W1 23.6 ชม. เจอ · W2 24.1 ชม./W3 47 ชม. ไม่เจอ · W4 วันนี้เป็นคันหลัก · W5 บันทึก+ออก ผ่าน) |
| Sidecar ปิดระหว่าง OUT (วันนี้/เมื่อวาน) | ✅ scan แรก / catch-up |
| IN → ยกเลิกในโปรแกรมชั่ง | ✅ ฟอร์มไม่ถูกแตะล้างเอง (IMP-2); ถ้าพิมพ์แล้ว BLOCK ตอนบันทึก |
| TICKET2 ซ้ำ / ซ่อน / restart มี draft | ✅ |

**ข้อสรุปเชิงปฏิบัติ:** รถออกลำดับใดก็ได้; รถที่หลุดกู้ได้เสมอ (IN หลุด = chip น้ำเงิน, OUT หลุด = chip แดง, sidecar ปิด = scan แรก/catch-up, IN เมื่อวาน ≤ 24 ชม. = อัตโนมัติ, เกิน = picker)

---

## F4 — ส่งยอดเข้า Aquadat (25/09, ปรับ UI 08/10)

### ความเป็นมา
ยอดรับสารรายวันต้องกรอกเข้า Aquadat ด้วยมือ

### วัตถุประสงค์
คำนวณยอดรับต่อ configID จากเที่ยวที่บันทึกครบ แล้วส่งเข้า Aquadat เป็นยอดตัดรายวัน

### รายละเอียด
- **การคำนวณ:** ตัน/เที่ยว = |W1 − W2| / 1000; รถเติมหลายถัง → แบ่งตามปริมาตรที่เติม (`Interpolation.cs`) แล้วรวมตาม configID
- **configID:** Alum A+B = **8037**, Alum C+D = **8036**, PACL A+B = **8423**, PACL C+D = **8424**
- **ส่งแบบตัดรายวัน:** `time` = `"24:00"` เสมอ; ค่า = ผลรวมเที่ยวของวันนั้น (upsert); mutation `update_transaction_datavalues` ที่ `https://aquadat.mwa.co.th/graphql`
- **Outbox:** SQLite (`aquadat_outbox.db`, ตาราง `aq_outbox`) — `PendingDaily()`, `MarkDaysSynced()`, `DailySummary()`
- **"Central DB" = Aquadat** (ผู้ใช้ยืนยัน) — `centralApiUrl` ปล่อยว่างเป็น hook สำรอง; คอลัมน์ `SYNCED`=0 ใน sidecar.db หมายถึงยังไม่ส่ง Central DB (ปิดอยู่) จึงเป็น 0 ตลอดเป็นปกติ

### การตัดสินใจเรื่อง Login (สำคัญ — ห้ามย้อน)
- โค้ดเดิมของ Uroboros (`AquadatFast.cs` `LoginToAPI`) **วนเดารหัส 8 หลักซ้ำกัน** — ระบบความปลอดภัยของ agent บล็อกการคัดลอก (ถูกปฏิเสธ 3 ครั้ง) และผู้ใช้ตกลง**ไม่ใช้**
- ใช้ **login แบบโต้ตอบ:** พนักงานพิมพ์ username + password เอง → POST ครั้งเดียวไป `aquadatEnrollUrl` → token เก็บ**ในหน่วยความจำเท่านั้น** ไม่ลงดิสก์ → login สำเร็จจึงเห็นปุ่ม "ส่ง Chemical income"
- `aquadatEnrollUrl` / `aquadatUrl` อยู่ใน config ให้ stub แทนได้

### ปรับปรุง UI แท็บส่งยอด (08/10)
| ก่อน | หลัง |
|---|---|
| username ถูกเติมจาก config ไว้ล่วงหน้า | ช่องว่าง + placeholder "เช่น 00102616" + focus ที่ชื่อผู้ใช้ทุกครั้งที่เข้าแท็บ (ถ้ายังไม่ login) |
| แสดงรายการทุกวันที่ค้าง | หลัง login มี **ปุ่มเลือกวัน 3 ปุ่ม**: เมื่อวาน / 2 / 3 วันก่อน (ค่าเริ่มต้นเมื่อวาน; ไม่รวมวันนี้เพราะยอดยังไม่ครบวัน) ป้าย `•` รอส่ง / `✓` ส่งแล้ว; วันว่าง → "ไม่มียอด" ไม่มีปุ่มส่ง; ค้างเก่ากว่า 3 วัน → เตือนสีส้ม |

ไฟล์: `Ui/AquadatPanel.cs` (`OnEnter()`, `DayChoices = 3`), `Ui/Glass.xaml` (style `Field` แสดง `Tag` เป็น placeholder — ใช้ได้กับทุกช่อง)

### ผลทดสอบ
`--aqtest` (interpolation + payload) ✅, `--preview` แท็บส่งยอด ✅ — **ยังไม่ได้ login Aquadat จริง** (ต้องให้ผู้ใช้พิมพ์รหัสเอง) และตัวเลือกวันยังไม่เห็นบนจอจริง

---

## F5 — รวมข้อมูลเข้า chem.db (Uroboros_mini) + เปลี่ยน JSON → SQLite (26/09)

### ความเป็นมา
ข้อมูลการรับสารต้องไปอยู่ในระบบกลาง; Uroboros_mini แปลง data.mdb → chem.db แล้ว upload Drive อยู่แล้ว จึงให้ merge ข้อมูล sidecar เข้าไปด้วย รอบแรกใช้ไฟล์ JSON ต่อเที่ยว แต่ไฟล์สะสมเยอะ (~1 KB/เที่ยว, 1,000–1,500 เที่ยว/ปี) และ merge ยุ่ง

### วัตถุประสงค์
- `chem.db` ฉบับสมบูรณ์ = ข้อมูลชั่ง + ข้อมูลการรับสาร
- เก็บ record เป็น SQLite schema เดียวกับ chem.db → merge ด้วย `ATTACH` + `INSERT…SELECT`, ไฟล์เดียว, query ที่หน้างานได้ (ผู้ใช้เป็นคนเสนอ)

### Workflow ก่อน / หลัง
- **ก่อน (รอบ 1):** sidecar เขียน `data\records\*.json` → Uroboros อ่าน JSON (`SidecarRecordsDir`) แปลงเป็นตาราง; convert ใหม่เฉพาะเมื่อ data.mdb เปลี่ยน
- **หลัง (รอบ 2):** sidecar เขียน `data\sidecar.db` → Uroboros snapshot → ATTACH → INSERT…SELECT เข้า chem.db; convert ใหม่เมื่อ data.mdb **หรือ** ข้อมูล sidecar เปลี่ยน

### รายละเอียด
**Schema ใน chem.db:** `WDATA` (มี `TICKET1`/`TICKET2`, เฉพาะ STAT='2'); `SIDECAR_UNLOAD` (PK `ID`, soft FK `TICKET1`→`WDATA.TICKET1`); `SIDECAR_FILL` (FK `UNLOAD_ID`→`SIDECAR_UNLOAD.ID`, `ON DELETE CASCADE`) — DDL ใน `Uroboros_mini\MdbReaderSafe.cs`

**TruckSidecar (`RecordStore.cs`)**
- `data\sidecar.db`: `SIDECAR_UNLOAD` + `SIDECAR_FILL` (DDL เดียวกับ chem.db — `RecordStore.Schema`) + `SIDECAR_AUDIT` (แทน `audit.jsonl`, ไม่ถูก merge)
- API ของ `RecordStore` ไม่เปลี่ยน (in-memory dictionary เหมือนเดิม เปลี่ยนแค่ที่เก็บ)
- `Save` = upsert UNLOAD + เขียน FILL ใหม่ + audit **ใน transaction เดียว**, `synchronous=FULL`, `Pooling=false`
- เวลาเก็บเป็น `yyyy-MM-dd HH:mm:ss`
- **ย้ายข้อมูลอัตโนมัติ:** เปิดครั้งแรกนำเข้า `records\*.json` → ย้ายโฟลเดอร์ไป `records_migrated_<ts>` (**ไม่ลบ**) + audit `migrate-json`
- DB เสีย → ย้ายเป็น `sidecar.db.bad_<ts>` สร้างใหม่ + toast (ไม่ crash วน)
- self-test `--storetest <dataDir> <out.txt>` (**ห้ามชี้ไป data\ ของเครื่องจริง** — มันแก้ record)

**Uroboros_mini (`SidecarMerge.cs` เขียนใหม่, `MdbReaderSafe.cs`, `Program.cs`)**
- config `"SidecarDb": "..\\TruckSidecar\\data\\sidecar.db"` (resolve จากโฟลเดอร์ exe)
- ขั้นตอน: **Snapshot** ด้วย SQLite online-backup API (read-only, `busy_timeout` 30 วิ — ปลอดภัยแม้ sidecar กำลังเขียน) → **ATTACH ก่อนเปิด transaction** → `INSERT OR REPLACE … SELECT` → commit → DETACH → ลบ snapshot
- **Stamp** = (สถานะ data.mdb) + `_sc{COUNT}v{SUM(VERSION)}s{SUM(SYNCED)}` จาก `SIDECAR_UNLOAD`
- merge เป็น best-effort: sidecar.db ไม่มี/snapshot ล้ม → log WARN แล้ว upload ต่อ
- self-test `--sidecar-test [sidecar.db] [outDb]`

### ปัญหาที่แก้
| ปัญหา | สาเหตุ | แก้ |
|---|---|---|
| ตาราง `SIDECAR_*` ใน chem.db ว่าง | Uroboros convert/upload เฉพาะเมื่อ data.mdb เปลี่ยน → record ที่บันทึกหลังรอบ mdb ล่าสุดไม่ถูก merge จนรถคันถัดไปชั่ง | stamp รวมสถานะ sidecar → upload ภายในรอบถัดไป (≤ 300 วิ) |

### ผลทดสอบ (เครื่อง dev)
- ข้อมูล: JSON จริง 2 เที่ยว (PACL, TICKET1 `0000014808`/`0000014809`) + mock 12 รายการ
- `--storetest`: นำเข้า 14/14, save+reopen v2→v3 PASS ✅
- `--sidecar-test`: merge 14 UNLOAD + 14 FILL, NET_KG เที่ยวจริง = 14115 / 14605 (ตรงกับ chem.db บน Drive), stamp `_sc14v28s0` ✅
- ผู้ใช้ยืนยันเวอร์ชัน JSON ที่เครื่องใช้งาน: chem.db บน Drive มีข้อมูล SIDECAR ✅
- ❗ **เวอร์ชัน SQLite ยังไม่มีบันทึกยืนยันผลที่เครื่องใช้งาน** (ใน handoff)

### ข้อควรระวัง
- **sidecar.db = ต้นฉบับ** — chem.db สร้างใหม่ทั้งไฟล์ทุกรอบ ลบแถวใน sidecar.db = หายจาก Drive ด้วย
- ห้ามก็อปไฟล์ .db ตรงๆ ขณะ sidecar ทำงาน (ใช้ backup API)

---

## F6 — แก้ปัญหาติดตั้ง/เปิดโปรแกรมที่เครื่องใช้งาน (26/09)

### ความเป็นมา
หลังย้ายโปรเจกต์มารวมโฟลเดอร์และติดตั้งที่เครื่องใช้งาน พบว่าเปิดไม่ขึ้นบางตัว, UAC เด้งหลายครั้ง, Uroboros เปิดไม่ได้หลัง re-publish

### วัตถุประสงค์
เปิดทุกโปรแกรมได้จากปุ่มเดียว UAC ครั้งเดียว ติดตั้งซ้ำได้โดยไม่ทับข้อมูล

### Workflow ก่อน / หลัง
- **ก่อน:** config ชี้ path แบบ absolute, แต่ละตัวขอสิทธิ์แยก, ก็อปไฟล์เอง
- **หลัง:** TruckLauncher (UAC ครั้งเดียว) → truck93a → sidecar → watchdog → uroboros (ท้ายสุด) ทุกตัวได้ admin ตาม; ติดตั้งด้วย `stage.ps1` / `deploy.ps1`

### ปัญหาที่แก้
| # | อาการ | สาเหตุ | แก้ |
|---|---|---|---|
| 1 | Launcher เปิด sidecar/uroboros ไม่ขึ้น | config ชี้ `D:\Sidecar_prj\...` แต่เครื่องที่รันไม่มี D: | ใช้ path relative (resolve จาก `AppContext.BaseDirectory`): `..\TruckSidecar\TruckSidecar.exe`, `..\TruckWatchdog\TruckWatchdog.exe`, `..\Uroboros_mini\Uroboros.MiniUploader.exe`; `truckExe` คง `D:\TRUCK93A\truck93a.exe`; แก้ที่ source ด้วย (deploy ทับ config จาก publish) |
| 2 | UAC เด้งหลายครั้ง / watchdog เปิด sidecar กลับไม่ได้ | สิทธิ์ไม่เท่ากัน | `app.manifest` `requireAdministrator` ให้ Launcher, Watchdog, Sidecar (Uroboros มีอยู่แล้ว) → UAC ครั้งเดียวที่ Launcher; เปิดตัวลูกตรงๆ จะเด้ง UAC ทุกครั้ง |
| 3 | Uroboros_mini ไม่ขึ้นหลัง re-publish (regression) | csproj ไม่มี `<SelfContained>` → ได้ runtimeconfig แบบ framework-dependent ปนกับ runtime self-contained เก่า (robocopy ไม่ลบ) | เพิ่ม `<SelfContained>true</SelfContained>`; ตรวจว่า `runtimeconfig.json` มี `"includedFrameworks"` |
| 4 | config.json ของ sidecar หายจาก bundle → `mdbPath` ว่าง | ไม่ได้ก็อป | ก็อปจาก source (`mdbPath` = `D:\TRUCK93A\data.mdb`) |
| 5 | Config Uroboros_mini ต้องเท่าตัวเดิมที่ใช้งาน | — | `PollIntervalSeconds`=300, `ScheduleWindowMinutes`=0, `IotEnabled`=true + ค่า IoT (`IotPollIntervalSeconds`=15); `credentials.json` เหมือนเดิม; ก็อป `token_store\` ล่าสุด; `config_\config.ini` `mdb_file_location="D:\TRUCK93A\data.mdb"` |

⚠ **ต้องปิด Uroboros ตัวเก่า (`C:\Uroboros-Mini`) ก่อนเปิดตัวใหม่** ไม่งั้น 2 ตัว upload chem.db ทับกัน

---

## F7 — คาดการณ์ระดับถัง (Predictor) + อัตราจ่ายสารจริง (26/09, 03/10)

### ความเป็นมา
API ระดับถังเป็น snapshot ราย 2 ชม. (ชั่วโมงเลขคู่) ออกช้า ~1 ชม. (เช่น รอบ 20:00 มี `updated_at` 21:13) → ระหว่างรอบไม่รู้ระดับจริง; ช่วงติดตั้งใหม่ประวัติ API มีรอบเดียว คาดการณ์ ALUM A ค้าง 180.4 ไม่ลด (ผู้ใช้เห็นว่า "คาดการณ์ ALUM ผิด")

### วัตถุประสงค์
แสดงระดับคาดการณ์ปัจจุบัน = ฐานล่าสุด ลดตามอัตราใช้ เพื่อตัดสินใจสั่งเติม

### หลักการ (ผู้ใช้ยืนยัน)
1. **ฐาน** = ค่าที่ใหม่กว่าระหว่าง API ล่าสุด กับ "ระดับหลังเติม" ล่าสุดที่บันทึกใน sidecar (ใช้**เวลาที่กดบันทึก**)
2. **อัตราใช้** (เฉพาะถังที่ ON; ไม่ ON = 0) ลำดับความสำคัญ (หลัง 03/10):
   1. **อัตราจ่ายจริง** (flow × dose) ถ้า > 0
   2. **เฉลี่ยประวัติ API** — ถ่วงเวลาจากทุกช่วงภายใน `rateWindowHours` (24) ที่ระดับลดลง; ข้ามช่วงที่ระดับเพิ่ม/เท่าเดิม (= เติม), ช่วง > 6 ชม., ช่วงที่ถังพักทั้งสองปลาย
   3. **`defaultUsagePerHour[สาร]` ÷ จำนวนถังของสารนั้นที่ ON**
3. **ค่าคาดการณ์** = `clamp(ฐาน + อัตรา × ชม.ที่ผ่านไป, 0, max(ความจุ, ฐาน))`

### อัตราจ่ายจริง (03/10)
- configID อัตราจ่ายตรง (Alum 87/90 L/hr, 149/152 L/min; PACl 8401/8403/8415/8417) **ยังไม่มีใน API** จึงคำนวณแบบ BellBeast (`CHEMview.js`):
  ```
  L/ชม. = flow × dose(mg/L) × factor
  flow: Ph1 = ID 1, Ph2 = ID 2 (m³/s); Ph3 = ID 39, Ph4 = ID 40 (m³/h → ÷3600)
  ALUM dose 89/92/151/154  factor 5.4135
  PACL dose 8402/8404/8416/8418  factor 8.6434
  ```
- ถังร่วม: Ph1-2 → ถัง A/B, Ph3-4 → ถัง C/D; อัตรารวมกลุ่ม ÷ จำนวนถังที่ ON ในกลุ่ม
- dose ว่าง = เฟสนั้นไม่จ่าย (0); flow ไม่มี = ไม่ทราบ; กลุ่มรวม = 0 → fallback
- API ล่ม: ใช้อัตรารอบล่าสุดได้ ≤ 6 ชม. (`MaxDoseAgeHours`)
- ค่าจริง ณ 03/10 ~13:30: ALUM Ph1+2 = 0 (ไม่มี dose 89/92 → ใช้วิธีเดิม), ALUM Ph3+4 ≈ 0.71, PACL Ph1+2 ≈ 2.25, PACL Ph3+4 ≈ 1.12 m³/ชม. — ผู้ใช้ยืนยัน "คำนวณได้ถูกต้อง"
- ข้อสังเกต: ระดับ PACL A ช่วง 30/09 ลดจริงเฉลี่ย ~1.08 m³/ชม. (ต่างจาก 2.25 ราว 2 เท่า) — ถ้าคาดการณ์คลาด ปรับ `dosing.*.factor` ใน config ได้โดยไม่แก้โค้ด

### การแสดงผลกราฟ
- แท่งทึบ = API ล่าสุด (ไม่มี API → ค่าคาดการณ์แบบจาง); ตัวเลข % และ m³ ใต้แท่ง = ค่าคาดการณ์
- เส้นประ = คาดการณ์ — แสดงเมื่อ |คาดการณ์ − API| > 1% ของความจุ (ALUM 7.5 m³; PACL 0.25 m³)
- **เอาเส้นแดง "┄ ต่ำ" ออกแล้ว**; legend `━ API  ┅ คาดการณ์`; ตัวเลข % ยังเป็นสีแดงเมื่อ ≤ 20%
- `·HH:mm` ใต้แท่ง = รอบล่าสุดยังไม่เข้า แสดงค่ารอบก่อน
- tooltip บอกที่มาของอัตรา เช่น "อัตราจ่ายจริง Ph1+2 2.25 m³/ชม. ÷ 1 ถัง (flow×dose 12:00)", "เฉลี่ยจาก API n ช่วง", "ค่าเริ่มต้นใน config (ประวัติ API ยังไม่พอ)"
- **จังหวะ:** คำนวณคาดการณ์ใหม่ทุก 30 วิ; ดึง API ทุก 300 วิ; ประวัติ API เก็บที่ `data\api_history.jsonl`

### Workflow ก่อน / หลัง
- **ก่อน:** แสดงแค่ API ล่าสุด (เก่าได้ถึง ~3 ชม.); ประวัติไม่พอ → ค่าค้างไม่ลด; มีเส้น "ต่ำ" ที่ผู้ใช้ไม่ใช้
- **หลัง:** แสดงทั้ง API และคาดการณ์ที่ลดตามอัตราจริง/ประวัติ/ค่าเริ่มต้น พร้อมบอกที่มาของอัตรา; บันทึกหลังเติมอัปเดตฐานทันที

### Sequence test (ผู้ใช้ยืนยัน "โอเคแล้ว")
สมมติ: PACL D = ON ใช้ 1 m³/ชม.; PACL C = STBY ระหว่างเติม; API เข้าช้า ~1 ชม. 13 นาที
- **S1 ปกติ:** 20:00 API C=4, D=10 → 21:30 บันทึกหลังเติม C=16 → ฐาน C เป็น 16 ทันที (เส้นประ 16) → ~23:13 ค่า 22:00 (C=17) เข้า → ฐาน = API → เส้นประหาย; D ลดต่อเนื่อง อัตรา (10−8)/2 = 1
  - การเทียบ API กับบันทึก และการลดตามอัตรา **เกิดทุก 30 วิ** — สิ่งที่รอชั่วโมงเลขคู่มีแค่ค่า API ใหม่
- **S2 ไม่มี API รอบใหม่:** C ค้างที่ 16 จนมี API ใหม่; ถ้า C เปลี่ยนเป็น ON ใช้ default; D ลดไปเรื่อยๆ ไม่มีเพดานเวลา ต่ำสุด 0 — **ผู้ใช้ยอมรับ ไม่ต้องทำ max-age** (ถังหมดเร็วสุด ~12 ชม., กะ 8 ชม. มีคนจดอย่างน้อย 1 ครั้ง)
- **S3 ติดตั้งใหม่:** ใช้ `defaultUsagePerHour` จนมีประวัติ
- **S4 หลายถัง ON:** อัตราจากประวัติแยกรายถัง; default หารเท่ากัน
- **S5 STANDBY/REPAIR:** อัตรา 0; ระดับลดขณะพักทั้งสองปลายไม่นับ (กัน noise เซนเซอร์)

### ไฟล์ที่แก้
`TankData.cs` (`Predictor.Predict`, `DoseRates`, `ChemApi.ReadDoseRates`), `SidecarController.cs` (`TankViews`, `Dose`), `Config.cs` (`TankConfig.Phases`, `PhaseFlowConfig`, `DosingConfig`), `Ui/TankBarChart.cs`, `config.json`, `mock/sim.ps1`

---

## F8 — ปรับ UI ฟอร์มรับสาร + Mock แบบ cmd (08/10)

### ความเป็นมา
- รายการ "รถรอบันทึก" ยาวขึ้นจนดันฟอร์มลงไปไกล ต้องเลื่อนหาช่อง ถ.พ. เอง
- Mock เดิมใช้ Notepad แทนโปรแกรมชั่ง ต้องรันสคริปต์แยกเพื่อจำลองรถ

### วัตถุประสงค์
- รถเข้า → เลื่อนลงล่างสุด + focus ช่อง ถ.พ.; บันทึก → เลื่อนกลับบนสุด
- Mock ที่หน้าต่าง cmd ทำหน้าที่เป็น "โปรแกรมชั่ง" พร้อมเมนูจำลองรถ

### Workflow ก่อน / หลัง (ฟอร์ม)
| ก่อน | หลัง |
|---|---|
| รถเข้า → ฟอร์มเติม แต่ต้องเลื่อนหาช่องเอง | เลื่อนลงล่างสุด + focus ถ.พ. (เฉพาะเมื่อสลับมาแท็บนั้น ไม่แย่งแท็บที่กำลังพิมพ์) |
| บันทึกแล้วหน้าค้างตำแหน่งเดิม | เลื่อนขึ้นบนสุด |

- **ตั้งใจ:** ถ้าหน้าต่าง sidecar ไม่ active (พนักงานอยู่ในโปรแกรมชั่ง) → ไม่ดึง keyboard; ตั้ง focus เมื่อหน้าต่างถูก activate (ผู้ใช้ยังไม่ได้ตอบว่าต้องการดึง focus ทันทีหรือไม่)
- ผลข้างเคียง (แจ้งผู้ใช้แล้ว): ข้อความ "บันทึก … ครบแล้ว ✓" อยู่ท้ายฟอร์ม พอเลื่อนขึ้นจะไม่เห็น
- ไฟล์: `Ui/ReceivePanel.cs` (`FocusEntry()`, `OnWindowActivated()`, `ScrollToTop()`, `AfterLayout()`), `MainWindow.OnArrival`; ScrollViewer `Focusable=false`

### Mock "cmd = โปรแกรมชั่ง"
- `mock/cmd-mock.bat`: เมนู 1 ล้าง DB / 2 W1 สุ่ม / 3 W2 สุ่มจากรถค้าง / 4 list / 9 ปิด / 0 ออก; `cmd-mock.ps1` (setup/stub/sidecar/watchdog, `-WipeSidecar`); `sim.ps1` เพิ่ม `-Target`, `randin`/`randout`/`list`
- ข้อเท็จจริงที่ตรวจแล้ว: หน้าต่าง `conhost.exe cmd.exe` ให้ pid ของ cmd → `targetProcess="cmd"` ใช้ได้; Windows Terminal หาไม่เจอ จึงบังคับ conhost; `FindMainWindow` เลือกหน้าต่าง cmd ที่ใหญ่สุด (cmd อื่นอาจแย่ง); ตั๋วใหม่ `9ddHHmmss`/`8ddHHmmss`
- ปัญหาที่แก้: sidecar/watchdog เป็น admin → หน้าต่างไม่ elevate ปิดไม่ได้ ("Access is denied") → `cmd-mock.bat` self-elevate และ `Stop-SidecarProcs` ขอ UAC แทน (ยังไม่ได้ทดสอบซ้ำ)

### ผลทดสอบ
build 0 warning ✅, `--preview` ✅, mock inject + ตรวจว่า sidecar เกาะหน้าต่าง cmd ✅ — ⚠ ยังไม่เห็นการเลื่อน/focus และตัวเลือกวันบนจอจริง, ยังไม่ทดสอบผ่าน UI ว่า sidecar ขึ้นรถที่ inject; ❌ ยังไม่ลงเครื่องโรงงาน (ณ 08/10)

---

## F9 — Uroboros_mini ไม่ upload chem.db ของใหม่ (12/10)

### ความเป็นมา
ผู้ใช้รายงาน: โปรแกรมชั่งบันทึกรถใหม่แล้ว รอ > 15 นาที (3 รอบ) chem.db บน Drive ไม่มีของใหม่

### สาเหตุ
- **หลัก:** `SyncMdbToMediaAtStartupCore` ตัดสิน "มีของใหม่" จาก **mtime ของ data.mdb** เทียบกับ `timestamp=` ใน `config.ini` — ขณะ truck93a (VB6/Jet) เปิด data.mdb ค้าง NTFS อาจไม่อัปเดต mtime → `UpToDate` ทุกรอบ (ฝั่ง sidecar รู้ปัญหานี้จึงมีบังคับ query 30 วิ แต่ Uroboros ไม่มี)
- ผู้ใช้ยืนยันโดยแก้ timestamp + ลบ `chem.db.stamp` → รอบถัดไป upload ได้จริง
- **บั๊กรองที่พบระหว่างไล่โค้ด:**
  1. เขียน `timestamp` **ก่อน** Convert — ถ้าได้สำเนาที่เขียนไม่เสร็จแล้ว Convert ล้ม → รอบหน้า UpToDate วนใช้สำเนาเสีย
  2. `File.Copy` คง mtime ต้นทาง → stamp ของ Convert ไม่เปลี่ยน → skip → upload chem.db ตัวเก่าซ้ำ
  3. `File.Copy` ไม่แชร์การเขียน → อาจ sharing violation ขณะ truck93a เขียน

### Workflow ก่อน / หลัง
- **ก่อน:** ทุก 300 วิ: ดู mtime data.mdb → ไม่เปลี่ยน = ข้าม (แม้เนื้อไฟล์เปลี่ยน)
- **หลัง:** ทุก 300 วิ: คัดลอก data.mdb (~4 MB) ด้วย `FileShare.ReadWrite|Delete` → SHA-256 (16 hex แรก) → เทียบ `media\data.mdb.sha256` → ต่าง = แทนที่ + `Synced` / เหมือน = `UpToDate`

### รายละเอียดการแก้ (`MdbReaderSafe.cs`, `Program.cs`)
- `timestamp` ใน config.ini เหลือไว้อ้างอิงเท่านั้น
- `MediaMdbHash()` อ่านจากไฟล์ `.sha256` เท่านั้น — **ลบ `.sha256` = บังคับคัดลอกใหม่**
- Convert stamp = hash + sidecar stamp; Convert ล้ม → ลบ `.sha256` แล้ว rethrow
- `RunOnceAsync` `sourceStamp` = hash + sidecar stamp (เทียบ `LastUploadedStamp` ใน `uploader-state.json`)
- รอบแรกหลังลงตัวใหม่จะ `Synced` + upload 1 ครั้งเสมอ (ปกติ); log มี `hash=...`

### ผลทดสอบ (ออฟไลน์ 6/6)
ครั้งแรก Synced ✅ · ไม่เปลี่ยน UpToDate ✅ · **เนื้อเปลี่ยนแต่ mtime เท่าเดิม → Synced** ✅ · ไม่เปลี่ยนอีกรอบ ✅ · ไฟล์ถูกเปิดเขียนค้าง → Synced ✅ · ลบ `.sha256` → Synced ใหม่ ✅
**ไม่ได้ทดสอบ** ขั้น Convert/Upload (ต้องใช้ ACE x64 + Google token); ผู้ใช้ deploy ที่โรงงานแล้ว 12/10 แต่**ยังไม่ได้ดู log จริง**

### หมายเหตุ publish
ต้อง publish ลง**โฟลเดอร์ว่าง** + `-p:RuntimeFrameworkVersion=10.0.8` (SDK 10.0.102 default เป็น 10.0.2 แต่ deploy เดิมเป็น 10.0.8 — publish ทับทำให้ runtime ปนกัน)

---

# ภาค 3 ข้อมูลอ้างอิงรวม

## 3.1 ตารางบั๊ก/ปัญหาทั้งหมด

| Feature | ID | ระดับ | สรุป | สถานะ |
|---|---|---|---|---|
| F2 | WD BUG-1 | mock | Notepad alias ตรวจไม่เจอบน Win11 | ✅ แก้ (ไม่กระทบเครื่องจริง) |
| F3 | BUG-1 | 🟡 | รถใหม่ดึงแท็บขณะพิมพ์ | ✅ |
| F3 | BUG-2 | 🔴 | async ใน constructor → ตรวจจับ/API พังเงียบหลัง restart | ✅ |
| F3 | BUG-3 | 🟠 | ขัดแย้งตอนชั่งออกมองไม่เห็น + audit ซ้ำ | ✅ |
| F3 | BUG-4 | 🔴 | record เปิดใน 2 ฟอร์ม (lost update) | ✅ |
| F3 | IMP-1 | 🟡 | ลบถังสารอื่นใน record v1 ไม่ได้ | ✅ |
| F3 | BUG-5 | 🔴 | ไฟล์ record เสีย → crash วน | ✅ |
| F3 | BUG-6 | 🟡 | ปี พ.ศ. (th-TH) | ✅ |
| F3 | BUG-7 | 🟠 | nag ไม่ลอยบนสุด | ✅ |
| F3 | BUG-8 | 🟡 | หัว nag ตัดบรรทัด/ว่าง 30 วิ | ✅ |
| F3 | BUG-9 | 🟡 | `sidecar.stopped` JSON ผิด | ✅ |
| F3 | BUG-10 | 🟠 | คลิก chip หาย (rebuild ทุก 3 วิ) | ✅ |
| F3 | IMP-2 | 🟡 | ฟอร์มตั๋วที่ยกเลิกค้าง | ✅ |
| F5 | — | 🟠 | ตาราง SIDECAR_* ว่าง (ไม่ upload ถ้า mdb ไม่เปลี่ยน) | ✅ |
| F6 | — | 🔴 | Launcher เปิดไม่ขึ้น (absolute path) | ✅ |
| F6 | — | 🟠 | UAC หลายครั้ง | ✅ |
| F6 | — | 🔴 | Uroboros ไม่ขึ้นหลัง re-publish (ไม่ self-contained) | ✅ |
| F6 | — | 🟠 | config.json sidecar หายจาก bundle | ✅ |
| F7 | — | 🟠 | คาดการณ์ค้างเมื่อประวัติ API ไม่พอ | ✅ (default rate → อัตราจ่ายจริง) |
| F8 | — | 🟡 | ปิด process admin จาก mock ไม่ได้ | ✅ แก้ (ยังไม่ทดสอบซ้ำ) |
| F9 | — | 🔴 | ไม่ upload ของใหม่เพราะ mtime ไม่ขยับ (+ บั๊กรอง 3 ข้อ) | ✅ แก้ (ยังไม่ยืนยันที่หน้างาน) |

## 3.2 สรุปผลทดสอบ

| ชุดทดสอบ | จำนวน | ผล |
|---|---|---|
| Watchdog unit + integration | 8 | ✅ ทั้งหมด |
| Sidecar v2 A–F (build, workflow, กฎ 5, คิว, docking, nag/admin exit) | ~40 เคส | ✅ ทั้งหมด (หลังแก้บั๊ก) |
| ลำดับรถ (G) + หน้าต่าง 24 ชม. (W1–W5) | 17 + 5 | ✅ |
| `--cctest` | 7 | ✅ 7/7 |
| `--aqtest` | — | ✅ ค่าเท่า v1 |
| `--storetest` / `--sidecar-test` | 14 records | ✅ |
| Predictor sequence S1–S5 | 5 | ✅ ผู้ใช้ยอมรับ |
| Uroboros hash sync ออฟไลน์ | 6 | ✅ |

**วิธีทดสอบ:** mock harness (Notepad/cmd แทน truck93a, `mock.mdb`, stub Aquadat `localhost:18080`), UI Automation, self-test modes (`--preview`, `--aqtest`, `--cctest`, `--storetest`, `--sidecar-test`) — ทั้งหมดบน Win11 เครื่อง dev

## 3.3 Edge cases (รวม)

| กรณี | พฤติกรรม | สถานะ |
|---|---|---|
| คาดการณ์ติดลบ/เกิน | clamp 0 – max(ความจุ, ฐาน) | ✅ |
| ฐานเกินความจุอยู่แล้ว (PACL B 25.2/25) | แสดง 101% ไม่ตัด | ตั้งใจ |
| API ขาดส่งนาน | ลดต่อถึง 0, ไม่มี max-age | ✅ ผู้ใช้ยอมรับ |
| กดบันทึกหลังเติม**ช้า** (หลัง API รอบใหม่เข้าแล้ว) | เวลากดบันทึกใหม่กว่า → ฐานบันทึกทับค่า API จริง | ⚠ ผู้ใช้โอเค (ทางแก้: ให้กรอกเวลาเติมเสร็จ) |
| E6: PACL D หลังเติม 12 < API 19.7 | น่าจะเป็นข้อมูลทดสอบ | ❓ รอคำตอบ |
| E7: PACL C 2 เที่ยวห่าง 23 วิ (ก่อนเติมคันที่ 2 ≠ หลังเติมคันแรก) | น่าจะเป็นข้อมูลทดสอบ | ❓ รอคำตอบ |
| Uroboros อ่านขณะ sidecar เขียน | backup API + busy_timeout 30 วิ | ✅ |
| sidecar.db ไม่มี / snapshot ล้ม | WARN แล้ว upload ต่อ | ✅ |
| sidecar.db / JSON เก่าเสีย | ย้ายเป็น `.bad_<ts>` / ไม่นำเข้า + toast | ✅ |
| ลบแถวใน sidecar.db | หายจาก Drive ด้วย | ⚠ ห้ามลบเอง |
| Uroboros 2 ตัวรันพร้อมกัน | upload ทับกัน | ⚠ ปิดตัวเก่า |
| ไม่มี `D:\TRUCK93A` | Launcher เตือนแต่เปิดตัวอื่นต่อ; Watchdog รอ 60 วิแล้วปิด | ตั้งใจ |
| ตั๋ว STAT=1 ที่ถูกทิ้ง | แสดงเป็น chip 24 ชม. (ซ่อนด้วย ✕) | เฝ้าดูที่หน้างาน |

## 3.4 ข้อเท็จจริงทางเทคนิคที่ยืนยันแล้ว

- **data.mdb ตาราง `WDATA`:** `STAT` '1' = ชั่งเข้า/ยังไม่ออก, '2' = ชั่งออกแล้ว; Track ID = `TICKET1` (10 หลักเติม 0) คู่กับ `W1`; `TICKET2`/`W2` มีหลังชั่งออก; `DAYIN/DAYOUT` = วันที่, `TMIN/TMOUT` = เวลา (text); `PRODUCT` 01 ALUM / 02 ทั่วไป / 03 PACL; พนักงานบางครั้งจดถังใน `REMARK1/2`; TICKET1/TICKET2 เป็นเลขรันแยกกัน
- **Chem summary API:** `http://172.16.198.150:443/api/chem/summary` (HTTP ธรรมดาบนพอร์ต 443); `Aq[id] = {ts, value, value_text, updated_at}`; snapshot ราย 2 ชม. ชั่วโมงเลขคู่ ออกช้า ~1 ชม.; ถังที่ ON อาจมีสถานะแต่ยังไม่มีระดับ
- **ความจุถัง:** Alum A/B/C/D = 750 m³; PACL A/B/C/D = 25/25/25/23 m³
- **configID Aquadat:** 8037 (Alum A+B), 8036 (Alum C+D), 8423 (PACL A+B), 8424 (PACL C+D)
- **mtime ของ data.mdb อาจไม่ขยับ** ขณะ truck93a เปิดไฟล์ค้าง (ยืนยันจาก F9) → ต้องใช้บังคับ query / hash

## 3.5 Config สำคัญ

**TruckSidecar `config.json`**
| key | ค่า | ความหมาย |
|---|---|---|
| `mdbPath` | `D:\TRUCK93A\data.mdb` | data.mdb ของโปรแกรมชั่ง |
| `targetProcess` | `truck93a` (mock: `notepad`/`cmd`) | หน้าต่างที่เกาะ |
| `watchSeconds` / `forceCheckSeconds` | 3 / 30 | รอบตรวจ / บังคับ query |
| `inWindowHours` | 24 | หน้าต่างตรวจจับชั่งเข้า |
| `productMap` | `01→ALUM`, `03→PACL` | แท็บตาม PRODUCT |
| `glass` | `blur` / `acrylic` / `off` | สไตล์ UI |
| `apiPollSeconds` | 300 | ดึง API ระดับถัง |
| `lowLevelPercent` | 20 | ตัวเลข % สีแดง |
| `rateWindowHours` | 24 | หน้าต่างเฉลี่ยอัตราใช้ |
| `defaultUsagePerHour` | ALUM 1.25, PACL **1.0** (m³/ชม. ทั้งระบบ) | อัตราสำรอง (ที่มา: WDATA 180 วัน, SG สมมติ 1.3/1.2; PACL เดิม ≈0.75 เปลี่ยนเป็น 1.0 เมื่อ 03/10) |
| `phaseFlow`, `dosing`, `phases` (ต่อถัง) | ดู F7 | อัตราจ่ายจริง |
| `aquadatUrl`, `aquadatEnrollUrl`, `aquadatUsername` | — | Aquadat (username ใช้เป็น placeholder เท่านั้นหลัง 08/10) |
| `centralApiUrl` | ว่าง | hook สำรอง (ปิด) |
| ความกว้างหน้าต่าง | 400 px | |

**`launcher.json`:** `truckExe`=`D:\TRUCK93A\truck93a.exe`, `sidecarExe`=`..\TruckSidecar\TruckSidecar.exe`, `watchdogExe`=`..\TruckWatchdog\TruckWatchdog.exe`, `uroborosExe`=`..\Uroboros_mini\Uroboros.MiniUploader.exe`
**`watchdog.json`:** `sidecarExe`=`..\TruckSidecar\TruckSidecar.exe`, `pollSeconds`=5, `startupGraceSeconds`=60 (option `stopFlagPath`)
**Uroboros_mini `mini-uploader.json`:** `PollIntervalSeconds`=300, `ScheduleWindowMinutes`=0, `IotEnabled`=true (+IoT), `SidecarDb`=`..\TruckSidecar\data\sidecar.db`; `config_\config.ini` `mdb_file_location="D:\TRUCK93A\data.mdb"`

**ไฟล์ข้อมูลของ Sidecar (`data\`)**: `sidecar.db` (record + audit), `aquadat_outbox.db`, `api_history.jsonl`, `dismissed.json`, `sidecar.stopped` (ธง admin exit), `records_migrated_*` (JSON เดิม)
**ไฟล์ของ Uroboros_mini**: `media\data.mdb`, `media\data.mdb.sha256`, `media\chem.db.stamp`, `uploader-state.json`, `logs\`, `credentials.json`, `token_store\`

## 3.6 Build / Deploy

- **dotnet:** x64 `C:\Program Files\dotnet\dotnet.exe` (build/publish ทุกตัว); x86 `C:\Program Files (x86)\dotnet\dotnet.exe` (รัน dll ของ TruckSidecar สำหรับ self-test)
- **Publish Sidecar/Launcher/Watchdog:** `dotnet publish <csproj> -c Release -o <proj>\publish` (single-file win-x86 self-contained)
- **Publish Uroboros_mini:** ลงโฟลเดอร์ว่าง + `-p:RuntimeFrameworkVersion=10.0.8`
- **Deploy:** `stage.ps1` (dev → `_deploy\`), `deploy.ps1` (copy `publish\` → ปลายทาง, additive, ไม่ทับ `data\ logs\ media\ uploader-state.json`, secrets ใส่เฉพาะถ้ายังไม่มี); config ปลายทาง**ถูกทับจาก `publish\` ทุกครั้ง → แก้ config ที่ source เสมอ**; backup ก่อน deploy ไว้ที่ `Sidecar_prj\_backup\`
- **ลำดับปิดก่อนอัปเดต:** ปิด TruckWatchdog ก่อน TruckSidecar (ไม่งั้นถูกเปิดกลับ)
- **exe ทั้งหมดเป็น requireAdministrator** → self-test รันผ่าน `dotnet <dll>`
- **Self-test:** `--preview <dir>`, `--aqtest out.txt`, `--cctest out.txt <TICKET1>`, `--storetest <dataDir> <out.txt>`, Uroboros `--sidecar-test`
- **กับดัก:** `.bat` ต้อง CRLF; `.ps1` ที่มีภาษาไทยต้อง UTF-8 มี BOM (PowerShell 5.1); JSON path ต้อง `\\`; Git Bash heredoc กิน backslash; Jet OLEDB ต้องรันใน PowerShell x86 (`sim.ps1` รีสตาร์ตตัวเองเป็น x86); single-instance mutex ค้างทำให้เปิดใหม่ออกเงียบ → kill ก่อน

## 3.7 บทเรียน (Lessons learned)

1. ห้ามเริ่ม async ที่แตะ UI ใน constructor ของหน้าต่าง WPF (ก่อน `app.Run()`)
2. วันที่ที่เขียนลงไฟล์/payload ต้องใช้ `InvariantCulture` (เครื่องเป็น th-TH)
3. รายการ UI ที่ refresh ตาม timer ต้องไม่สร้างปุ่มใหม่ถ้าข้อมูลไม่เปลี่ยน
4. โค้ดที่รันก่อน `app.Run()` ไม่มี global exception handler — ต้องมี try/catch เอง
5. WPF `Topmost` เชื่อไม่ได้กับหน้าต่างนี้ ใช้ Win32 ตรง
6. อย่าใช้ mtime ตัดสินว่า data.mdb เปลี่ยน (VB6/Jet เปิดค้าง) — ใช้ hash หรือบังคับ query
7. บันทึก "สถานะแล้ว" (timestamp/stamp) **หลัง**ขั้นตอนสำเร็จ ไม่ใช่ก่อน
8. self-contained ต้องกำหนดใน csproj และ publish ลงโฟลเดอร์ว่าง กัน runtime ปน

## 3.8 งานค้าง / ยังไม่ได้ยืนยัน (รวมจากทุก handoff, ตัดข้อที่ทำแล้วออก)

**ต้องตรวจที่เครื่องโรงงาน**
1. glass `blur` บน Win10 ใช้ได้ไหม / `acrylic` หน่วงไหม
2. Nag mode และ Ctrl+Shift+F12 กับหน้าต่าง truck93a จริง (ผ่านกับ Notepad แล้ว)
3. เวอร์ชัน SQLite (`sidecar.db`): migrate จริง, เขียน/อ่านพร้อมกัน, upload จริง (ดู `records_migrated_*`, log `[SIDECAR] Merged N`, query `SIDECAR_UNLOAD` ใน chem.db)
4. อัตราจ่ายจริง: เห็นข้อความ "อัตราจ่ายจริง Ph1+2 …" ที่ PACL A, เทียบคาดการณ์กับ API 2–3 รอบ
5. การเลื่อน/focus ฟอร์ม และตัวเลือกวันส่ง Aquadat (F8) — ณ 08/10 ยังไม่ลงโรงงาน
6. Uroboros hash sync: log `Synced ... hash=` → `[MDB] Convert OK` → `Upload done` ภายใน ~5 นาทีหลังชั่งรถ
7. Login Aquadat จริง (ผู้ใช้พิมพ์รหัสเอง)

**รอผู้ใช้ตัดสินใจ**
8. E6/E7 เป็นข้อมูลทดสอบไหม — ถ้าใช่ ลบใน `sidecar.db` (ลบ UNLOAD แล้ว FILL ลบตาม CASCADE)
9. เอาตัวเลข % สีแดง (≤ 20%) ออกด้วยไหม
10. ดึง focus มาที่ sidecar ทันทีตอนรถเข้า / รวม "วันนี้" ในตัวเลือกส่งยอด / แสดงผลบันทึกเป็น toast
11. ตัวจับเวลาส่ง Aquadat อัตโนมัติ เปิดไว้หรือให้กดปุ่มเท่านั้น
12. ใส่ timeout ให้ `SidecarController.ScanScaleAsync` (ธง `_scanning` ค้าง true ถ้า copy/query ค้าง → ไม่ตรวจตาชั่งอีกจนเปิดใหม่; `SidecarController.cs:89`, `ScaleDb.Query`)

**งานเทคนิค**
13. อัปเกรด `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (NU1903 ช่องโหว่ high) ใน Uroboros_mini
14. ใส่ `<RuntimeFrameworkVersion>10.0.8</RuntimeFrameworkVersion>` ถาวรใน csproj ของ Uroboros_mini
15. อัปเดตเอกสารที่ยังอ้าง JSON: `docs\architecture.md`, Uroboros_mini `HANDOFF.md` (`SidecarRecordsDir` → `SidecarDb`)
16. ถ้า API เพิ่ม configID อัตราจ่ายตรง (87, 90, 149, 152, 8401…) ต้องเพิ่มโค้ดอ่าน (ระวังหน่วย L/min ของ 149/152)
17. `mock\run\config.json` ยังเป็นของเก่า (สร้างใหม่เมื่อรัน `sim.ps1` setup); ทดสอบปิด process admin จาก mock ซ้ำ
18. อ่านหน้าจอโปรแกรมชั่งสด (เกินกว่า picker) — ต้องมีโปรแกรมจริง + dongle

**อนาคต / พักไว้**
19. Integration ระบบกลางเพิ่มเติมผ่าน `centralApiUrl` ("ค่อยว่ากัน")
20. อัปเดตรายงาน `Sidecar_prj_deploy\TruckSidecar_Change_Report_*.docx` ถ้าต้องการ

## 3.9 แผนที่ไฟล์ source (TruckSidecar v2)

| ไฟล์ | หน้าที่ |
|---|---|
| `Program.cs` | bootstrap WPF, single-instance mutex, โหมด self-test |
| `SidecarController.cs` | ตรรกะทั้งหมดที่ไม่ใช่ UI: ตรวจจับ (กฎ 2/4), crosscheck (กฎ 5), commit, chem API, Aquadat, `TankViews`, `Dose` |
| `ScaleWatcher.cs` | เฝ้า data.mdb ทุก 3 วิ + บังคับ query 30 วิ |
| `ScaleDb.cs` | อ่าน WDATA จากสำเนาชั่วคราว (`DetectAsync`, `ByTicket1Async`, `CandidatesAsync`) |
| `RecordStore.cs` | SQLite `sidecar.db` (UNLOAD/FILL/AUDIT), migrate JSON, `DismissedSet` |
| `TankData.cs` | ดึง API, ประวัติระดับ, `Predictor`, `DoseRates` |
| `Interpolation.cs` | แบ่งน้ำหนักตามปริมาตรเติม รวมตาม configID |
| `Aquadat.cs` / `AquadatLogin.cs` | outbox + GraphQL client / login แบบโต้ตอบ |
| `Native.cs` | Win32: หาหน้าต่าง, DWM bounds, monitor, AppBar, topmost |
| `Config.cs` + `config.json` | การตั้งค่า |
| `Ui/MainWindow.cs` | หน้าต่าง, docking/AppBar, nag, แท็บ, toast, admin exit |
| `Ui/ReceivePanel.cs` | chip "รถรอบันทึก" + ฟอร์ม "บันทึกการรับ" (ต่อแท็บสาร), เลื่อน/focus |
| `Ui/AquadatPanel.cs` | แท็บส่งยอด, login, เลือกวัน |
| `Ui/TankBarChart.cs` | กราฟระดับถัง + คาดการณ์ |
| `Ui/ScalePickerWindow.cs`, `GlassDialog.cs`, `GlassBackdrop.cs`, `Kit.cs`, `Glass.xaml` | picker, dialog, glass, ชุด UI |
| `AqSelfTest.cs`, `CrossCheckSelfTest.cs`, `StoreSelfTest.cs` | `--aqtest`, `--cctest`, `--storetest` |
| `_backup_v1\` | source v1 WinForms (ไม่ถูก build) |
| `mock\` | mock harness (Notepad / cmd), `sim.ps1`, stub Aquadat |

Uroboros_mini: `MdbReaderSafe.cs` (sync/convert, DDL), `SidecarMerge.cs` (snapshot/ATTACH/stamp), `Program.cs` (`RunOnceAsync`)
เอกสารอื่นที่ยังอยู่: `docs\architecture.md` (§4.4, §4.5, §5), `mock\README.md`, `TruckWatchdog\README.md`, `TruckLauncher\README.md`
