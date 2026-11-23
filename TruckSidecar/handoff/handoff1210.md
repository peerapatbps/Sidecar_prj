# Handoff — Uroboros_mini ไม่ upload chem.db ของใหม่ (mtime ของ data.mdb ไม่ขยับ) (2026-10-12)

ต่อจาก [handoff0810.md](handoff0810.md) — session นี้แก้บัคใน **Uroboros_mini** (ตัวรวม data.mdb + sidecar.db → chem.db แล้ว upload ขึ้น Google Drive) ไม่ได้แก้ TruckSidecar
repo ไม่ใช่ git — ดูการเปลี่ยนแปลงจากไฟล์ที่ระบุด้านล่างโดยตรง ผู้ใช้สื่อสารภาษาไทย (ตอบเป็นภาษาไทย)

---

## 0. สถานะ (อ่านตรงนี้ก่อน)

| เรื่อง | สถานะ |
|---|---|
| โค้ดแก้ใน `Uroboros_mini\` | ✅ `dotnet build -c Release` ผ่าน (มีแค่ warning NU1903 เดิม) |
| ทดสอบ sync ออฟไลน์ | ✅ 6 กรณีผ่าน (ดู §C) — **ไม่ได้ทดสอบขั้น Convert/Upload** (ต้องใช้ ACE x64 + Google token) |
| `Sidecar_prj_deploy\Uroboros_mini\` | ✅ exe/dll ใหม่ runtime .NET **10.0.8** ทั้งชุด (hash dll ตรง build ใหม่); ไม่แตะ `mini-uploader.json`, `config_\`, `credentials.json`, `token_store\`, `logs\`, `media\` |
| `Uroboros_mini\publish\` | ✅ อัปเดตตรงกับ deploy (เผื่อรัน `stage.ps1`) |
| เครื่องโรงงาน (`D:\Sidecar_prj\Uroboros_mini`) | ✅ ผู้ใช้ deploy แล้ว (2026-10-12) |
| ยืนยันผลที่หน้างาน | ❌ ยังไม่ได้ดู log จริงหลังลงตัวใหม่ |

---

## A. บัคที่พบ

**อาการ (ผู้ใช้รายงาน):** โปรแกรมชั่ง (truck93a) บันทึกรถคันใหม่แล้ว รอ >15 นาที chem.db บน Drive ไม่มีของใหม่ (Uroboros รอบละ `PollIntervalSeconds: 300` → ผ่านไป 3 รอบแล้ว)

**ต้นเหตุ:** `MdbReaderSafe.SyncMdbToMediaAtStartupCore` ตัดสินว่า "มีของใหม่" จาก `File.GetLastWriteTimeUtc(data.mdb)` เทียบกับ `timestamp=` ใน `config_\config.ini`
ขณะ truck93a (VB6/Jet) เปิด data.mdb ค้าง NTFS อาจไม่อัปเดต mtime → ได้ `UpToDate` ทุกรอบ → ไม่คัดลอกใหม่ (ฝั่ง TruckSidecar รู้ปัญหานี้อยู่แล้ว จึงมี `forceCheckSeconds: 30`; Uroboros ไม่มี)

**ยืนยันแล้ว:** ผู้ใช้แก้ `timestamp` ใน config.ini ให้ไม่ตรง (+ ลบ `media\chem.db.stamp`) → รอบถัดไป upload ของใหม่ขึ้นจริง

**บัครอง (พบระหว่างไล่โค้ด แก้ไปด้วย):**
1. `timestamp` ถูกเขียนลง ini **ก่อน** Convert — ถ้าคัดลอกได้สำเนาที่ Jet เขียนไม่เสร็จ แล้ว Convert ล้ม → รอบหน้าเห็น UpToDate → วนใช้สำเนาเสียตลอดจน mdb เปลี่ยนอีกครั้ง
2. `File.Copy` คง mtime ของต้นทาง → stamp ของ Convert (`chem.db.stamp` = mtime ของ `media\data.mdb` + sidecar stamp) ไม่เปลี่ยน → Convert skip → upload chem.db ตัวเก่าซ้ำ (เหตุที่ทดสอบต้องลบ `chem.db.stamp` ด้วย)
3. `File.Copy` เปิดต้นทางแบบไม่แชร์การเขียน → อาจโดน sharing violation ขณะ truck93a เขียนอยู่

## B. การแก้ไข

ไฟล์: `Uroboros_mini\MdbReaderSafe.cs`, `Uroboros_mini\Program.cs` (ค้นคอมเมนต์ภาษาไทย "hash" / "mtime")

- **Sync:** คัดลอก data.mdb ทุกรอบ (~4 MB) ด้วย `FileStream(FileShare.ReadWrite|Delete)` ไปไฟล์ชั่วคราว → SHA-256 (16 hex แรก) → เทียบกับ `media\data.mdb.sha256` (ไฟล์ใหม่) — ต่าง = แทนที่ `media\data.mdb` + เขียน hash → `Synced`; เหมือน = `UpToDate`
- `timestamp` ใน config.ini ยังเขียนอยู่แต่ **อ้างอิงเท่านั้น** ไม่ใช้ตัดสินแล้ว
- `MediaMdbHash()` อ่านจากไฟล์ `.sha256` เท่านั้น (ไม่คำนวณเอง) — **ลบไฟล์ `.sha256` = บังคับให้รอบหน้าคัดลอกใหม่**
- **Convert:** stamp = hash + `SidecarMerge.Stamp(...)` (แทน mtime); ถ้า Convert throw → ลบ `.sha256` แล้ว rethrow (กันวนใช้สำเนาเสีย)
- **Program.cs `RunOnceAsync`:** `sourceStamp` = hash + sidecar stamp (แทน mtime) — ใช้เทียบกับ `LastUploadedStamp` ใน `uploader-state.json`
- รอบแรกหลังลงตัวใหม่จะ `Synced` + upload 1 ครั้งเสมอ (ยังไม่มี `.sha256`) — ปกติ
- log บรรทัด sync มี `hash=...` ต่อท้าย ใช้ดูว่าเนื้อไฟล์เปลี่ยนจริงไหม

## C. การทดสอบที่ทำ

harness ชั่วคราวใน scratchpad (ลบได้ ไม่ได้อยู่ใน repo) เรียก `SyncMdbToMediaAtStartup` ตรงๆ:

| กรณี | ผล |
|---|---|
| ครั้งแรก | Synced ✅ |
| ไม่เปลี่ยน | UpToDate ✅ |
| เนื้อไฟล์เปลี่ยน แต่ตั้ง mtime เท่าเดิม (บัคหลัก) | Synced ✅ |
| ไม่เปลี่ยนอีกรอบ | UpToDate ✅ |
| ไฟล์ถูกเปิดเขียนค้าง (FileShare.ReadWrite) | Synced ✅ |
| ลบ `.sha256` (จำลอง Convert ล้ม) | Synced ใหม่ ✅ |

## D. หมายเหตุ publish

- **ต้อง publish ลงโฟลเดอร์ว่าง + `-p:RuntimeFrameworkVersion=10.0.8`** — SDK ในเครื่อง (10.0.102) default เป็น runtime 10.0.2 แต่ deploy เดิมเป็น 10.0.8; publish ทับโฟลเดอร์ `publish\` เดิมทำให้ runtime ปนกัน (เจอ `mscordaccore_..._10.0.225...dll` + runtimeconfig 10.0.2 ปนกับ coreclr 10.0.826) — แก้แล้ว ลบไฟล์หลงแล้ว
  ```
  dotnet publish Uroboros_mini\Uroboros.MiniUploader.csproj -c Release -o <โฟลเดอร์ว่าง> -p:RuntimeFrameworkVersion=10.0.8
  ```
- `Microsoft.Data.Sqlite.dll` เลข 10.0.225 = เวอร์ชัน NuGet package ไม่ใช่ runtime — ปกติ
- ควรพิจารณาใส่ `<RuntimeFrameworkVersion>10.0.8</RuntimeFrameworkVersion>` ใน csproj ถาวร (ยังไม่ได้ทำ)

## E. งานค้าง / ข้อเสนอ

1. ลงเครื่องโรงงาน แล้วดู `Uroboros_mini\logs\` ว่ามี `Synced ... hash=` → `[MDB] Convert OK` → `Upload done` หลังชั่งรถใหม่ ภายใน ~5 นาที
2. Warning NU1903: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 มีช่องโหว่ high — เสนออัปเดต package แล้ว ผู้ใช้ยังไม่ตอบ
3. TruckSidecar (ไม่ได้แก้): `SidecarController.ScanScaleAsync` ใช้ธง `_scanning` — ถ้า copy/query data.mdb ค้างไม่จบ ธงค้าง true → ไม่ตรวจตาชั่งอีกเลยจนเปิดใหม่ เสนอใส่ timeout แล้ว ผู้ใช้ยังไม่ตอบ (`TruckSidecar\SidecarController.cs:89`, `ScaleDb.Query`)
4. ความถี่ที่ถามในรอบนี้ (อ้างอิง): TruckWatchdog `pollSeconds: 5`; TruckSidecar `watchSeconds: 3` + `forceCheckSeconds: 30`; Uroboros `PollIntervalSeconds: 300`

## Suggested skills

- `scrutinize` / `code-review` — รีวิว diff ใน `MdbReaderSafe.cs` + `Program.cs` ก่อนลงโรงงาน
- `run` — ถ้าต้องรัน Uroboros แบบ `--sidecar-test` เพื่อลอง Convert ออฟไลน์
- `handoff:handoff` — ปิด session ถัดไป
