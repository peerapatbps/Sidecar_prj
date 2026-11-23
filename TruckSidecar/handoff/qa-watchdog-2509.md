# QA Test Report — TruckWatchdog (+ Sidecar integration)

- **วันที่ทดสอบ:** 2026-09-25
- **ผู้ทดสอบ:** Claude (auto) + peerapat.drive@gmail.com
- **สภาพแวดล้อม:** Windows 11 Home Single Language 10.0.26200, .NET 9 (x86 self-contained)
- **ส่วนที่ทดสอบ:** `TruckWatchdog` (โปรเจกต์ใหม่) + โปรโตคอล stop-flag ใน `TruckSidecar`
- **วิธีทดสอบ:** mock harness (`mock\start-mock.ps1`) — Notepad แทน `truck93a.exe`, sidecar ตัวจริง, Aquadat stub (localhost:18080)
- **ผลรวม:** ✅ ผ่าน 4/4 (unit-level) + ✅ ผ่าน 4/4 (integration กับ sidecar จริง)

---

## ขอบเขต (Scope)
ตรวจว่า watchdog:
1. เปิด sidecar กลับเมื่อถูกปิด/crash **เฉพาะขณะโปรแกรมชั่งทำงาน**
2. เคารพทางออกผู้ดูแลระบบ (Ctrl+Shift+F12) ผ่านธง `sidecar.stopped` — ไม่เปิดใหม่
3. ล้างธงเมื่อ sidecar เปิดตามปกติ (กลับมาคุ้มครอง)
4. จบตัวเองเมื่อโปรแกรมชั่งปิด และ **ไม่เคยฆ่า** sidecar

**นอก scope:** ส่ง Ctrl+Shift+F12 เข้าหน้าต่าง sidecar จริง (ทดสอบด้วยการเขียนธงเลียนแบบพฤติกรรม OnKeyDown แทน), Aquadat GraphQL push (ทดสอบแยกด้วย `--aqtest` แล้ว), การอ่าน data.mdb

---

## A. Unit / behavior test (แยกอิสระ, ตัวแทน process = ping/charmap)

| # | Test case | ขั้นตอน | คาดหวัง | ผล |
|---|-----------|---------|---------|-----|
| A1 | Relaunch เมื่อไม่มีธง | truck(ping)รัน, ไม่มีธง, เปิด watchdog | เปิด sidecar(charmap) | ✅ charmap=1 |
| A2 | Relaunch หลังถูกฆ่าซ้ำ | ฆ่า charmap | เปิดใหม่ภายใน 1 poll | ✅ charmap=1 อีกครั้ง |
| A3 | เคารพธง (admin exit) | มีธง `sidecar.stopped`, truck รัน | **ไม่**เปิด sidecar | ✅ charmap=0 |
| A4 | จบเอง + ล้างธงเมื่อ truck ปิด | ฆ่า ping | ลบธง + watchdog exit | ✅ ธง GONE, watchdog=0 |
| — | ตรวจจับ sidecarExe ผิด path | ชี้ไปไฟล์ที่ไม่มี | เตือน MessageBox แล้วหยุด | ✅ (พบ MessageBox "Truck Scale Watchdog") |
| — | Single-instance | เปิด watchdog ซ้อน | ตัวที่ 2 ออกทันที (mutex) | ✅ |

## B. Integration test (mock, sidecar ตัวจริง, truck stand-in = Notepad)

| # | Test case | ขั้นตอน | คาดหวัง | ผล |
|---|-----------|---------|---------|-----|
| B1 | Relaunch sidecar จริง | ฆ่า `TruckSidecar` ขณะ Notepad เปิด | เปิดใหม่ PID เปลี่ยน | ✅ 11592 → 30564 |
| B2 | เคารพ admin-exit | เขียน `run\data\sidecar.stopped` แล้วฆ่า sidecar | ไม่เปิดใหม่, ธงคงอยู่, watchdog ยังเฝ้า | ✅ sidecar=0, watchdog=1, ธง PRESENT |
| B3 | ล้างธงตอน startup + คุ้มครองต่อ | เปิด sidecar เอง แล้วฆ่าอีกครั้ง | ธงถูกลบ, เด้งกลับ | ✅ ธง GONE, 35512 → 3388 |
| B4 | จบเมื่อ truck ปิด, ไม่ฆ่า sidecar | ปิด Notepad | watchdog exit, sidecar ยังอยู่ | ✅ watchdog=0, sidecar=1 |

---

## บั๊ก/ประเด็นที่พบ & การแก้

### BUG-1 (mock harness, ไม่กระทบ deploy) — Notepad alias ตรวจจับไม่ได้บน Win11
- **อาการ:** `Start-Process notepad` (app execution alias) บนเครื่องนี้ไม่สร้าง process ชื่อ `notepad` ที่ `GetProcessesByName("notepad")` มองเห็น → ทั้ง sidecar และ watchdog เข้าใจผิดว่าโปรแกรมชั่งไม่ทำงาน (watchdog หมด grace แล้ว exit; sidecar เข้า nag mode)
- **สาเหตุ:** Windows 11 ใช้ Store Notepad (process ชื่อ `Notepad`) ผ่าน alias ที่บางครั้งไม่ผูกกับ process ที่ query ได้ทันที
- **แก้:** `mock\start-mock.ps1` เปลี่ยนเป็นเปิดด้วย full path `%SystemRoot%\System32\notepad.exe` + รอ 2 วิ + เตือนถ้ายัง detect ไม่ได้
- **หมายเหตุ deploy จริง:** ไม่กระทบ — เครื่องจริงใช้ `truck93a.exe` (Win32 แท้ ชื่อ process ตรง `GetProcessesByName` เห็นแน่นอน)

### หมายเหตุการทดสอบ
- ระหว่างชุดทดสอบ A แรกๆ พบ **false negative จาก mutex ค้าง**: watchdog ตัวเก่าที่ยังไม่ตายถือ mutex อยู่ ทำให้ตัวใหม่ออกทันที → ต้อง `Stop-Process TruckWatchdog` ให้เกลี้ยงก่อนแต่ละเคส (ยืนยันว่า single-instance ทำงานถูก)
- ครั้งแรกใช้ `mspaint.exe` เป็นตัวแทน sidecar แต่ path `System32\mspaint.exe` ไม่มีบนเครื่องนี้ (อยู่ที่ WindowsApps) → watchdog เตือนถูกต้องว่าหาไฟล์ไม่พบ; เปลี่ยนไปใช้ `charmap.exe`

---

## สรุป
`TruckWatchdog` ทำงานตามข้อกำหนดครบทุกข้อ รวมถึงเงื่อนไขสำคัญ:
- relaunch เฉพาะขณะโปรแกรมชั่งทำงาน
- เคารพ Ctrl+Shift+F12 (admin exit) ไม่รบกวนผู้ดูแล
- ไม่ฆ่า sidecar เอง, จบตัวเองสะอาดเมื่อจบงาน

**พร้อม deploy.** ก่อนใช้จริงให้ตรวจ path ใน `watchdog.json` / `launcher.json` ให้ตรงเครื่องปลายทาง และทดสอบ Ctrl+Shift+F12 กับหน้าต่าง sidecar จริงอีกครั้ง (ยังไม่ได้ทดสอบการส่งคีย์เข้าหน้าต่างจริงในรอบนี้).
