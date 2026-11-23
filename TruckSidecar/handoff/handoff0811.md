# Handoff — 08/11/2026: ไฮไลต์ถังที่กำลังรับสาร + Sidecar ขึ้นมา active เองตอน W1/W2

> ต่อจาก `handoff_conclude0611.md` (สรุปรวม F1–F9 ถึง 12/10) — อ่านไฟล์นั้นก่อนสำหรับภาพรวมระบบ ข้อจำกัดบังคับ และ build/deploy
> session นี้เพิ่ม 2 feature: **F10 ไฮไลต์ถัง** และ **F11 Auto-activate** — ทั้งคู่ทดสอบแล้ว + อัปเดต deploy/mock_deploy แล้ว (ผู้ใช้ยืนยันว่า mock ทำงานได้)
> ยังไม่ได้ลงเครื่องโรงงาน (`D:\Sidecar_prj\TruckSidecar\`)

---

## F10 — ไฮไลต์ถังที่ถูกเลือก + ป้ายทะเบียนรถ

**ความต้องการ:** บันทึก W1 + เลือกถังแล้ว → ถังนั้นบนกราฟต้องดูต่างออกไป + บอกว่ามาจากรถคันไหน; ชั่งออก (W2) **และ** บันทึกระดับหลังเติมแล้ว → กลับสีปกติ

**การตัดสินใจ**
- ผู้ใช้เสนอม่วงลาย แต่ **PACL ใช้ไล่สีม่วงอยู่แล้ว** (`MainWindow.ChemColors`) → เลือก **เหลืองอำพันลายเฉียง 45°** + กรอบ/เงาเรืองอำพัน (ผู้ใช้อนุมัติ "ใช้สีนี้เลย")
- แท่งยังสูงตามระดับจริง (ไม่เสียข้อมูล %) — เปลี่ยนแค่สี
- ป้ายเหนือถัง = ทะเบียนอย่างเดียว + ลูกศรชี้ลง (ไม่มีคำว่า "จาก" เพราะคอลัมน์แคบ ~90px ตัดคำ); หลายคัน = `80-2222 +1`; tooltip มีรายละเอียด "▶ กำลังรับสาร: … — รอชั่งออก + ระดับหลังเติม"
- record ค้างเกิน `pendingLookbackDays` (7) ไม่ไฮไลต์ — กันถังเหลืองค้างถาวร

**โค้ด**
- `SidecarController.cs` — `ActiveFills()` (กฎ: fill ของถังนั้น ยังไม่ `StepWeighOut && LevelAfter != null`), `TankView` เพิ่มพารามิเตอร์ `Active`
- `Ui/TankBarChart.cs` — `Stripes()`, `ActiveFill/ActiveTrack/ActiveBorder/ActiveGlow`, ป้ายทะเบียน, tooltip; ความสูง 214→240, `TopPad` 26→50

**ทดสอบ:** `--preview` กับ sidecar.db ที่ seed 5 record (Alum B ยังไม่ชั่งออก, Alum D ชั่งออกแล้วไม่มีระดับหลัง, Alum A ครบ, PACL C 2 คัน) → ไฮไลต์ถูกทุกกรณี ✅
⚠ `--preview` ต้องรันจาก `bin\Release\net9.0-windows\win-x86\` — dll ที่ `bin\Release\net9.0-windows\` (ราก) เป็น build **v1 เก่า** หลงอยู่

---

## F11 — Sidecar ขึ้นมา active เองตอนรถชั่งเข้า/ออก (เดิมเป็นงานค้างข้อ 10 ใน handoff_conclude0611)

**พฤติกรรม**
- W1 → activate + สลับแท็บสาร + cursor ที่ **ถ.พ.** (ใช้ `FocusEntry()` เดิม)
- W2 ของรถที่มี record และยังขาดข้อมูล → ถ้าฟอร์ม**ว่างจริง** (`ReceivePanel.IsBlank` = ไม่แก้ค้าง และไม่มีรถเข้าที่ระบบเติมรอ) เปิด record นั้น + cursor ที่ช่องระดับช่องแรกที่ว่าง (`FocusLevels()`, ปกติ = ระดับหลังเติม); ไม่ว่าง → แค่ activate + toast ไม่ทับฟอร์ม
- W2 ของรถที่ไม่มี record / record ครบแล้ว → ไม่ activate

**เงื่อนไขดึงขึ้นมา** (`MainWindow.TryActivate`, เรียกทุก tick 150ms ของ `Track()`):
1. foreground = หน้าต่างหลักโปรแกรมชั่ง (หรือไม่มี) — dialog ของโปรแกรมชั่ง / โปรแกรมอื่น → รอ
2. หน้าต่างหลักไม่ถูก modal ล็อก (`IsWindowEnabled`)
3. ผู้ใช้หยุด input ≥ `activateIdleMs` (1500) — `GetLastInputInfo`
4. เกิน `activateTimeoutSeconds` (60) → เลิก เหลือ toast
- config ใหม่ (ค่าเริ่มต้นอยู่ในโค้ด ไม่ต้องแก้ config.json): `activateOnScale` (true), `activateIdleMs`, `activateTimeoutSeconds` — `Config.cs`

**วิธี activate — สิ่งที่เรียนรู้ (สำคัญ)**
- Windows foreground lock: `SetForegroundWindow` จากโปรเซสเบื้องหลังมักถูกปฏิเสธ; เครื่อง dev มี `SPI_GETFOREGROUNDLOCKTIMEOUT` = 2147483647 (ไม่มีวันหมด)
- `AttachThreadInput` กับหน้าต่าง **console (mock cmd) ล้มเหลวเสมอ** err=87 (thread เป็นของ conhost) — กับ truck93a (VB6 GUI ปกติ) น่าจะใช้ได้ แต่ยังไม่ยืนยัน
- **`Window.IsActive` ของ WPF ค้างเป็น true** หลัง SetForegroundWindow ที่ล้มเหลว → โค้ดรุ่นแรกคิดว่า active แล้วเลยหยุด (นี่คือบั๊กที่ผู้ใช้เจอใน mock) → แก้ให้ตัดสินจาก `GetForegroundWindow()` ทุกที่ (MainWindow + `ReceivePanel.OnWindowActivated`)
- ล้มเหลวแน่นอนเมื่อ: Sidecar เคย active แล้ว → พนักงานคลิกกลับไปโปรแกรมชั่ง → รถคันถัดไป (= workflow จริง)
- **ทางแก้ที่ใช้: hotkey F24** — `RegisterHotKey(MOD_NOREPEAT, VK_F24)` ตอน `SourceInitialized`; Attach+SetForeground ล้ม → `keybd_event` F24 keydown (ถูกกินเป็น hotkey ไม่ถึงโปรแกรมชั่ง) → `WM_HOTKEY` ให้สิทธิ์ → `SetForegroundWindow` สำเร็จ → ส่ง keyup หลังเราขึ้นหน้าแล้ว (ตกที่ sidecar); กัน F24 ค้าง 1 วิ; ส่งได้ ≤1 ครั้ง/2 วิ
- ปฏิเสธแล้ว (ขัดข้อห้าม "ห้ามพิมพ์/คลิกเข้าโปรแกรมชั่ง" หรือแก้ระบบ): กด Alt จำลอง, แก้ `ForegroundLockTimeout`
- `SwitchToThisWindow` ลองแล้วไม่ช่วย (ยังอยู่ในโค้ดเป็น fallback)

**โค้ด:** `Native.cs` (`ForceForeground(hWnd, out diag)`, `IdleMs`, `Describe`, F24/hotkey), `Ui/MainWindow.cs` (`RequestActivate`, `TryActivate`, `WndProc`, `OnForced`, `ActLog`, `CurrentPanel`, `OnArrival`/`OnWeighedOut`), `Ui/ReceivePanel.cs` (`_focusTarget`, `FocusLater`, `FocusLevels`, `IsBlank`), `Config.cs`

**Log วินิจฉัย:** `data\activate.log` — ทุก request + ผลแต่ละขั้น เช่น `hotkey F24 registered`, `request`, `ForceForeground failed (attach→… FAIL err=87, SetForegroundWindow=False…)`, `sending hotkey F24`, `activated via hotkey`, `wait: user typing`, `wait: foreground … is not scale …` (เวลาเป็นปี พ.ศ. เพราะ culture th-TH — ปกติ)

**ผลทดสอบ** (mock จำลองที่เปิดตามลำดับเดียวกับ cmd-mock จริง + คลิกเมนูด้วยเมาส์จริงผ่าน computer-use, ไม่ elevate):
| กรณี | ผล |
|---|---|
| ก่อนแก้ hotkey: active แล้ว → คลิกกลับเมนู → W1 | ❌ reproduce บั๊กได้ |
| hotkey: W1 × 4 รอบ คลิกกลับเมนูทุกรอบ | ✅ 4/4 (5–10 ms) |
| W2 ฟอร์มว่าง | ✅ cursor ที่ Alum B ระดับหลังเติม |
| W2 ขณะฟอร์มมีรถเข้า auto รอ | ✅ ไม่ทับ, cursor ที่ ถ.พ. ของคันที่รอ |
| W2 ของรถไม่มี record | ✅ ไม่ activate |
| W1 ขณะอยู่โปรแกรมอื่น | ✅ ไม่แย่ง |
| ผู้ใช้ทดสอบเองด้วย TESTER.exe (elevated) หลังแก้ | ✅ "ทำงานได้แล้ว" |

ยังไม่ทดสอบ: dialog ของโปรแกรมชั่งเปิดอยู่ / กำลังพิมพ์ (ทดสอบแค่ตรรกะ), **truck93a จริง**, เครื่องที่มีโปรแกรมอื่นจอง F24 (จะ log "NOT registered" แล้วเหลือแค่ toast)

---

## สถานะไฟล์ deploy

- `Sidecar_prj_deploy\TruckSidecar\TruckSidecar.exe/.pdb` = publish 08/11 21:14 (ตรงกับ `TruckSidecar\publish\`) — config.json/data ไม่แตะ; Launcher/Watchdog/Uroboros ไม่เปลี่ยน
- `TruckSidecar\mock\mock_deploy\bin\` และ `run\` = ตัวเดียวกัน
- Backup: `_backup\TruckSidecar_deploy_20261108_pre_tankhighlight\` (ตัว 11/10 ก่อนเริ่มวันนี้ — ใช้ย้อนกลับ), `_backup\mock_deploy_20261108_pre_tankhighlight\`, `_backup\TruckSidecar_20261108_pre_autoactivate\` (ตัวไฮไลต์ถังอย่างเดียว)
- ลงโรงงาน: ปิดโปรแกรมชั่ง → ก็อป exe(+pdb) ทับ `D:\Sidecar_prj\TruckSidecar\` → เปิดผ่าน TruckLauncher
- ไม่ได้ใช้ `stage.ps1` (มัน /MIR ทั้งโฟลเดอร์ — จะลบ config/data ของ deploy)

## งานค้าง / ขั้นต่อไป

1. ลงเครื่องโรงงาน + ทดสอบกับ truck93a จริง — ดู `data\activate.log` ว่าใช้เส้นทาง attach หรือ hotkey, และตอนหน้าต่างพิมพ์ตั๋ว/กล่องข้อความของ truck93a เปิด
2. ยืนยันกับหน้างานว่าการเด้งขึ้นมาไม่รบกวน (ปิดได้ด้วย `"activateOnScale": false`)
3. (ไม่บังคับ) `activate.log` ไม่มีการหมุนไฟล์ — ~3 บรรทัด/เที่ยว ยังเล็ก
4. (ไม่บังคับ) ลบ build v1 เก่าที่ `bin\Release\net9.0-windows\TruckSidecar.dll` กันสับสนตอน `--preview`
5. อัปเดต `docs/architecture.md` / รายงานถ้าต้องการ (ยังไม่ได้แตะ)

## เครื่องมือทดสอบที่สร้างใน session (อยู่ใน scratchpad ของ session — อาจหายไป, สร้างใหม่ได้จากคำอธิบายนี้)
- `launch.ps1`: reset mock.mdb → เปิด sidecar ด้วย `"C:\Program Files (x86)\dotnet\dotnet.exe" TruckSidecar.dll` (ไม่ต้อง admin) → รอ 3 วิ → `conhost cmd.exe /c "cmd-mock.bat" menu` (ข้ามขั้น elevate)
- inject ด้วย `mock_deploy\sim.ps1 randin|weighin|weighout ...`; ตรวจ foreground ด้วย `GetForegroundWindow`; ตรวจ cursor ด้วย UIAutomation `AutomationElement.FocusedElement`
- ต้องคลิกเมนูด้วยเมาส์จริง (computer-use) — การดึงหน้าต่างขึ้นด้วยสคริปต์ทำให้กฎ foreground ผ่อน ผลทดสอบหลอกว่าผ่าน
- seed record W2 ลง `SIDECAR_UNLOAD`/`SIDECAR_FILL` (วันที่รูปแบบ `yyyy-MM-dd HH:mm:ss`) แล้ว restart sidecar (store โหลดตอนเริ่มเท่านั้น)
- mock จริงต้อง admin: `cmd-mock.exe` ถามรหัส Administrator (เครื่องนี้ยังไม่ได้จำ — agent ห้ามกรอกรหัส), `TESTER.exe` ขอผ่าน UAC

## Suggested skills
- `scrutinize` — รีวิว F11 ก่อนลงโรงงาน (โดยเฉพาะ F24/keyup และเงื่อนไข TryActivate)
- `run` — เปิด app/mock เพื่อดูผลจริง
- `anthropic-skills:docx` — ถ้าต้องอัปเดตรายงาน `Sidecar_Project_Full_Report.docx` / Change Report ใน `Sidecar_prj_deploy\`
- ตอบผู้ใช้เป็นภาษาไทยเสมอ (memory: reply-in-thai)
