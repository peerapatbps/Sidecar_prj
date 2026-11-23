# Handoff — Sidecar_prj (2026-09-26)

ต่อจาก [handoff2509.md](handoff2509.md) (พื้นหลัง sidecar / watchdog / launcher) และ [sequence_qa_testcase.md](sequence_qa_testcase.md) (QA ฟอร์มรับสาร / crosscheck กฎ 1–5) — ไฟล์นี้ครอบคลุมเฉพาะสิ่งที่เปลี่ยนวันที่ 26/09/2026 ซึ่งมี **2 รอบ (session)**:

- **รอบ 1 (เช้า–เย็น):** รวม record sidecar เข้า `chem.db` ของ Uroboros_mini, ย้าย 4 โปรเจกต์มาไว้โฟลเดอร์เดียว, launcher เปิดครบ 4 ตัว, สคริปต์ deploy — §1
- **รอบ 2 (ค่ำ):** แก้ปัญหาที่เครื่องใช้งาน, สิทธิ์ admin, อัตราคาดการณ์, **เปลี่ยน record จาก JSON → SQLite `sidecar.db`**, sequence test ของการคาดการณ์ระดับถัง — §2–§6

> ⚠ หลายอย่างในรอบ 1 **ถูกแทนที่แล้ว** ในรอบ 2 (ทำเครื่องหมาย ~~ขีดฆ่า~~ / "แทนที่โดย §x") — ถ้าขัดกัน ให้ถือ §2 ขึ้นไปเป็นหลัก

---

## 0. สถานะปัจจุบัน (อ่านตรงนี้ก่อน)

| เรื่อง | สถานะ |
|---|---|
| Sidecar ทำงานที่เครื่องใช้งาน | ✅ ผู้ใช้ยืนยัน (มี screenshot ALUM/PACL) |
| Uroboros_mini upload `chem.db` ขึ้น Drive พร้อมตาราง `SIDECAR_*` มีข้อมูล | ✅ ผู้ใช้ยืนยันด้วย sqlite3 (2 เที่ยวจริง PACL) — **ยังเป็นเวอร์ชัน JSON** |
| เวอร์ชัน SQLite (`sidecar.db`) | ✅ build + self-test ผ่านบนเครื่อง dev, deploy ลง bundle แล้ว — **ยังไม่ได้รันที่เครื่องใช้งาน** |
| อัตราคาดการณ์ (เฉลี่ย + ค่าเริ่มต้น config) | ✅ build ผ่าน — ยังไม่เห็นผลจริงบนหน้าจอเครื่องใช้งาน |
| เอาเส้น "┄ ต่ำ" ออกจากกราฟ | ✅ build ผ่าน |
| ผู้ใช้ยอมรับพฤติกรรมคาดการณ์ทั้งหมด (§5) | ✅ "เราโอเคแล้ว" — **ไม่ต้องเพิ่ม max-age / เพดานเวลา** |

**Bundle ที่ส่งไปเครื่องใช้งาน:** `C:\Users\peera\OneDrive\Desktop\Sidecar_prj\Sidecar_prj\` (4 โฟลเดอร์ + `deploy.ps1`) → ผู้ใช้ก็อปเองไป `D:\Sidecar_prj\`

---

## 1. รอบ 1 — สรุป (ส่วนที่ยังเป็นจริง)

- **โครงโฟลเดอร์ dev:** `C:\Users\peera\OneDrive\Desktop\Sidecar_prj\{TruckSidecar, TruckLauncher, TruckWatchdog, Uroboros_mini}` (บน Desktop เหลือ `TRUCK93A` legacy + `Uroboros` backend)
- **เครื่องใช้งาน:** `D:\TRUCK93A\` (`truck93a.exe` + `data.mdb`) และ `D:\Sidecar_prj\<4 โปรเจกต์>\`
- **Integration concept:** Uroboros_mini แปลง `data.mdb` → `chem.db` แล้ว merge ข้อมูล sidecar เข้าไปก่อน upload Drive; Uroboros backend ดึง `chem.db` แล้ว **แทนที่ทั้ง DB** (full swap)
- **Schema เพิ่มใน `chem.db`:** `WDATA` มี `TICKET1`/`TICKET2`; `SIDECAR_UNLOAD` (PK `ID`, soft FK `TICKET1`→`WDATA.TICKET1` เพราะ WDATA มีแค่ STAT='2'); `SIDECAR_FILL` (FK จริง `UNLOAD_ID`→`SIDECAR_UNLOAD.ID`, `ON DELETE CASCADE`). DDL อยู่ใน `Uroboros_mini\MdbReaderSafe.cs` (`CreateSql`)
- **TruckLauncher เปิดครบ 4 ตัว:** truck93a → sidecar → watchdog → uroboros (uroboros ท้ายสุด)
- **Uroboros_mini = `WinExe`** (ไม่มี cmd), log อยู่ `logs\`
- **สคริปต์:** `Sidecar_prj\stage.ps1` (dev → `_deploy\`) และ `deploy.ps1` (copy `<proj>\publish` → ปลายทาง, additive, ไม่ทับ `data\ logs\ media\ uploader-state.json`, secrets ใส่เฉพาะถ้ายังไม่มี). รายละเอียดดูหัวไฟล์สคริปต์
- เขียนละเอียดรอบ 1 เพิ่มเติม: Uroboros_mini `HANDOFF.md` §"TruckSidecar integration" (⚠ ส่วน JSON / `SidecarRecordsDir` ในไฟล์นั้น **ล้าสมัยแล้ว** — ดู §3)

~~Trigger = เวลาไฟล์ MDB อย่างเดียว~~ → แทนที่โดย §3.3
~~Sidecar เขียน `data\records\*.json`, `SidecarRecordsDir`, DTO ใน `SidecarMerge.cs`~~ → แทนที่โดย §3
~~Config ใช้ absolute path `D:\Sidecar_prj\...`~~ → แทนที่โดย §2.1
~~Uroboros ต้องกด UAC ทุกครั้ง แยกจากตัวอื่น~~ → แทนที่โดย §2.2

---

## 2. รอบ 2 — การแก้ไขฝั่ง deploy / launch

### 2.1 Launcher เปิด sidecar / uroboros ไม่ขึ้น → path relative
- **อาการ:** รัน Launcher จากโฟลเดอร์ Desktop แล้ว sidecar + uroboros ไม่ขึ้น
- **สาเหตุ:** config ชี้ `D:\Sidecar_prj\...` แต่เครื่องที่รันไม่มี `D:` → Launcher ขึ้นกล่อง "ไม่พบ ..." แล้วข้าม (ไม่ abort)
- **แก้:** เปลี่ยนเป็น relative (Launcher/Watchdog resolve จากโฟลเดอร์ exe ของตัวเอง — `TryResolve` ใช้ `AppContext.BaseDirectory`):
  - `launcher.json`: `sidecarExe`=`..\TruckSidecar\TruckSidecar.exe`, `watchdogExe`=`..\TruckWatchdog\TruckWatchdog.exe`, `uroborosExe`=`..\Uroboros_mini\Uroboros.MiniUploader.exe`
  - `watchdog.json`: `sidecarExe`=`..\TruckSidecar\TruckSidecar.exe`
  - `truckExe` **คงเป็น** `D:\TRUCK93A\truck93a.exe` (โปรแกรมเดิม อยู่นอก Sidecar_prj)
- แก้ใน **source** ของแต่ละโปรเจกต์ด้วย เพราะ `deploy.ps1` ทับ config จาก `publish\` ทุกครั้ง

### 2.2 ทุกตัวรันด้วยสิทธิ์ admin
- เพิ่ม `app.manifest` (`requireAdministrator`) ให้ **TruckLauncher, TruckWatchdog** (ใหม่ + `<ApplicationManifest>` ใน csproj) และ **TruckSidecar** (เพิ่ม `trustInfo` ใน manifest เดิม). Uroboros มีอยู่แล้ว
- **ผล:** UAC เด้ง **ครั้งเดียว** ที่ Launcher → ลูกทุกตัว (รวม truck93a) ได้สิทธิ์ admin ตามโดยไม่เด้งซ้ำ; Watchdog เปิด sidecar กลับได้โดยไม่มี UAC
- **Edge:** เปิด sidecar / watchdog ตรงๆ (ไม่ผ่าน Launcher) → UAC เด้งทุกครั้ง
- ตรวจ manifest ใน exe ที่ publish แล้วด้วยการค้น string `requestedExecutionLevel level="requireAdministrator"` — ผ่านทั้ง 4

### 2.3 Uroboros_mini ไม่ขึ้นหลัง re-publish (regression ที่ผมทำเอง — แก้แล้ว)
- **สาเหตุ:** csproj ไม่มี `<SelfContained>` → `dotnet publish` ธรรมดาได้ `runtimeconfig.json` แบบ framework-dependent แต่ bundle ยังมี runtime ชุด self-contained เก่าอยู่ (robocopy additive ไม่ลบ) → ไม่เข้ากัน สตาร์ทไม่ได้
- **แก้:** เพิ่ม `<SelfContained>true</SelfContained>` ใน `Uroboros.MiniUploader.csproj` → ได้ `includedFrameworks` 10.0.8
- **วิธีตรวจ:** `Uroboros.MiniUploader.runtimeconfig.json` ต้องมี `"includedFrameworks"` ไม่ใช่ `"framework"`

### 2.4 Config ของ Uroboros_mini = ค่าเดียวกับตัวที่ใช้งานจริง
เทียบกับตัวเก่าที่รันบนเครื่องใช้งาน (สำเนาที่ `C:\Users\peera\OneDrive\Desktop\Uroboros-Mini`, บนเครื่องจริงอยู่ที่ `C:\Uroboros-Mini`) แล้วใส่ค่าเดิมใน source `mini-uploader.json`:
`PollIntervalSeconds`=300, `ScheduleWindowMinutes`=0, `IotEnabled`=true + `IotServerUrl`/`IotDeviceName`/`IotDeviceType`/`IotPollIntervalSeconds`=15 — ต่างจากตัวเก่าเฉพาะ `SidecarDb` (ใหม่, §3)
- `credentials.json` เหมือนกันทุกไบต์; ก็อป `token_store\` ตัวที่ refresh ล่าสุดจากตัวเก่ามาใส่ bundle แล้ว
- `config_\config.ini`: `mdb_file_location="D:\TRUCK93A\data.mdb"`, `timestamp=` ว่างโดยตั้งใจ (รอบแรก convert ใหม่ทั้งหมด)
- ⚠ **ต้องปิด Uroboros ตัวเก่า (`C:\Uroboros-Mini`) ก่อนเปิดตัวใหม่** ไม่งั้น 2 ตัว upload `chem.db` ทับกัน

### 2.5 config.json ของ sidecar หายจาก bundle
- bundle ไม่มี `TruckSidecar\config.json` → `mdbPath` ว่าง. ก็อปจาก source แล้ว (`mdbPath`=`D:\TRUCK93A\data.mdb`) — deploy รอบหลังๆ มาจาก `publish\` ที่มีไฟล์นี้อยู่แล้ว

---

## 3. รอบ 2 — เปลี่ยน record จาก JSON → SQLite `sidecar.db` (ตามแบบที่ผู้ใช้เสนอ)

### 3.1 ทำไม
ผู้ใช้เสนอ: เก็บเป็น SQLite แล้วตอนแปลง `data.mdb` → `chem.db` ค่อย merge ตารางเข้าไป → ได้ `chem.db` ฉบับสมบูรณ์. ข้อดี: merge = `ATTACH` + `INSERT…SELECT`, ไฟล์เดียว (ไม่สะสมไฟล์ละเที่ยว), query ที่หน้างานได้ทันที, schema เดียวกับ chem.db

### 3.2 TruckSidecar (`RecordStore.cs`)
- ไฟล์: `data\sidecar.db` ตาราง `SIDECAR_UNLOAD` + `SIDECAR_FILL` (**DDL เดียวกับ chem.db ทุกคอลัมน์** — ค่าคงที่ `RecordStore.Schema`) + `SIDECAR_AUDIT` (แทน `audit.jsonl`; ไม่ถูก merge เข้า chem.db)
- API ของ `RecordStore` ที่คลาสอื่นเรียกใช้ **ไม่เปลี่ยน** (in-memory dictionary เหมือนเดิม เปลี่ยนแค่ persistence)
- `Save` = upsert UNLOAD + ลบ/เขียน FILL ใหม่ + audit **ใน transaction เดียว**, `synchronous=FULL`, `Pooling=false` (ไม่ถือ handle ค้าง)
- เวลาเก็บเป็น `yyyy-MM-dd HH:mm:ss` (เท่ากับ chem.db — ตัดเศษวินาที)
- **ย้ายข้อมูลอัตโนมัติ:** เปิดครั้งแรก นำเข้า `data\records\*.json` (เฉพาะที่ยังไม่มี หรือ Version ใหม่กว่า) → ย้ายโฟลเดอร์ไป `data\records_migrated_<yyyyMMddHHmmss>` (**ไม่ลบ**) + audit `migrate-json`
- self-test ใหม่: `TruckSidecar.exe --storetest <dataDir> <out.txt>` (`StoreSelfTest.cs`) — **ห้ามชี้ไปที่ data\ ของเครื่องใช้งาน** (มันแก้ record แรกเพื่อทดสอบ)

### 3.3 Uroboros_mini (`SidecarMerge.cs` เขียนใหม่ทั้งไฟล์, `MdbReaderSafe.cs`, `Program.cs`)
- config: `"SidecarDb": "..\\TruckSidecar\\data\\sidecar.db"` (แทน `SidecarRecordsDir`; relative → resolve จากโฟลเดอร์ exe ด้วย `ResolvePath`, ไม่ใช่ CWD)
- ขั้นตอนใน `ConvertToChemSqlite`: **Snapshot** (SQLite online-backup API จาก connection read-only, `busy_timeout` 30 s — ปลอดภัยแม้ sidecar กำลังเขียน) → **ATTACH ก่อนเปิด transaction** (SQLite ห้าม ATTACH ใน tx) → `INSERT OR REPLACE … SELECT` / `INSERT … SELECT` → commit → DETACH → ลบไฟล์ snapshot
- **Stamp (ตัวตัดสินว่าต้อง convert/upload ใหม่ไหม)** = เวลาไฟล์ MDB + `_sc{COUNT}v{SUM(VERSION)}s{SUM(SYNCED)}` จาก `SIDECAR_UNLOAD` — ใช้ทั้งใน `RunOnceAsync` (sourceStamp) และ `ConvertMediaMdbToChemDb` (stamp file). ไม่ใช้เวลาไฟล์ .db เพราะอาจไม่เปลี่ยนทันทีหลัง commit
- self-test: `Uroboros.MiniUploader.exe --sidecar-test [sidecar.db] [outDb]` (ไม่ใส่ path = ใช้ `SidecarDb` จาก config)

### 3.4 ปัญหาเดิมที่แก้ไปด้วย: ตาราง `SIDECAR_*` ใน chem.db ว่าง
- **สาเหตุ:** Uroboros convert/upload ใหม่**เฉพาะเมื่อ `data.mdb` เปลี่ยน** — record ที่บันทึกหลังรอบ mdb ล่าสุดจึงไม่ถูก merge จนกว่าจะมีรถชั่งคันถัดไป
- **แก้:** stamp รวมสถานะ sidecar (§3.3) — ทำตั้งแต่ยังเป็น JSON (ตอนนั้นใช้เวลาไฟล์ + จำนวนไฟล์) แล้วผู้ใช้ยืนยันว่าข้อมูลเข้า; เวอร์ชัน SQLite ใช้ COUNT/VERSION แทน

### 3.5 ผลทดสอบ (เครื่อง dev)
- ข้อมูล: JSON จริง 2 เที่ยวจากเครื่องใช้งาน (PACL, TICKET1 `0000014808`/`0000014809`) + mock 12 รายการ (`TruckSidecar\mock\run\data\records`)
- `--storetest`: นำเข้า 14/14, `LatestAfterReadings` ถูก, save+reopen v2→v3 **PASS**, โฟลเดอร์เดิมถูกย้ายเป็น `records_migrated_*`
- `--sidecar-test`: merge 14 UNLOAD + 14 FILL, NET_KG ของ 2 เที่ยวจริง = 14115 / 14605 (ตรงกับที่ผู้ใช้ query จาก chem.db บน Drive), stamp `_sc14v28s0`, ไม่มี snapshot ค้าง
- ❌ **ยังไม่ได้ทดสอบ:** รันจริงที่เครื่องใช้งาน (migrate ของจริง, sidecar เขียนพร้อม Uroboros อ่าน, upload จริง)

---

## 4. รอบ 2 — การคาดการณ์ระดับถัง (Predictor)

### 4.1 หลักการ (ผู้ใช้ยืนยันแล้ว)
**"คาดการณ์" = ใช้ข้อมูลที่บันทึก ก่อน/หลังเติม เป็นฐาน แล้วลดตามอัตราใช้**
1. **ฐาน (base)** = ค่าที่ใหม่กว่าระหว่าง API ล่าสุด (`ts` ของรอบ API) กับ "ระดับหลังเติม" ล่าสุดที่พนักงานบันทึกใน sidecar (`AfterRecordedAt` = **เวลาที่กดบันทึก**, ไม่ใช่เวลาเติมเสร็จจริง — ดู E5)
2. **อัตราใช้** (เฉพาะถังที่ status ปัจจุบัน = ON; ไม่ ON → 0):
   - เฉลี่ยถ่วงเวลาจาก**ทุกช่วง**ภายใน `rateWindowHours` (24) ที่ระดับลดลงและถัง ON ที่ปลายใดปลายหนึ่ง; ข้ามช่วงที่ระดับเพิ่ม/เท่าเดิม (= เติม), ช่วง > 6 ชม., ช่วงที่ถังพักทั้งสองปลาย
   - ถ้ายังไม่มีช่วงให้คำนวณ → `defaultUsagePerHour[สาร] ÷ จำนวนถังของสารนั้นที่ ON`
3. **ค่าคาดการณ์** = `clamp(base + rate × ชม.ที่ผ่านไปจากฐาน, 0, max(ความจุ, base))`
- โค้ด: `TankData.cs` (`Predictor.Predict`), `SidecarController.TankViews`, tooltip ใน `Ui\TankBarChart.cs` บอกที่มาของอัตรา ("เฉลี่ยจาก API n ช่วง" / "ค่าเริ่มต้นใน config")

### 4.2 Config ใหม่ (`TruckSidecar\config.json`)
```
"rateWindowHours": 24,
"defaultUsagePerHour": { "ALUM": 1.25, "PACL": 0.75 }   // m³/ชม. ทั้งระบบ
```
ประมาณจาก WDATA 180 วัน (`chem.db`): ALUM ~39 ตัน/วัน ÷ SG 1.3 ≈ 1.25; PACL ~22 ตัน/วัน ÷ SG 1.2 ≈ 0.75 — **SG เป็นค่าสมมติ** ผู้ใช้ปรับได้ (ข้อมูลผู้ใช้: 1 ถังใช้หมด ≥ 12 ชม.)

### 4.3 การแสดงผล (กราฟ)
- แท่งทึบ = API ล่าสุด (ถ้าไม่มี API ใช้ค่าคาดการณ์แบบจาง); ตัวเลข % และ m³ ใต้แท่ง = **ค่าคาดการณ์** (ถ้ามี)
- เส้นประ = ค่าคาดการณ์ — **แสดงเมื่อ |คาดการณ์ − API| > 1% ของความจุ** (ALUM 750 → 7.5 m³; PACL 25 → 0.25 m³)
- **เอาเส้นแดง "┄ ต่ำ" และคำอธิบายออกแล้ว** (ผู้ใช้ใช้แค่ API + คาดการณ์); legend = `━ API  ┅ คาดการณ์`. ตัวเลข % ยังเป็นสีแดงเมื่อ ≤ `lowLevelPercent` (20) — ยังไม่ได้ถามว่าจะเอาออกด้วยไหม
- "·HH:mm" ใต้แท่ง = ระดับรอบล่าสุดยังไม่เข้า แสดงค่ารอบก่อน (stale)

### 4.4 จังหวะเวลา (สำคัญต่อการอ่าน sequence test)
- หน้าจอคำนวณคาดการณ์ใหม่ **ทุก 30 วินาที** (`_minuteTimer`) — **ไม่ได้รอชั่วโมงเลขคู่**
- ดึง API ทุก `apiPollSeconds` (300 s); API เป็น snapshot **ราย 2 ชม. ชั่วโมงเลขคู่** (`PickHour`) แต่**ออกช้า ~1 ชม.** (ตัวอย่างจริง: รอบ 20:00 มี `updated_at` 21:13)
- ประวัติ API เก็บในเครื่องที่ `data\api_history.jsonl` (ติดตั้งใหม่ = ว่าง → ใช้ default rate จนกว่าจะมี ≥ 2 รอบที่ลดลง)

---

## 5. Sequence test — การคาดการณ์ (ไล่ตามโค้ด, ผู้ใช้ยืนยัน "โอเคแล้ว")

สมมติฐานร่วม: PACL D = ON ใช้ 1 m³/ชม. (มีประวัติรอบ 18:00 ของ D จึงได้อัตราจากประวัติ); PACL C = STBY ระหว่างเติม; API รอบเลขคู่เข้าช้า ~1 ชม. 13 นาที

### S1 — มีบันทึกเติม + มีค่า API รอบถัดไป (กรณีปกติ)
เหตุการณ์: 20:00 API C=4, D=10 → 20:30 รถมาเติม C → 21:30 บันทึกหลังเติม C 4→16 → 22:00 operator จดระดับ (เข้า API) C=17, D=8

| เวลา | PACL C | PACL D |
|---|---|---|
| ~21:13 ค่า 20:00 เข้า | แท่ง 4, ไม่มีเส้นประ | แท่ง 10, คาดการณ์ ~8.8 → **มีเส้นประ** ลดต่อเนื่อง |
| 21:30 กดบันทึก 16 | ฐาน → 16 (21:30 ใหม่กว่า API 20:00); แท่งทึบยัง 4; **เส้นประ 16**, ตัวเลข 16/25; ไม่ลด (STBY) | ~8.5 |
| 22:00 – ~23:13 | **เส้นประยังอยู่ 16** (ค่า 22:00 ยังไม่เข้า API) | ลดต่อจาก 10 → ~6.8 |
| ~23:13 ค่า 22:00 เข้า | API 17 @22:00 ใหม่กว่าบันทึก 21:30 → ฐาน = 17 = API → **เส้นประหาย** | ฐาน 8 @22:00; อัตรา (10−8)/2 = 1; คาดการณ์ 8−1.2 = 6.8 → **มีเส้นประ** |

- ช่วง 20:00→22:00 ของ C ขึ้น 4→17 = การเติม → ไม่ถูกใช้คำนวณอัตรา
- **คำตอบคำถามผู้ใช้:** เส้นประยังแสดงจนกว่าค่า API รอบใหม่จะ**เข้าจริง** (ไม่ใช่ตอน 22:00 พอดี); การเทียบ API กับบันทึก และการลดตามอัตรา เกิด**ตลอดทุก 30 วินาที** ระหว่างชั่วโมงเลขคู่ด้วย — สิ่งที่รอชั่วโมงเลขคู่มีแค่ "ค่า API ใหม่"

### S2 — operator ไม่จด (ไม่มีค่า API รอบใหม่) มีแต่บันทึกเติม 4→16
- **C:** ฐาน = บันทึก 16 @21:30 ค้างไปเรื่อยๆ (ใหม่กว่า API 20:00 เสมอ); STBY → **เส้นประค้างที่ 16** จนกว่าจะมีค่า API ของ C ใหม่; แท่งทึบค้าง 4 พร้อม "·20:00"
- **C เปลี่ยนเป็น ON** (สถานะอาจมาก่อนค่าระดับ): ประวัติ C ไม่มีช่วงที่ลด → ใช้ default PACL 0.75 ÷ จำนวนถัง PACL ที่ ON → ลดจาก 16
- **D:** ลดจาก 10 @20:00 ชั่วโมงละ 1 ไปเรื่อยๆ — **ไม่มีเพดานเวลา**, ต่ำสุด 0 (clamp)
- **การตัดสินใจของผู้ใช้:** ยอมรับ — 1 ถังใช้หมดเร็วสุด ~12 ชม., กะ 8 ชม. operator จดอย่างน้อย 1 ครั้งก่อนออกงาน → มีข้อมูลตัดสินใจสั่งเติมเสมอ; ถ้า API ขาดนานจนคาดการณ์ถึง 0 operator ก็ยังรู้ว่าต้องเติมถังไหน. **ไม่ต้องทำ max-age**

### S3 — ติดตั้งใหม่ / ประวัติ API มีรอบเดียว (เจอจริงวันนี้)
- `api_history.jsonl` มีถังละ 1 จุด (20:00) → เดิม: คำนวณอัตราไม่ได้ → คาดการณ์ = API, ALUM A (ON) ค้าง 180.4 ไม่ลด (ผู้ใช้เห็นว่า "คาดการณ์ ALUM ผิด")
- ตอนนี้: ใช้ `defaultUsagePerHour` จนกว่าจะมีประวัติ; tooltip บอก "ค่าเริ่มต้นใน config (ประวัติ API ยังไม่พอ)"
- ALUM ความจุ 750 → เส้นประขึ้นเมื่อต่าง > 7.5 m³ ≈ 6 ชม. หลังรอบ API (1.25 m³/ชม.) แต่ตัวเลข %/m³ ลดทันที

### S4 — ถังของสารเดียวกัน ON หลายถัง
- อัตราจากประวัติ: แยกรายถัง (ถังใครถังมัน)
- อัตรา default: หารเท่ากันตามจำนวนถังที่ ON (default = ยอดใช้ทั้งระบบ)

### S5 — ถัง STANDBY / REPAIR
- อัตรา = 0 → คาดการณ์ = ฐาน (ถ้าฐานคือบันทึกเติม ก็ค้างที่ค่าบันทึก เช่น S1 ช่วง 21:30–23:13)
- ช่วงที่ระดับลดขณะถังพักทั้งสองปลาย ไม่ถูกนับเป็นการใช้ (กัน noise ของเซนเซอร์)

---

## 6. Edge cases (รวมที่คุยกัน)

| # | กรณี | พฤติกรรม | สถานะ |
|---|---|---|---|
| E1 | คาดการณ์ติดลบ / เกิน | `Math.Clamp(…, 0, max(ความจุ, ฐาน))` (`TankData.cs`); กราฟ clamp 0–100% | ✅ ผู้ใช้ถาม "ใส่ min=0 ใช่มั้ย" → ใช่ |
| E2 | ฐานเกินความจุอยู่แล้ว (PACL B 25.2/25) | ไม่ตัดทิ้ง แสดง 101% | ตั้งใจ |
| E3 | API ขาดส่งนาน | ลดต่อเนื่องถึง 0; ไม่มี max-age | ✅ ผู้ใช้ยอมรับ (S2) |
| E4 | API ออกช้า ~1 ชม. | ช่วง 22:00–23:13 ยังใช้ค่ารอบ 20:00; "·HH:mm" บอก stale | พฤติกรรมของ API |
| E5 | พนักงานกดบันทึกหลังเติม**ช้า** (เช่น 23:20 หลังค่า API 22:00=17 เข้าแล้ว) | `AfterRecordedAt` = เวลากดบันทึก → ถือว่าใหม่กว่า API → ฐาน = 16 ทับค่าจริง 17 | ⚠ รู้แล้ว ผู้ใช้โอเคกับพฤติกรรมปัจจุบัน (ทางแก้ถ้าต้องการ: ให้กรอกเวลาเติมเสร็จ) |
| E6 | PACL D ขึ้น "หลังเติม" 12 < API 19.7 (screenshot) | ค่าบันทึกหลังเติมต่ำกว่า API = ผิดปกติ; 12 บังเอิญเท่า Aq 8418 (ID ที่**ไม่ใช่** PACL) — น่าจะเป็นข้อมูลทดสอบ | ❓ ยังไม่ได้คำตอบ |
| E7 | 2 เที่ยวจริงลง PACL C: 4→15 @21:37:40 แล้ว 13→25 @21:38:03 | "ก่อนเติม" คันที่ 2 (13) ≠ "หลังเติม" คันแรก (15) ห่าง 23 วินาที — น่าจะเป็นข้อมูลทดสอบ | ❓ แจ้งผู้ใช้แล้ว |
| E8 | บันทึก sidecar ตอน `data.mdb` ไม่เปลี่ยน | stamp รวม sidecar → upload รอบถัดไป (≤ 300 s) | ✅ แก้ (§3.4) |
| E9 | Uroboros อ่านขณะ sidecar เขียน | backup API + busy_timeout 30 s; ห้ามก็อปไฟล์ .db ตรงๆ | ✅ |
| E10 | `sidecar.db` ไม่มี / snapshot ล้ม | log WARN `[SIDECAR] …; skipping sidecar merge` แล้ว upload ต่อ (merge = best-effort) | ✅ |
| E11 | `sidecar.db` เสีย | ย้ายเป็น `sidecar.db.bad_<ts>`, สร้างใหม่, toast แจ้ง — ⚠ record เก่าจะไม่อยู่ใน db ใหม่ (กู้จากไฟล์ .bad / `records_migrated_*`) | ✅ (ไม่ล้มวน) |
| E12 | JSON เก่าอ่านไม่ได้ตอน migrate | ไม่นำเข้า, toast แจ้ง, ไฟล์ยังอยู่ใน `records_migrated_*` | ✅ |
| E13 | ลบแถวใน `sidecar.db` | `chem.db` สร้างใหม่ทั้งไฟล์ทุกรอบ → แถวหายจาก Drive ด้วย (**sidecar.db = ต้นฉบับ**) | ⚠ ห้ามลบเอง |
| E14 | `Synced: false` / คอลัมน์ `SYNCED`=0 | = ยังไม่ส่ง **Central DB** (`centralApiUrl` ว่าง = ปิดอยู่) ไม่เกี่ยวกับ Drive / Aquadat — ค้าง 0 ตลอดเป็นปกติ | อธิบายผู้ใช้แล้ว |
| E15 | Uroboros 2 ตัว (เก่า `C:\Uroboros-Mini` + ใหม่) | upload `chem.db` ทับกัน | ⚠ ต้องปิดตัวเก่า |
| E16 | เครื่องไม่มี `D:\TRUCK93A` | Launcher เตือน "ไม่พบโปรแกรมชั่ง" แต่เปิดตัวอื่นต่อ; Watchdog รอ truck93a 60 s (`startupGraceSeconds`) แล้วปิดตัวเอง | ตั้งใจ |
| E17 | ปริมาณข้อมูลระยะยาว | JSON เดิม ~1 KB/เที่ยว, 1,000–1,500 เที่ยว/ปี → ย้ายเป็น SQLite แล้ว ไม่สะสมไฟล์ | ✅ |

---

## 7. Build / deploy — วิธีและกับดัก

- dotnet บนเครื่องนี้: x64 = `C:\Program Files\dotnet\dotnet.exe` (ใช้ build/publish ทุกตัว + รัน dll ของ Uroboros); x86 = `C:\Program Files (x86)\dotnet\dotnet.exe` (**ใช้รัน dll ของ TruckSidecar** win-x86 สำหรับ `--storetest`)
- exe ทุกตัวเป็น `requireAdministrator` → self-test ให้รันผ่าน `dotnet <dll>` (ไม่มี UAC และเห็น console)
- publish: `dotnet publish <csproj> -c Release -o <proj>\publish` (SelfContained อยู่ใน csproj ทั้ง 4 แล้ว)
- deploy ลง bundle (ที่ใช้รอบนี้):
  ```
  powershell -NoProfile -ExecutionPolicy Bypass -File Sidecar_prj\Sidecar_prj\deploy.ps1 -SrcRoot C:\Users\peera\OneDrive\Desktop\Sidecar_prj -DestRoot C:\Users\peera\OneDrive\Desktop\Sidecar_prj\Sidecar_prj
  ```
  config ใน bundle **ถูกทับจาก `publish\`** ทุกครั้ง → แก้ config ที่ source เสมอ; `credentials.json` / `token_store\` ไม่ถูกทับถ้ามีอยู่แล้ว
- ⚠ Git Bash heredoc บนเครื่องนี้**กิน backslash** (`\\` → `\`) ทำ JSON path พัง — เขียน JSON ด้วยเครื่องมือ Write/Edit แทน
- ⚠ warning NU1903: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (Uroboros_mini) มีช่องโหว่ระดับ high — ยังไม่ได้อัปเกรด

---

## 8. สิ่งที่ต้องทำต่อ

1. **เครื่องใช้งาน (ผู้ใช้ทำ):** ปิด sidecar + Uroboros (รวมตัวเก่า `C:\Uroboros-Mini`) → ก็อป `TruckSidecar` + `Uroboros_mini` จาก bundle ทับ `D:\Sidecar_prj` → เปิดผ่าน Launcher → ตรวจ:
   - `D:\Sidecar_prj\TruckSidecar\data\` มี `sidecar.db` + `records_migrated_*`
   - `D:\Sidecar_prj\Uroboros_mini\logs\` มี `[SIDECAR] Merged N unload record(s)`
   - ~5 นาที ดาวน์โหลด `chem.db` แล้ว `select * from SIDECAR_UNLOAD;` ได้ 2 แถวเดิม
2. ถาม E6 (PACL D หลังเติม 12) / E7 — ถ้าเป็นข้อมูลทดสอบ ช่วยลบใน `sidecar.db` (ลบแถว UNLOAD → FILL ลบตาม CASCADE)
3. ถามว่าจะเอาตัวเลข % สีแดง (≤ 20%) ออกด้วยไหม (เส้นเอาออกแล้ว)
4. อัปเดตเอกสารที่ยังอ้าง JSON: `handoff2509.md`, `docs\architecture.md`, Uroboros_mini `HANDOFF.md` (`SidecarRecordsDir` → `SidecarDb`)
5. อัปเกรด `SQLitePCLRaw` (NU1903)
6. ค้างจากก่อนหน้า: Aquadat login จริง, glass/blur บนเครื่องใช้งาน — ดู handoff2509.md "Still open"

## Suggested skills for the next session

- **scrutinize** — รีวิว `RecordStore.cs` (migration / corrupt-db / transaction) และ `SidecarMerge.cs` (snapshot → ATTACH → INSERT…SELECT, stamp) ก่อนใช้งานยาว
- **run** — ถ้ามีสิทธิ์เข้าเครื่องที่มี `D:\TRUCK93A`: เปิด Launcher แล้วตรวจ UAC ครั้งเดียว, ครบ 4 process, migrate + upload
- **code-review** — ตรวจ diff ของ Predictor (อัตราเฉลี่ย / default ÷ ON count) กับ sequence test §5
