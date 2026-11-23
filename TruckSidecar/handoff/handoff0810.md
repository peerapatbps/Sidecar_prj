# Handoff — TruckSidecar: เลื่อน/focus ฟอร์ม, เลือกวันส่ง Aquadat, login ว่าง + mock cmd (2026-10-08)

ต่อจาก [handoff0310.md](handoff0310.md) — session นี้มี 2 ส่วน: (A) แก้ UI 3 ข้อ + deploy, (B) mock ใหม่ "cmd = โปรแกรมชั่ง"
repo ไม่ใช่ git — ดูการเปลี่ยนแปลงจากไฟล์ที่ระบุด้านล่างโดยตรง ผู้ใช้สื่อสารภาษาไทย (ตอบเป็นภาษาไทย)

---

## 0. สถานะ (อ่านตรงนี้ก่อน)

| เรื่อง | สถานะ |
|---|---|
| โค้ด A (3 ข้อ) | ✅ build Debug + `dotnet publish -c Release -o publish` ผ่าน 0 warning |
| `--preview` แท็บส่งยอด | ✅ ช่องว่าง + placeholder "เช่น 00102616" + focus ที่ชื่อผู้ใช้ |
| ข้อ 1 (เลื่อน/focus ถ.พ.) และตัวเลือกวัน (ข้อ 2) | ⚠ **ยังไม่ได้เห็นบนจอจริง** — ตัวเลือกวันแสดงหลัง login เท่านั้น |
| `Sidecar_prj_deploy\TruckSidecar\` | ✅ exe/pdb/config ใหม่ (md5 ตรงกับ `TruckSidecar\publish\`), `data\` ไม่ถูกแตะ, config.json เหมือนเดิมทุกตัวอักษร |
| Backup deploy เดิม | `Sidecar_prj\_backup\deploy_TruckSidecar_20261008-161517\` |
| เครื่องโรงงาน (`D:\Sidecar_prj`) | ❌ ยังไม่ลง — ปิด TruckWatchdog ก่อน TruckSidecar แล้ว copy 3 ไฟล์ หรือรัน `Sidecar_prj\deploy.ps1` |
| Mock cmd (B) | ✅ สคริปต์ inject ทดสอบแล้ว + ตรวจด้วย EnumWindows ว่า sidecar เกาะหน้าต่าง cmd; ⚠ ยังไม่ได้ทดสอบผ่าน UI ว่า sidecar ขึ้นรถที่ inject (ไม่มี screenshot) |
| Mock ที่เปิดค้างไว้ | ตอนจบ session: sidecar + watchdog + stub + หน้าต่างเมนู cmd ยังรันอยู่ (mdb มีรถค้าง W1 1 คัน PACL ที่ inject ทดสอบ) — ปิดด้วยเมนู 9 หรือ `mock\stop-mock.bat` |

---

## A. แก้ UI 3 ข้อ

**1. รถเข้า → เลื่อนลงล่างสุด + focus ถ.พ. / บันทึก → เลื่อนขึ้นบนสุด** (ปัญหา: รายการ "รถรอบันทึก" ยาวขึ้นจนดันฟอร์มลงไปไกล)
- `Ui/ReceivePanel.cs`: `FocusEntry()`, `OnWindowActivated()`, `ScrollToTop()`, `AfterLayout()` (หา ScrollViewer ของหน้าแท็บจาก visual tree, รอ `DispatcherPriority.Loaded`)
- auto arrival: `MainWindow.OnArrival` เรียก `panel.FocusEntry()` เฉพาะตอนสลับมาแท็บนั้น (ไม่แย่งแท็บที่พนักงานพิมพ์อยู่); เลือกรถเอง (`LoadArrival(auto:false)`) เรียกเอง
- **ตั้งใจ:** หน้าต่างไม่ active (พนักงานอยู่ในโปรแกรมชั่ง) → ไม่ดึง keyboard แค่ตั้ง logical focus + ธง `_focusSg` แล้ว focus เมื่อ `Window.Activated` (hook ใน MainWindow) — ผู้ใช้ยังไม่ได้ตอบว่าต้องการให้ดึง focus ทันทีหรือไม่
- หน้าแท็บ ScrollViewer `Focusable = false` (คลิกพื้นไม่แย่ง focus)
- ผลข้างเคียงที่แจ้งผู้ใช้แล้ว: ข้อความ "บันทึก … ครบแล้ว ✓" อยู่ท้ายฟอร์ม พอเลื่อนขึ้นบนสุดจะไม่เห็น

**2. แท็บส่งยอด: เลือกวันที่จะส่งหลัง login** — `Ui/AquadatPanel.cs`
- segmented 3 ปุ่ม (`DayChoices = 3`): เมื่อวาน / 2 / 3 วันก่อน (ไม่รวมวันนี้ — ยอดยังไม่ครบวัน; ผู้ใช้ยังไม่ได้ยืนยันว่าต้องรวมวันนี้ไหม), ค่าเริ่มต้นเมื่อวาน, ป้าย `•` รอยืนยัน / `✓` ยืนยันแล้ว
- login แล้วแสดงเฉพาะวันที่เลือก (`_c.CutOf`), วันว่าง → chip "ไม่มียอด" ไม่มีปุ่มส่ง; วันค้างเก่ากว่า 3 วัน → บรรทัดเตือนสีส้ม (ส่งจากหน้านี้ไม่ได้แล้ว)
- ยังไม่ login → รายการแบบเดิม; `_sig` รวม `_dayBack` + วันที่ปัจจุบัน

**3. แท็บส่งยอด: login ว่าง + focus ชื่อผู้ใช้**
- `AquadatPanel.OnEnter()` (ผูกกับ `_tabs[^1].Seg.Checked` ใน MainWindow): ถ้ายังไม่ login → ล้าง user/pass + focus `_user`
- ไม่เติม `cfg.AquadatUsername` แล้ว → ใช้เป็น placeholder `Tag = "เช่น 00102616"`
- `Ui/Glass.xaml` style `Field`: เพิ่ม TextBlock `ph` แสดง `Tag` เมื่อ `Text == ""` — ใช้ได้กับทุกช่อง (ช่องอื่นไม่ได้ตั้ง Tag จึงไม่เห็นผล)

---

## B. Mock "cmd = โปรแกรมชั่ง"

วิธีใช้อยู่ใน [../mock/README.md](../mock/README.md) หัวข้อ "แบบที่ 2" — ไม่ซ้ำที่นี่

ไฟล์: `mock/cmd-mock.bat` (launcher + เมนู 1 ล้าง DB / 2 W1 สุ่ม / 3 W2 สุ่มจากรถค้าง / 4 list / 9 ปิด / 0 ออก), `mock/cmd-mock.ps1` (setup/stub/sidecar/watchdog, `-WipeSidecar`), `mock/sim.ps1` (+ `-Target`, actions `randin`/`randout`/`list`, `Query`, `NewTicket`)

ข้อเท็จจริงที่ตรวจแล้ว (สำคัญถ้าจะแก้ต่อ):
- หน้าต่างที่เปิดด้วย `conhost.exe cmd.exe` → `GetWindowThreadProcessId` คืน pid ของ **cmd** (แม้มี powershell ลูกรันอยู่ในคอนโซล) → `targetProcess = "cmd"` ใช้ได้ และ sidecar ไม่กระพริบตอน inject
- ถ้าเปิดใน Windows Terminal หน้าต่างเป็นของ WindowsTerminal.exe → หาไม่เจอ จึงบังคับ conhost
- `Native.FindMainWindow` เลือกหน้าต่าง cmd ที่ใหญ่ที่สุด — หน้าต่าง cmd อื่นของผู้ใช้อาจแย่ง
- ตั๋วใหม่ `9ddHHmmss` / `8ddHHmmss` (+1 ถ้าซ้ำใน mdb) — กันชนกับ record เก่าใน `run\data` หลังล้าง mdb
- encoding: `.ps1` ต้อง UTF-8 **มี BOM** (PowerShell 5.1 + ข้อความไทย); `.bat` UTF-8 ไม่มี BOM + CRLF + `chcp 65001`
- **TruckSidecar + TruckWatchdog เป็น `requireAdministrator`** (app.manifest) → หน้าต่างที่ไม่ elevate ปิดไม่ได้ (Access is denied) — `cmd-mock.bat` จึง self-elevate (UAC ครั้งเดียว) และ `Stop-SidecarProcs` ใน `cmd-mock.ps1`/`stop-mock.ps1` ขอ UAC ปิดแทนถ้าโดนปฏิเสธ (ผู้ใช้เจอ error นี้ที่เมนู 1 → y; แก้แล้วแต่ยังไม่ได้ทดสอบซ้ำ)
- `sim.ps1` รีสตาร์ตตัวเองใน PowerShell x86 (Jet OLEDB 32 บิต) ทุกครั้ง (~1 วิ/คำสั่ง)

---

## งานถัดไปที่น่าจะมา
1. ผู้ใช้ทดสอบ mock cmd: inject W1 → sidecar ต้องสลับแท็บ + เลื่อนลงล่าง + (เมื่อคลิก sidecar) focus ถ.พ.; บันทึก → เลื่อนขึ้นบน; inject W2 → ฟอร์ม/คิวตามผลชั่งออก
2. login stub (รหัสอะไรก็ได้) → ทดสอบตัวเลือก 3 วัน + ส่ง → ตรวจ `mock\received.log`
3. ถ้าผู้ใช้ต้องการ: ดึง focus มาที่ sidecar ทันทีตอนรถเข้า / รวม "วันนี้" ในตัวเลือก / แสดงผลบันทึกเป็น toast แทนข้อความท้ายฟอร์ม
4. deploy ลงเครื่องโรงงาน + อัปเดต `Sidecar_prj_deploy\TruckSidecar_Change_Report_*.docx` ถ้าผู้ใช้ขอ

## Build / Deploy (ทวน)
```
"C:\Program Files\dotnet\dotnet.exe" publish TruckSidecar\TruckSidecar.csproj -c Release -o TruckSidecar\publish
```
deploy ในเครื่อง dev = backup `Sidecar_prj_deploy\TruckSidecar\{exe,pdb,config.json}` ไป `_backup\deploy_TruckSidecar_<yyyyMMdd-HHmmss>\` แล้ว copy จาก `publish\` (ไม่แตะ `data\`)
`--preview <dir>` เรนเดอร์ทุกแท็บเป็น png (ใช้ `Start-Process` จาก PowerShell — Bash เรียก exe ตรงไม่ได้)

## Suggested skills
- `run` — เปิด mock/แอปเพื่อดูผลจริง
- `code-review` / `scrutinize` — ตรวจ `ReceivePanel.cs` / `AquadatPanel.cs` (focus timing, sig ของ Refresh)
- `anthropic-skills:docx` — ถ้าต้องอัปเดตรายงานการเปลี่ยนแปลง `.docx` ใน `Sidecar_prj_deploy\`
- `handoff:handoff` — ปิด session ถัดไป
