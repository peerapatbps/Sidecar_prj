# Handoff + QA test sequence: TruckSidecar v2 (auto-detect workflow + liquid glass UI)

- **Date:** 2026-09-25 (late evening)
- **Tested by:** Claude (auto, driven through UI Automation) + peerapat.drive@gmail.com
- **Environment:** Windows 11 Home 10.0.26200 on the dev PC. The plant PC runs **Windows 10** and has not been tested yet. .NET 9 WPF, x86 self-contained.
- **Test method:** mock harness (`mock\start-mock.ps1`). Notepad stands in for `truck93a.exe`, `mock\run\mock.mdb` stands in for data.mdb, and there is an Aquadat stub on localhost:18080. The UI was driven with UI Automation (script in Appendix A).
- **Plan:** `C:\Users\peera\.claude\plans\c-users-peera-onedrive-desktop-trucksid-federated-harp.md`
- **Result:** ✅ Every case passed: A–F (build / workflow / rule 5 / queue / docking / nag + admin exit) + G (17 truck-order scenarios, e.g. IN1→IN2→OUT2→OUT1) + `--cctest` 7/7. **10 bugs + 2 improvements** were found and fixed (§C). The decision tree for the 5 rules is in §0.

---

## 0. Logical decision tree for the 5 rules (matches the code as of 2026-09-25 23:20)

> BLOCK = saving is refused (red dialog) · WARN = asks for confirmation before saving · every BLOCK/WARN is written to `audit.jsonl` (action `crosscheck`)
> Main code: `ScaleWatcher.CheckAsync` → `SidecarController.Process` / `ApplyWeighOut` / `CrossCheckAsync` / `Commit`, and `ReceivePanel` / `MainWindow.OnArrival` on the UI side

### Heartbeat: when data.mdb is read (supports rules 2 and 4)
```
Every watchSeconds (3 s)  ScaleWatcher.CheckAsync()
├─ mdbPath empty ─────────────────────────► do nothing (grey "ตาชั่ง: ไม่ได้ตั้งค่า" pill)
├─ file missing / stat fails ─────────────► LastError (red pill) → retry next tick
├─ stamp (LastWriteTime+Length) unchanged
│    AND less than forceCheckSeconds (30 s) since last query ─► skip (don't copy the file)
└─ stamp changed OR 30 s reached
     ├─ copy to temp + Jet query fails (half-written file) ─► don't store the stamp → retry in 3 s
     └─ success → store stamp → Process(Ins, Outs) → CatchUpOldWaiting() → Changed → redraw
```

### Rule 1: plate / Track ID come from the scale program only
```
The ทะเบียนรถ and Track ID fields are ReadOnly. They are set in only 2 ways:
├─ (a) auto-detect (rule 2)  → LoadArrival(auto=true)  → not counted as "typed" (a newer truck can replace it)
└─ (b) "ดึงจากตาชั่ง" button → pick a row in ScalePickerWindow
        ├─ that TICKET1 already has a record
        │    ├─ record belongs to another chemical ─► switch to its tab + TryOpen (never open in 2 forms)
        │    └─ same chemical ───────────────────► TryOpen (asks to discard if the form has unsaved typing)
        ├─ PRODUCT is another chemical ──────────► "รถคันนี้เป็น X — ไปบันทึกที่แท็บ X" stop
        ├─ the form is editing another record ───► "ผูกกับ Track ID … แล้ว เปลี่ยนไม่ได้" stop
        └─ otherwise → ask to discard if needed → LoadArrival(auto=false) → counts as "typed"
```

### Rule 2: detect weigh-in (IN)
```
Ins = WDATA WHERE STAT='1' AND (TICKET2 IS NULL OR '') AND DAYIN>=DateAdd('d',-1,Date())  ORDER BY TICKET1 DESC
 └─ (C#) keep rows whose weigh-in time DAYIN+TMIN >= now − inWindowHours (default 24 h)
     Jet filters by DAYIN first because DAYIN holds only the date; the exact filter uses the real time
     (updated 2026-09-25 23:25: was DAYIN=Date(), which missed trucks weighed in before midnight)
 └─ filter PRODUCT ∈ productMap {01→ALUM, 03→PACL}; 02 general cargo and others are dropped
     primary = first row (= max TICKET1)
     For each row:
     ├─ TICKET1 already has a record ──► not shown (already recorded)
     ├─ TICKET1 in dismissed.json ─────► not shown ("ไม่ใช่รถสารเคมี")
     └─ otherwise ─────────────────────► Arrival = blue chip "ชั่งเข้า hh:mm · รอบันทึก" in the chemical's tab

     A primary not yet announced this session (NewPrimaryArrival):
     ├─ that chemical's form IsIdle (no record open + nothing typed)
     │    ├─ LoadArrival(auto) — can replace an older auto draft nobody has touched
     │    ├─ current tab idle / is that tab / is the ส่งยอด tab ─► switch to that tab + toast "รถ X เข้า"
     │    └─ operator typing in another tab ──────────────────► don't switch; toast "เติมฟอร์มไว้ที่แท็บ X"
     └─ form not idle ──────────────────────────────────────► chip only + toast "อยู่ในรายการรถรอบันทึก"

     Cleanup: auto draft still untouched, but TICKET1 is gone from Ins/Outs and has no record
       ─► clear the form + red toast "ไม่อยู่ในตาชั่งแล้ว (ถูกยกเลิกในโปรแกรมชั่ง)"
     Weighed in more than 24 h ago (e.g. an abandoned STAT=1 ticket) → not auto-detected → use "ดึงจากตาชั่ง"
     Chips for yesterday's trucks show the date: "ชั่งเข้า 24/09 23:50 (เมื่อวาน)"
```

### Rule 3: the first save creates a record even while incomplete
```
Press บันทึก (first time for this TICKET1)
├─ Collect(): read the form → error only for badly formatted numbers (e.g. "1.3x") → show it, stop
├─ Rule 5 (below) passes → Commit → RecordStore.Save(action="save", Version=1) + audit
└─ The record exists with before/after levels still empty ("data table waiting for levels")
    Stepper (computed from data, not stored):
    ① ชั่งเข้า = TrackId present   ② ใบนำส่ง = SG+temp+book+number+≥1 tank
    ③ ระดับถัง = every row has before+after   ④ ชั่งออก = TICKET2 present   ⑤ ครบ = Missing is empty
    Incomplete → stays in the "รถรอบันทึก" list (orange) and counts toward nag mode
```

### Rule 4: detect weigh-out (OUT)
```
Outs = WDATA WHERE STAT='2' AND DAYOUT=Date()  ORDER BY TICKET2 DESC
  (use DAYOUT, not DAYIN: trucks that weigh out after midnight · match by TICKET1: TICKET1/2 are separate counters)
For each row:
├─ record with this TICKET1 exists and has no TICKET2 → ApplyWeighOut
│    ├─ this TICKET2 is already on another record ─► reject + audit once + red toast (BUG-3)
│    ├─ TRUCK ≠ plate on the record ──────────────► reject + audit once + red toast
│    └─ passes → ApplyScale(TICKET2, W2, DAYOUT+TMOUT) → Save(action="scale")
│                 → complete? → Interpolation → Aquadat outbox → toast "ชั่งออกแล้ว"
│                 (if that record is open, only the scale data changes; unsaved typing is kept)
├─ record already has TICKET2 ─────────────────► skip
└─ no record + known chemical + not dismissed ─► red chip "ชั่งออกแล้วแต่ยังไม่มีบันทึก"
Catch-up: records with no TICKET2 whose TICKET1 is not in today's Ins/Outs
  ─► ScaleDb.ByTicket1 → if STAT='2' → ApplyWeighOut (same branch as above)
  (covers the sidecar being closed while the truck weighed out on an earlier day)
```

### Rule 5: crosscheck before saving (prevents duplicates from rules 2 and 4)
```
CrossCheckAsync(r)
B1  TrackId empty ─────────────────────────────────► BLOCK "ยังไม่ได้เลือกรถ"
B2  TICKET1 already on another record (different Id) ─► BLOCK "ถูกบันทึกแล้ว"
Read this TICKET1's row again from data.mdb (temp copy)
├─ read fails ─► MdbError → ask "บันทึกต่อโดยไม่ตรวจกับตาชั่ง?" (the operator decides; audited)
├─ row missing ─► BLOCK "ไม่พบ Track ID … (อาจถูกยกเลิกในโปรแกรมชั่ง)"
└─ row found
    B3  TRUCK ≠ plate ──────────────► BLOCK "ทะเบียนไม่ตรงกับตาชั่ง"
    B4  DAYIN date ≠ WeighInAt date ─► BLOCK "วันชั่งเข้าไม่ตรง"
    STAT='2' (weighed out while the form was being filled)
    ├─ B5 that TICKET2 is on another record ─► BLOCK "ตั๋วชั่งออก … ผูกกับ Track ID … อยู่แล้ว"
    └─ no other BLOCK ──────────────────────► ApplyScale: attach the weigh-out result before saving
    W1  PRODUCT → chemical ≠ record's Chemical ─► WARN
B6  record has Chemical and a tank belongs to another chemical ─► BLOCK "รถ X เลือกถัง Y ไม่ได้"
W2  SG outside 1.0–1.6 · W3 temp outside 5–60 °C · W4 level above capacity · W5 after ≤ before ─► WARN

Result: any BLOCK → red dialog, stop · MdbError → ask · WARN → confirm · passes → Commit
Commit: Save → complete? → outbox (interpolated per configID) → removed from Arrivals → Aquadat push (if logged in)
```

---

## 1. What this session changed (read before testing)

### Goals (user's requirements)
1. The sidecar does not hook the main app's UI, so it relies on "ดึงจากตาชั่ง" or on watching the DB.
2. Check data.mdb every 3 s. When it changes, fetch `max(TICKET1)` with `STAT=1`, `TICKET2` null and `DAYIN` = today, and auto-fill the form.
3. After detection, the operator records ถ.พ., temperature, delivery note book/number and tank. A record is created with before/after levels still empty.
4. Weigh-out works the same way (`STAT=2`, `max(TICKET2)`).
5. Crosscheck against rules 2 and 4 on every save, to prevent duplicate processing.
6. UI: three tabs (ALUM / PACL / ส่งยอดสารเคมี). ALUM and PACL each have their own bar chart above a shared "บันทึกการรับ" section, in an iOS "liquid glass" style.

### Decisions made with the user
| Topic | Decision | Reason |
|---|---|---|
| UI tech | **WPF** (rewrite of the UI layer only) | WinForms can't render real glass. WebView2 may be missing on the Win10 plant PC. |
| Glass | `glass: "blur"` is the default (`acrylic` / `off` also available) | Acrylic lags on Win10 when the window moves, and the sidecar moves with the scale window every 150 ms. |
| Multiple trucks | **"both"**: `max(TICKET1)` auto-fills, other trucks queue as chips in "รถรอบันทึก" | Sample data has 2 Alum trucks 4 minutes apart (02/09 08:00 and 08:04). |
| Tab routing | Automatic, from `WDATA.PRODUCT` | `01` = อลูมิเนียมซัลเฟต → ALUM, `03` = PACl → PACL, `02` = ทั่วไป → ignored. |
| Rule 4 (adjusted) | `STAT='2' AND DAYOUT=Date()`, newest TICKET2, **matched to the record by TICKET1** | TICKET1 and TICKET2 are separate counters (first in can be last out), and 8 rows weigh out after midnight. |
| Tab switching | Only switch when the current tab's form is idle | Don't pull the tab away while the operator is typing. |

### Files
- **New:** `SidecarController.cs`, `ScaleWatcher.cs`, `CrossCheckSelfTest.cs` (`--cctest`), `GlobalUsings.cs`, `app.manifest`, `Ui\Glass.xaml`, `Ui\Kit.cs`, `Ui\MainWindow.cs`, `Ui\TankBarChart.cs`, `Ui\ReceivePanel.cs`, `Ui\AquadatPanel.cs`, `Ui\ScalePickerWindow.cs`, `Ui\GlassDialog.cs`, `Ui\GlassBackdrop.cs`
- **Changed:**
  - `ScaleDb.cs`: +PRODUCT, `DetectAsync`
  - `RecordStore.cs`: +Chemical/Product/Source, Step*, `FindByTicket2`, `Audit`, `DismissedSet`
  - `Config.cs` / `config.json`: `watchSeconds`, `forceCheckSeconds`, `productMap`, `glass`, width 400
  - `Aquadat.cs`: `DailySummary`, read-only
  - `Program.cs`: WPF Application
  - `TruckSidecar.csproj`: `UseWPF`
  - `mock\sim.ps1`: `-Product`
  - `docs\architecture.md`: §4.4, §4.5, §5
  - `mock\README.md`
- **Removed:** `SidecarForm.cs`, `TankGauge.cs`, `ScalePicker.cs`. The v1 source is kept in `_backup_v1\` and excluded from the build.
- **Untouched (by rule):** `Native.cs` (docking functions only added), `TankData.cs`, `Interpolation.cs`, `AquadatLogin.cs` (no brute-force), `AqSelfTest.cs`. truck93a.exe and data.mdb are never written.

### Mock data set aside
Old v1 data was in `mock\run\data`. It was moved to `mock\run\data.v1bak.223001` so the test would start from an empty store.

---

## 2. Test preparation (run in this order)

```
dotnet publish -c Release -o publish
powershell -ExecutionPolicy Bypass -File mock\start-mock.ps1 -Truck 70-1111 -Ticket 90001 -W1 25000
```
`start-mock.ps1` does the following:
1. Sets up `mock.mdb` and the config.
2. Starts the stub.
3. Opens Notepad.
4. Starts the sidecar and the watchdog.
5. Runs a weigh-in for PRODUCT 03 (PACL).

After any rebuild, update the mock copy with these steps:
1. Stop TruckWatchdog and TruckSidecar.
2. Publish.
3. Copy `publish\TruckSidecar.exe` into `mock\run\`.
4. Start the sidecar and the watchdog again.

UI helper: `ui.ps1` (Appendix A). Direct mdb edits: `jet.ps1`, which must run in **x86 PowerShell** (Appendix B).

---

## 3. Test sequence (run in order; each case continues from the state of the previous one)

### A. Render / build

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| A1 | Build | `dotnet build -c Release` / `publish` | No errors or warnings; exe about 58 MB | ✅ |
| A2 | Preview of all 3 tabs | `TruckSidecar.exe --preview <folder>` | `tab1..3.png`, nothing overflows at 400 px, Thai text renders, glass cards visible | ✅ ALUM / PACL / ส่งยอด are complete |
| A3 | Aquadat calculation unchanged | `TruckSidecar.exe --aqtest out.txt` | 8424=14.2 · 8423=3 + 8424=9 · 8037=19 | ✅ same as v1 |

### B. Workflow (rules 2 → 3 → 4)

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| B1 | Rule 2: auto-detect weigh-in | start-mock (weigh-in 70-1111 #90001 PRODUCT 03) | Within about 3 s: PACL tab selected, plate/Track filled (read-only), toast, step "ชั่งเข้า" ✓ | ✅ "ตาชั่ง 22:30:24", Edit ทะเบียนรถ=70-1111, Track=90001 |
| B2 | Auto-detect after restart | Kill the sidecar and open it again | The PACL form is filled again (the draft was unsaved) | ❌ → fixed (BUG-2) → ✅ |
| B3 | Rule 3: save with levels empty | Enter ถ.พ. 1.31, temp 31.5, book 12, number 0457, pick PACL C → บันทึก | Record created with `Chemical=PACL`, `Source=auto`, PACL C row with null levels; steps "ใบนำส่ง" ✓; chip "ขาด 3" appears | ✅ `records\20c1…json` + audit `save` |
| B4 | Second truck arrives while operator is typing | Type "ก่อนเติม 11.9" (not saved) → weigh-in 71-2222 #90002 **PRODUCT 01** | Stays on PACL, typed value kept; ALUM form filled in the background; toast "เติมฟอร์มไว้ที่แท็บ ALUM"; ALUM badge = 1 | ✅ (after the tab-switch fix, BUG-1) |
| B5 | PRODUCT 02 is ignored | weigh-in 72-3333 #90003 **PRODUCT 02** | No chip or badge appears | ✅ |
| B6 | Rule 4: auto-detect weigh-out | `sim weighout -Ticket 90001 -W2 11000 -Ticket2 80001` | Within about 3 s: TICKET2 80001, W2, net 14,000 kg fill in; step "ชั่งออก" ✓; unsaved "11.9" kept; audit `scale` | ✅ |
| B7 | Complete record → outbox | Enter หลังเติม 20.5 → บันทึก | "บันทึก Track ID 90001 (70-1111) ครบแล้ว ✓", form resets, outbox gets `8424 = 14.0` | ✅ `sqlite3 … aq_outbox` → `2026-09-25\|22:33\|8424\|14.0\|0000090001\|0` |

### C. Rule 5: crosscheck on save

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| C1 | Plate mismatch | Change TRUCK of #90002 in mdb to `99-9999` (jet.ps1) → fill the ALUM form → บันทึก | Block "ทะเบียนไม่ตรงกับตาชั่ง: บันทึก 71-2222 แต่ data.mdb เป็น 99-9999"; audit `crosscheck` | ✅ |
| C2 | Row deleted in the scale program | `DELETE … TICKET1='0000090002'` → บันทึก | Block "ไม่พบ Track ID 90002 ใน data.mdb"; chip disappears from the queue | ✅ |
| C3-UI-1 | Duplicate TICKET1 via the picker (same tab) | PACL tab → ดึงจากตาชั่ง → pick 70-1111 #90001 (already recorded) | Opens the existing record ("แก้ไข · ฉบับที่ 3") and does **not** create a new one | ✅ |
| C3-UI-2 | Switching tickets while editing a record | While editing #90001 → ดึงจากตาชั่ง → pick 75-6666 #90012 | Message "รายการนี้ผูกกับ Track ID 90001 แล้ว เปลี่ยนไม่ได้ · กด เริ่มใหม่ ก่อน", form unchanged | ✅ |
| C3-UI-3 | Picking an existing record **of the other chemical** | ALUM tab → ดึงจากตาชั่ง → pick 70-1111 #90001 (a PACL record) | Switches to the PACL tab and opens it there, toast "เป็นรถ PACL — เปิดที่แท็บ PACL"; the same record is never open in two forms | ❌ → fixed (BUG-4) → ✅ |
| C3-CC | Duplicate TICKET1 at the save check (bypassing the UI) | `--cctest` C3-1 / C3-2 | New record with the same TICKET1 → block "ถูกบันทึกแล้ว"; re-saving the same record → not blocked | ✅ |
| C4a | Duplicate TICKET2 during **automatic weigh-out** | Existing record: #90001 has TICKET2 80001. Weigh-in 74-5555 #90011 (PACL) → save the documents → `weighout -Ticket 90011 -Ticket2 80001` | TICKET2 is not written to #90011 (it stays "ยังไม่ชั่งออก"); audit `ticket2-duplicate` **once**; red toast "ไม่รับผลชั่งออกของ 74-5555 #90011 · ตั๋วออก 80001 ผูกกับ #90001 (70-1111) อยู่แล้ว" | ❌ → fixed (BUG-3) → ✅ |
| C4b | Duplicate TICKET2 on **save** | Weigh-in 75-6666 #90012 → เริ่มใหม่ → `weighout -Ticket 90012 -Ticket2 80001` → open the red chip "ชั่งออกแล้วแต่ยังไม่มีบันทึก" → fill the form → บันทึก | Block "ตั๋วชั่งออก 80001 ผูกกับ Track ID 90001 อยู่แล้ว", audit `crosscheck`, no record created for 90012 | ✅ |
| C4-CC | Duplicate TICKET2 at the save check | `--cctest` C4-1 | Block "ผูกกับ Track ID 90001" | ✅ |
| C5-UI-1 | Tank pills only show that tab's chemical | Dump the ALUM tab / PACL tab | ALUM = Alum A–D only · PACL = PACL A–D only | ✅ |
| C5-UI-2 | Picking a truck of the other chemical in the wrong tab | ALUM tab → ดึงจากตาชั่ง → pick 75-6666 #90012 (PACL, no record) | Message "รถคันนี้เป็น PACL — ไปบันทึกที่แท็บ PACL", ALUM form unchanged | ✅ |
| C5-UI-3 | Old v1 record with mixed tanks | Plant a v1 record `v1mixed…json` (#90022 77-8888, no Chemical, Alum A + PACL C) → restart → ALUM tab → open the chip → บันทึก → press "✕ PACL C" → บันทึก | Attempt 1: block "รถ ALUM เลือกถัง PACL C (PACL) ไม่ได้"; the PACL C row has a red ✕ button; after removing it the save succeeds (ฉบับที่ 2, `Chemical=ALUM`, fills = Alum A) | ✅ (the ✕ button was added in this session) |
| C5-CC | Other-chemical tank at the save check | `--cctest` C5-1…C5-4 | PACL C on an ALUM truck → block · mixed Alum A + PACL D → block · Alum A + B → pass · v1 with no Chemical → skipped (the UI always fills Chemical first) | ✅ |

**`--cctest` (new):** `TruckSidecar.exe --cctest <out.txt> <clean ALUM TICKET1>` runs the save check directly against the mdb in config (use `mock\run`). It stores records in a temporary folder and never touches real data. It expects #90001 and #90012 to both have weighed out with TICKET2 80001 (the state after C4). Latest run: **PASS 7 / FAIL 0**, exit code 0.

### D. Queue and dismiss

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| D1 | New truck while the form has unsaved data | Form holds 71-2222 (dirty) → weigh-in 73-4444 #90004 PRODUCT 01 | Chip "73-4444 #90004" appears; the form is untouched | ✅ |
| D2 | "ไม่ใช่รถสารเคมี" | Press ✕ on the chip → confirm "ซ่อน" | Chip disappears; `data\dismissed.json` = `["0000090004"]`; audit `dismiss` | ✅ |
| D3 | Dismiss survives restart | Kill and reopen the sidecar | #90004 does not reappear; #90001 (complete) does not reappear | ✅ |

### E. Docking regression (Notepad as the stand-in)

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| E1 | Follows move | MoveWindow(Notepad 200,120 700×600) | Sidecar sits beside it on the right | ✅ sidecar 895,120 400×640 |
| E2 | Maximize → AppBar | ShowWindow(Notepad, MAXIMIZE) | Notepad shrinks out of the way; sidecar reserves the right edge | ✅ Notepad to 1528, sidecar 1520–1920 |
| E3 | Restore | ShowWindow(Notepad, RESTORE) | Back beside the window, AppBar released | ✅ |
| E4 | Minimize, no incomplete tickets | ShowWindow(Notepad, MINIMIZE) | Sidecar hides | ✅ |
| E5 | Chem API down | API unreachable from the dev PC (curl timeout) | Pill "API —" in red; the rest works normally | ✅ correct behaviour (not a bug) |

### F. Nag mode + admin exit (Ctrl+Shift+F12)

**Nag mode** = the scale program is closed or minimized while a ticket is still incomplete → the sidecar will not hide, floats above every window (topmost), and shows a red header. It clears as soon as the scale program is back.
**Ctrl+Shift+F12** = the admin exit. Operators can't close the sidecar (Alt+F4 and X are ignored). This key combination writes `data\sidecar.stopped` so the watchdog won't reopen it, then closes. The next normal start deletes the flag and protection resumes.

Starting state: 2 incomplete tickets (#90011 has no weigh-out, #90022 has no weigh-out). Script: `nag.ps1` (Appendix D). It reads visible / topmost (WS_EX_TOPMOST) / rect / header from the real window.

| # | Test case | Steps | Expected | Result |
|---|---|---|---|---|
| N0 | Normal | Notepad open | visible, topmost=False, docked beside it, subtitle "ตั๋วที่ยังบันทึกไม่ครบ 2 คัน" | ✅ |
| N1 | Scale program minimized | ShowWindow(Notepad, MIN) | visible=True, **topmost=True**, header "⚠ บันทึกไม่ครบ" + "มีตั๋วค้าง 2 คัน — บันทึกให้ครบก่อนปิดโปรแกรมชั่ง" in red | ❌ topmost=False → fixed (BUG-7); header wrapped onto 2 lines → fixed (BUG-8) → ✅ |
| N2 | Scale program restored | ShowWindow(Notepad, RESTORE) | topmost=False, docked again, subtitle back **immediately** | ❌ subtitle blank until the 30 s refresh → fixed (BUG-8) → ✅ |
| N3 | Scale program closed | Stop-Process notepad | Same as N1 (visible + topmost + red); the watchdog exits (truck closed), the sidecar stays | ✅ |
| N4 | Scale program reopened | Start notepad | Docks to the new window, header back to normal | ✅ |
| N5 | Alt+F4 / X | PostMessage(WM_CLOSE) to the sidecar | Doesn't close (Closing is cancelled) | ✅ |
| F1 | Ctrl+Shift+F12 (normal) | Give the sidecar focus → keybd_event Ctrl+Shift+F12 | Process exits, `sidecar.stopped` written (valid JSON), watchdog **does not** reopen it within 10 s | ✅ (flag was invalid JSON → fixed, BUG-9) |
| F2 | Normal start after admin exit | Start TruckSidecar.exe → kill it | Flag is deleted at startup; after the kill the watchdog reopens it within about 3 s (PID 29320 → 29888) | ✅ |
| F3 | Ctrl+Shift+F12 during nag (topmost) | Minimize Notepad → press the keys | Closes normally, flag written | ✅ |
| F4 | Ctrl+Shift+F12 while AppBar is reserved | Maximize Notepad (1528 wide) → press the keys | AppBar released, Notepad expands back to 1928 | ✅ |

### G. Sequence scenarios: truck order at the scale (run in the mock 2026-09-25 23:08–23:20, all real results)

**Setup:** start from an empty `mock.mdb` (`sim.ps1 -Action reset`). The old data went to `mock\run\data.qa-c345.*`. All trucks are PACL (`-Product 03`) unless stated. Helpers are in Appendix E (`h.sh`: `SIM` / `JET` / `FILL` / `REC` / `VIEW`).
**Notation:** IN = `sim weighin` · OUT = `sim weighout` · "save" = fill SG/temp/book/number + pick a tank + บันทึก (levels left empty) · wait 5 s after each step (watcher checks every 3 s).

| # | Sequence | Steps and what the sidecar does (actual) | Final record | Result |
|---|---|---|---|---|
| **S1** | **IN 1 truck** | IN #91001 → ~3 s later the **PACL** tab opens, form shows 80-0001/91001, blue chip "รอบันทึก" → save (PACL A) → `REC #91001 t2=-` → OUT (T2 81001) → within ~3 s `t2=81001 W2=11000`, toast "ชั่งออกแล้ว · สุทธิ 14,000", step ④ ✓, still missing levels → enter 5/15 → "ครบแล้ว ✓" | #91001 complete → outbox | ✅ |
| **S2a** | **IN1 → IN2 back-to-back** (form untouched) | IN #91002 → form = 80-0002 · IN #91003 → form **switches to 80-0003** (max TICKET1, rule 2) · #91002 waits as a chip ("รถรอบันทึก (2)") | the operator opens each one | ✅ |
| **S2b** | **IN1 (operator typing) → IN2** | (tested as B4 / D1) IN2 arrives while the form holds typed data → the form **is not touched**, IN2 becomes a chip + toast; a different chemical fills its own tab in the background without switching tabs | — | ✅ |
| **S3** | **IN1 → IN2 → OUT1 → OUT2** (out in the same order as in) | IN #91004, IN #91005 → the form shows #91005, save (PACL A) → open the #91004 chip → save (PACL B) → OUT #91004 (81004) → only #91004 gets T2 → OUT #91005 (81005) → #91005 gets T2 | #91004 t2=81004 · #91005 t2=81005 | ✅ |
| **S4** | **IN1 → IN2 → OUT2 → OUT1** (reverse order) | IN #91006, IN #91007 → save both → **OUT #91007 first** (TICKET2 = 81006, the smaller number) → only #91007 gets it → OUT #91006 (81007) → #91006 gets it. **Matched by TICKET1**, so TICKET2 order doesn't matter | #91007 t2=81006 · #91006 t2=81007 | ✅ |
| **S5a** | **IN → OUT before saving** (the operator didn't save in time) | IN #91002 (not saved) → OUT (81002) → **red chip "ชั่งออกแล้วแต่ยังไม่มีบันทึก"** → open it → the form already shows the weigh-out → save → the record is created with TICKET2 immediately (rule 5 attaches the result) | #91002 t2=81002 | ✅ |
| **S5b** | **IN ALUM → IN PACL** (different chemicals back-to-back) | IN #91008 (01) → the **ALUM** tab opens with 81-0008 · IN #91009 (03) → switches to **PACL** with 80-0009 (the ALUM tab was untouched, so switching is allowed) · back on ALUM, the form still holds 81-0008 | each chemical in its own tab | ✅ |
| **S5c** | **IN general cargo (PRODUCT 02)** | IN #91099 99-0099 → mentioned **0 times** in the UI (no chip, no badge) | — | ✅ |
| **S5d** | **Same plate returns the same day** | 80-0001 already has #91001 (complete) → IN again as #91010 → the form shows 91010 (a new TICKET1) → save → **a separate record**, the old one is untouched | #91001 + #91010 (same plate, 2 trips) | ✅ |
| **S5e** | **IN yesterday → OUT today** (overnight) | *First run (old rule 2, DAYIN=today):* #91012 with `DAYIN=yesterday 23:50` was **not detected**, so the picker was needed, then OUT today attached T2. *Rerun after extending rule 2 to 24 h (W1–W5 below):* detected automatically | #91012 t2=81012 · #92001 t2=82001 | ✅ |
| **S5f** | **Sidecar closed while the truck weighs out** | save #91008 (ALUM) → stop the sidecar → OUT (81008) → start the sidecar → **first scan attaches T2** | #91008 t2=81008 | ✅ |
| **S5g** | **Weighed out yesterday while the sidecar was closed** (catch-up) | #91009 saved → stop the sidecar → OUT + set `DAYOUT=yesterday` (not in today's Outs) → start → `CatchUpOldWaitingAsync` looks it up by TICKET1 → T2 attached | #91009 t2=81009 | ✅ |
| **S5h** | **IN → cancelled in the scale program** (form untouched) | IN #91014 → form 83-0014 → delete the WDATA row → **the form clears itself** + red toast "ตั๋ว 83-0014 #91014 ไม่อยู่ในตาชั่งแล้ว (ถูกยกเลิก…)" | no record | ❌ form stayed stale → fixed (IMP-2) → ✅ |
| **S5h-2** | **IN → operator typing → cancelled** | IN #91015 → type SG → delete the row → **the form stays** (typed data isn't discarded) → บันทึก → BLOCK "ไม่พบ Track ID 91015 ใน data.mdb" | no record | ✅ |
| **S5i** | **Duplicate TICKET2** (restored mdb / reset counter) | see C4a/C4b: automatic OUT with a duplicate T2 is rejected + red toast · save is BLOCKed | — | ✅ |
| **S5j** | **Truck isn't chemical (hide)** | see D2/D3: ✕ on the chip → `dismissed.json` → doesn't come back after a restart | — | ✅ |
| **S5k** | **Restart with an unsaved auto draft** | see B2: after restart the first scan fills the form again (the draft is in memory only; not lost because it is recreated from data.mdb) | — | ✅ |

**Rule 2, 24 h window (added 2026-09-25 23:25; tested at 23:26 with `inWindowHours=24`).** Rows were inserted directly into mock.mdb with exact dates (`JET INSERT … DAYIN=DateAdd('d',-N,Date())`):

| # | DAYIN + TMIN | Hours ago | Expected | Actual | Result |
|---|---|---|---|---|---|
| W1 | yesterday 23:50 (#92001) | ~23.6 | detected, chip shows the date | chip "ชั่งเข้า 24/09 23:50 (เมื่อวาน)", form auto-filled 84-0001 | ✅ |
| W2 | yesterday 23:20 (#92002) | ~24.1 | **not** detected (just over the window) | not in the UI | ✅ |
| W3 | 2 days ago 23:55 (#92003) | ~47.5 | not detected | not in the UI | ✅ |
| W4 | today 23:27 (#92004) | 0 | detected + primary (max TICKET1) | form switched to 84-0004, #92001 waits as a chip | ✅ |
| W5 | open the #92001 chip → save → OUT today | — | save check passes (DAYIN yesterday = yesterday) → T2 attached | `#92001 t2=82001 W2=11000` | ✅ |


**Operating conclusions:**
- Trucks can **go out in any order** (S3/S4) because OUT matches by TICKET1, not by queue order or `max(TICKET2)` alone.
- A truck that's missed is always recoverable:
  - IN missed: blue chip (S2a).
  - OUT missed: red chip (S5a).
  - Sidecar closed: first scan after restart (S5f), or catch-up (S5g).
  - IN from yesterday within 24 h: auto-detected (W1). Older than 24 h: the picker.

**BUG-10 (found while running S3)**
- **What happened:** the driver pressed บันทึก, then clicked the #91002 chip right away. The click was lost, the form stayed on #91003, and the next FILL went into #91003 instead (PACL B + PACL C).
- **Cause:** the chip list was being rebuilt every 3 s (details in §C).
- **Fix and retest:** after the fix the chip's UI element keeps the same RuntimeId across 10 s of scans (`7.11588.50630832` → unchanged). The data from the lost click was corrected afterwards: PACL C was removed from #91003, and #91002 went through the S5a path.

---

## C. Problems found and fixed (details)

Summary: **10 bugs + 2 improvements**, all fixed and retested in the mock. Severity is judged by on-site impact: 🔴 serious (data wrong/lost, or the system becomes unusable) · 🟠 important (a mechanism doesn't work as designed) · 🟡 UX/minor.

| ID | Severity | Summary | Found in |
|---|---|---|---|
| BUG-1 | 🟡 | A new truck pulls the tab away while the operator is typing | B4 |
| BUG-2 | 🔴 | Async work started in the constructor → first scan/API run breaks silently | B2 |
| BUG-3 | 🟠 | Automatic OUT conflict invisible to the operator + audit logged every 30 s | C4a |
| BUG-4 | 🔴 | A record of the other chemical opens in the wrong tab → open in 2 forms | C3-UI-3 |
| IMP-1 | 🟡 | Other-chemical tank rows in v1 records can't be removed | C5-UI-3 |
| BUG-5 | 🔴 | One corrupt record file → sidecar crash-loops on startup | C5 setup |
| BUG-6 | 🟡 | Buddhist year in filenames / date parsing (th-TH) | C5 setup |
| BUG-7 | 🟠 | Nag mode doesn't float above other windows | N1 |
| BUG-8 | 🟡 | Nag header wraps + subtitle blank for 30 s | N1/N2 |
| BUG-9 | 🟡 | `sidecar.stopped` flag is invalid JSON | F1 |
| BUG-10 | 🟠 | The chip list is rebuilt every 3 s → clicks lost at random | S3 |
| IMP-2 | 🟡 | Auto-filled form of a cancelled ticket stays on screen | S5h |

---

### BUG-1 🟡: a new truck pulls the tab away while the operator is typing
- **Symptom:** the operator is typing in the PACL tab, an ALUM truck weighs in, and the window switches to the ALUM tab. The PACL data isn't lost, just hidden, and the operator loses their place.
- **Reproduce:** PACL form with typed data → `sim weighin -Product 01`.
- **Cause:** the first version of `MainWindow.OnArrival` only checked whether the **destination** tab's form was idle (the ALUM form was idle), then called `SelectTab()` without checking the tab currently in use.
- **Fix:** `OnArrival` now also checks the **current tab**. If its form isn't idle, the new truck is loaded into its own tab without switching, and the toast "เติมฟอร์มไว้ที่แท็บ X แล้ว (แท็บนี้ยังไม่ถูกแตะ)" is shown.
- **Verified:** B4. The tab stays on PACL, the typed "11.9" is kept, and the ALUM badge = 1.

### BUG-2 🔴: async work started in the window constructor before `app.Run()` breaks silently
- **Symptom:** after a restart, a truck waiting on the scale wasn't auto-filled into the form (only the badge showed), and the API pill stayed "—" even when the API was reachable. No error appeared anywhere.
- **Reproduce:** a STAT=1 truck is on the scale → restart the sidecar.
- **Cause:** the constructor called `_ = _c.ScanScaleAsync()` / `FetchApiAsync()` **before** `app.Run()`. At that point there is no `DispatcherSynchronizationContext`, so the continuation after `await Task.Run(...)` ran on a **thread-pool** thread. The `NewPrimaryArrival` / `Changed` events touched WPF controls from the wrong thread and threw `InvalidOperationException` inside a Task nobody awaited, so the exception disappeared. Worse, `_announced` had already recorded the TICKET1, so the truck was never announced again that session.
- **On-site impact:** every time the sidecar opens (or the watchdog relaunches it) while a truck is on the scale, auto-fill doesn't work for that truck.
- **Fix:** start the first round with `Dispatcher.BeginInvoke(async () => { await Scan; await FetchApi; })`, so everything runs on the UI thread after the dispatcher starts.
- **Lesson:** never start UI-related async work in a WPF window constructor.
- **Verified:** B2 / S5k. The form is filled again immediately after a restart.

### BUG-3 🟠: automatic OUT conflicts are invisible to the operator and fill the audit log
- **Symptom:** TICKET2 on OUT duplicated another record's. The system correctly rejected it, but (1) nothing appeared on screen, so the record stayed "ยังไม่ชั่งออก" forever without the operator knowing why, and (2) `audit.jsonl` got the same line every 30 s (1 → 3 lines in 65 s).
- **Reproduce:** C4a (#90011 weighs out with the same TICKET2 as #90001).
- **Cause:** `ApplyWeighOut` called `Store.Audit` + `return` every time it saw the conflict. The forced query every 30 s kept finding the same row, and there was no path to tell the UI.
- **Fix:** `SidecarController.Conflict(key, detail, message)` keeps a `HashSet _conflicts` (key = kind|TICKET1|value). It logs **once per case per session** and fires the new `ScaleConflict` event. `MainWindow` shows a red toast for 15 s. This covers both TICKET2 duplicates and plate mismatches on OUT.
- **Verified:** after a restart, 1 new audit line (7→8), none added over 70 s, and the toast reads "⚠ ไม่รับผลชั่งออกของ 74-5555 #90011 · ตั๋วออก 80001 ผูกกับ #90001 (70-1111) อยู่แล้ว — แจ้งผู้ดูแลตรวจ data.mdb".

### BUG-4 🔴: a record of the other chemical opens in the wrong tab and ends up open in 2 forms
- **Symptom:** in the ALUM tab, "ดึงจากตาชั่ง" → pick #90001 (a PACL record) → it opened in the ALUM form (Alum pills + a PACL C row), while the PACL tab was already editing the same record.
- **Cause:** in `PickFromScaleAsync`, the "already has a record → open it" branch ran before any chemical check and always used the current panel.
- **On-site impact:** each form holds its own copy of the record (`Clone`), so **whichever saves last overwrites the other's changes** (lost update). The PACL record also shows Alum tank choices.
- **Fix:** if `existing.ChemicalOr(...)` ≠ this tab's chemical, call `openInOwnTab` → `MainWindow` switches to that tab and calls `TryOpen` (no-op if already open) with the toast "#… เป็นรถ PACL — เปิดที่แท็บ PACL".
- **Verified:** C3-UI-3 (retest). The PACL tab opens and shows PACL pills.

### IMP-1 🟡: other-chemical tank rows in v1 records can't be removed
- **Symptom (found by designing C5-UI-3):** an old v1 record with Alum A + PACL C opened in the ALUM tab shows a PACL C row with no pill to toggle it off. The save check blocks it ("รถ ALUM เลือกถัง PACL C ไม่ได้"), so the operator would be **stuck with no way forward**. `Collect()` also re-added old fills every time.
- **Fix:** rows whose tank isn't in this tab show a red **"✕ PACL C"** button (UIA name "ลบ PACL C"). `Collect()` builds fills only from rows on screen (this tab's tanks first, then others), so nothing is dropped without the operator seeing it.
- **Verified:** C5-UI-3. First save BLOCKed → press ✕ → save passes (ฉบับที่ 2, `Chemical=ALUM`, fills = Alum A).

### BUG-5 🔴: one corrupt record file makes the sidecar crash-loop on startup (also in v1)
- **Symptom:** the sidecar exited right after opening, the watchdog relaunched it, and it exited again, with the PID changing every few seconds and no window. The .NET Runtime event showed `JsonException: 'v' is an invalid escapable character … Path: $.CreatedBy`.
- **Reproduce:** put a `records\*.json` file with bad JSON in place (it happened by accident while preparing the C5 test file).
- **Cause:** the `RecordStore` constructor ran `JsonSerializer.Deserialize` on every file with no try/catch. It runs before `app.Run()`, so `DispatcherUnhandledException` doesn't cover it.
- **On-site impact:** one file broken by a power cut / full disk / OneDrive sync conflict / someone editing it by hand leaves **the operator with no sidecar at all**, and the watchdog keeps relaunching it for nothing.
- **Fix:** catch per file → move it to `records\_bad\<name>_<yyyyMMddHHmmss>.json` → audit `bad-record-file` (file name + error) → `BadFiles` → red toast at startup "ไฟล์บันทึกเสีย N ไฟล์ ถูกย้ายไป data\records\_bad\ — แจ้งผู้ดูแลตรวจ". The program keeps running.
- **Verified:** the process runs, the file moved to `_bad`, audit has the line.

### BUG-6 🟡: Buddhist year (th-TH culture)
- **Symptom:** the `_bad` filename came out as `…_25690925225340.json` (year 2569).
- **Cause:** the machine's culture is th-TH, which uses the Thai Buddhist calendar, and `$"{DateTime.Now:yyyyMMdd…}"` follows the current culture. In `AquadatPanel`, `DateTime.TryParse("2026-09-25")` was also at risk of reading 2026 as B.E., which would put the wrong weekday on the daily totals table.
- **Fix:** the filename uses `ToString(..., CultureInfo.InvariantCulture)`, and `AquadatPanel` uses `TryParseExact("yyyy-MM-dd", InvariantCulture)`. Outbox/ledger dates and the Aquadat payload already used InvariantCulture (`--aqtest` unchanged).
- **Watch for:** any new code that writes dates to files or payloads must specify InvariantCulture.

### BUG-7 🟠: nag mode doesn't float above other windows
- **Symptom:** with the scale program minimized/closed and incomplete tickets present, the header turned red correctly, but `WS_EX_TOPMOST` was never set (`topmost=False`) even though the code set `Window.Topmost = true` on every tick.
- **Cause:** WPF's `Window.Topmost` didn't take effect on this window (WPF window + AllowsTransparency + WS_EX_TOOLWINDOW that we set ourselves + Win32 SetWindowPos repositioning every tick), and nothing checked whether it actually worked.
- **On-site impact:** other programs could cover the sidecar, the operator never sees the warning, and **nag mode fails at its main job**.
- **Fix:** `MainWindow.SetTopmost(bool)` calls `SetWindowPos(HWND_TOPMOST / HWND_NOTOPMOST, NOMOVE|NOSIZE|NOACTIVATE)` directly, reading the real ex-style every tick so it **repairs itself** if something removes the flag. WPF `Topmost` is no longer used. Constants were added to `Native.cs`.
- **Verified:** N1/N3 topmost=True · N2/N4 topmost=False.

### BUG-8 🟡: nag header layout
- **Symptom:** the title "⚠ มีตั๋วที่ยังบันทึกไม่ครบ" at 18pt didn't fit beside the 2 pills, so the ⚠ sat alone on its own line. After leaving nag the subtitle was blank until the next 30 s refresh.
- **Cause:** the title was too long for a 400 px window. When nag ended, `Track()` set the subtitle to "" and waited for `_minuteTimer` to refill it.
- **Fix:** short title "⚠ บันทึกไม่ครบ" + red subtitle "มีตั๋วค้าง N คัน — บันทึกให้ครบก่อนปิดโปรแกรมชั่ง" (in `RefreshAll`). `RefreshAll()` runs immediately whenever nag mode turns on or off.
- **Verified:** screenshot `nag2.png`, N2 shows the subtitle straight away.

### BUG-9 🟡: `sidecar.stopped` flag is invalid JSON (also in v1)
- **Symptom:** the content was `{"at":"…","by":"PEERAPAT\peera"}`, where `\p` is an invalid JSON escape.
- **Cause:** the JSON was built with string interpolation without escaping the `\` in `DOMAIN\user`.
- **On-site impact:** nothing today (the watchdog only checks that the file exists), but it's a trap for anyone who parses it later.
- **Fix:** write it with `JsonSerializer.Serialize(new { at, by })`.
- **Verified:** `ConvertFrom-Json` reads it, `by=PEERAPAT\peera`, and the watchdog still respects the flag (F1).

### BUG-10 🟠: the "รถรอบันทึก" chip list is rebuilt every 3 s → clicks lost at random
- **Symptom:** in S3 the test driver pressed บันทึก on #91003, then clicked the #91002 chip. The click did nothing (the form stayed on #91003), so the next typing went into #91003 (PACL B + PACL C). Clicking the same chip again later worked.
- **Cause:** every scale check (every 3 s) calls `Changed` → `RefreshAll` → `RefreshQueue()`, which ran `_queue.Children.Clear()` and **created every chip button again**, even when the data hadn't changed. Any element the user (or UIA) was holding became a dead object. `AquadatPanel.Refresh` also rebuilt its table and queried SQLite every 3 s.
- **On-site impact:** a real mouse click could fall in the gap: press on the old button, the list rebuilds, release on the new button, and WPF doesn't count it as a click. The chip "sometimes doesn't respond". Worse, the operator may not notice and keep typing into the form of the **previous truck**.
- **Fix:** `RefreshQueue()` builds a signature (arrivals: TICKET1+state+time · pending: Id+Version · the open record · the draft) and **rebuilds only when it changes**. `AquadatPanel` does the same for its table (date/configID/total/trip count/status).
- **Verified:** the #91002 chip's RuntimeId was `7.11588.50630832` both before and after 10 s of scans (not rebuilt). S3/S4 were re-run and the chip clicks work every time.

### IMP-2 🟡: auto-filled form of a cancelled ticket stays on screen
- **Symptom:** in S5h a truck weighed in (form auto-filled), then the ticket was cancelled in the scale program. The chip disappeared but the form still showed 83-0013, which could confuse the operator (saving would be blocked anyway).
- **Fix:** `ReceivePanel.DropIfCancelled()` runs in `RefreshAll` after at least one successful scan. It clears the form only when **all** of these hold: the form came from auto (`_pickedSource="auto"`), the operator hasn't typed (`!_dirty`), no record is open, the TICKET1 is gone from Ins/Outs, and it has no record. It then shows a red toast "ตั๋ว … ไม่อยู่ในตาชั่งแล้ว (ถูกยกเลิกในโปรแกรมชั่ง) — ล้างฟอร์มให้แล้ว". If the operator has typed anything, the form is kept and the save check BLOCKs it (S5h-2).
- **Verified:** S5h (form clears + toast), S5h-2 (form kept + BLOCK "ไม่พบ Track ID 91015").

### Small cleanup
- The 🚚 / ⚖ emoji rendered badly with Leelawadee UI, so they were removed from toasts.
- `AutomationProperties.Name` was added to fields (from their labels), the tabs, and the ✕ buttons, for accessibility and UIA testing.

---

## D. Not yet tested / next steps

1. **Real Win10 plant PC:** does `blur` work (SetWindowCompositionAttribute)? Test `acrylic` for drag lag, and fall back to `off` if needed.
2. ~~Nag mode with v2 visuals~~: tested in the mock (N0–N5). Still to check: nag with the real truck93a.
3. **Real truck93a + dongle:** check that data.mdb's mtime updates while VB6 holds the file open. If it doesn't, the forced query every 30 s is the backstop, and detection may take up to 30 s.
4. ~~Duplicate TICKET2 (C4)~~: tested (C4a/C4b).
4b. ~~Weigh-in from yesterday not auto-detected~~: rule 2 extended to a 24 h window (`inWindowHours`), tested W1–W5. On site, watch for abandoned STAT=1 tickets that show up as chips for 24 h (hide them with ✕).
5. ~~Ctrl+Shift+F12~~: tested (F1–F4) by sending real keystrokes to the sidecar window.
6. ~~Watchdog stop-flag protocol with v2~~: tested (F1, F2).
7. Set `mdbPath` for the plant (likely `D:\TRUCK93A\data.mdb`), plus `targetProcess: "truck93a"`.

---

## Appendix A: `ui.ps1` (UI Automation helper; run in normal PowerShell)

```powershell
# ui.ps1 shot <file> | dump | set <name> <value> | click <name> | setlevel <idx> <value>
# Prefix "dlg" = act on the dialog window (e.g. dlgdump)
param([string]$Cmd, [string]$A, [string]$B)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$p = Get-Process TruckSidecar -ErrorAction Stop | Select-Object -First 1
$root = [Windows.Automation.AutomationElement]::RootElement
$cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$wins = $root.FindAll([Windows.Automation.TreeScope]::Children, $cond)
$win = $wins | Where-Object { $_.Current.Name -eq 'การรับสารเคมี' } | Select-Object -First 1
if (-not $win) { $win = $wins[0] }
if ($Cmd -like 'dlg*') {
  $win = $wins | Where-Object { $_.Current.Name -ne 'การรับสารเคมี' } | Select-Object -First 1
  if (-not $win) { 'no dialog'; exit }
  $Cmd = $Cmd.Substring(3)
}
function All { $win.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) }
function Val($e) { try { $e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { '' } }
switch ($Cmd) {
  'shot' {
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $bmp.Save($A); "shot $($r)"
  }
  'dump' {
    foreach ($e in All) {
      $c = $e.Current
      if ($c.IsOffscreen) { continue }
      $t = $c.ControlType.ProgrammaticName -replace 'ControlType\.',''
      if ($t -in 'Text','Edit','Button','CheckBox','RadioButton') { "{0,-12} [{1}] {2}" -f $t, $c.Name, (Val $e) }
    }
  }
  'set' {
    $e = All | Where-Object { $_.Current.Name -eq $A -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Edit -and -not $_.Current.IsOffscreen } | Select-Object -First 1
    $e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($B); "set $A=$B"
  }
  'setlevel' {
    $edits = @(All | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Edit -and $_.Current.Name -eq '' -and -not $_.Current.IsOffscreen })
    $edits[[int]$A].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($B); "level[$A]=$B (of $($edits.Count))"
  }
  'click' {
    $e = All | Where-Object { $_.Current.Name -eq $A -and -not $_.Current.IsOffscreen } | Select-Object -First 1
    if (-not $e) { "not found: $A"; break }
    # text inside a chip → walk up to the parent button that can be clicked
    $tw = [Windows.Automation.TreeWalker]::ControlViewWalker
    while ($e -and $e.Current.ControlType -eq [Windows.Automation.ControlType]::Text) { $e = $tw.GetParent($e) }
    try { $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { try { $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle() } catch { $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() } }
    "clicked $A"
  }
}
```

Example (B3):
```
ui.ps1 set "ถ.พ." 1.31 ; ui.ps1 set "อุณหภูมิ (°C)" 31.5 ; ui.ps1 set "ใบนำส่ง เล่มที่" 12
ui.ps1 set "ใบนำส่ง เลขที่" 0457 ; ui.ps1 click "PACL C" ; ui.ps1 click "บันทึก"
```
Notes:
- The level fields have no name, so use `setlevel 0` (before) and `setlevel 1` (after).
- Tabs are named `ALUM` / `PACL` / `ส่งยอดสารเคมี`. Click them with `SelectionItem`.
- The GlassDialog has an Owner, so it shows up in the same tree as the main window: `dump` / `click "ตกลง"` works directly.

## Appendix B: `jet.ps1` (edit mock.mdb directly; **must run with x86 PowerShell**)

```powershell
param([string]$Sql)
$c = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=C:\Users\peera\OneDrive\Desktop\TruckSidecar\mock\run\mock.mdb"); $c.Open()
$cmd = $c.CreateCommand(); $cmd.CommandText = $Sql; "rows=" + $cmd.ExecuteNonQuery(); $c.Close()
```
```
%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File jet.ps1 "UPDATE WDATA SET TRUCK='99-9999' WHERE TICKET1='0000090002'"
```
> ⚠ Use it only on `mock.mdb`. Never point it at the real data.mdb (constraint: data.mdb is read-only).

## Appendix C: checking the outbox
```
sqlite3 mock\run\data\aquadat_outbox.db "select date,time,config_id,value,source_track,synced from aq_outbox;"
```

## Appendix D: `nag.ps1` (nag mode / admin exit tests; normal PowerShell)

Usage: `nag.ps1 state` · `nag.ps1 np min|max|restore|close|open` · `nag.ps1 wmclose` (like Alt+F4) · `nag.ps1 keys` (focus the sidecar, then press Ctrl+Shift+F12)

```powershell
# nag.ps1 state | np <min|max|restore|close|open> | wmclose | keys
param([string]$Cmd, [string]$A)
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class N {
 public delegate bool P(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(P cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);
 [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 public static IntPtr ByTitle(uint pid, string title) { IntPtr found = IntPtr.Zero;
  EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true;
   var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == title) { found = h; return false; } return true; }, IntPtr.Zero);
  return found; }
 public struct R { public int L,T,Ri,B; }
 public static IntPtr Big(uint pid) { IntPtr best = IntPtr.Zero; long area = 0;
  EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true;
   R r; GetWindowRect(h, out r); long a = (long)(r.Ri-r.L)*(r.B-r.T); if (a > area && GetWindowTextLength(h) > 0) { area = a; best = h; } return true; }, IntPtr.Zero);
  return best; }
}
'@
function Hwnd($name) { $p = Get-Process $name -EA 0 | Select-Object -First 1; if (-not $p) { return [IntPtr]::Zero }
  if ($name -eq 'TruckSidecar') { return [N]::ByTitle([uint32]$p.Id, 'การรับสารเคมี') }; [N]::Big([uint32]$p.Id) }
function State {
  $h = Hwnd TruckSidecar
  if ($h -eq [IntPtr]::Zero) { return "sidecar: NO PROCESS" }
  $r = New-Object N+R; [void][N]::GetWindowRect($h, [ref]$r)
  $top = ([N]::GetWindowLong($h, -20) -band 0x8) -ne 0
  $title = ''
  try {
    $e = [Windows.Automation.AutomationElement]::FromHandle($h)
    $t = $e.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
    $title = ($t | Select-Object -First 2 | ForEach-Object { $_.Current.Name }) -join ' | '
  } catch {}
  "sidecar: visible=$([N]::IsWindowVisible($h)) topmost=$top rect=$($r.L),$($r.T)-$($r.Ri),$($r.B) header='$title'"
}
switch ($Cmd) {
  'state' { State }
  'np' {
    $h = Hwnd notepad
    switch ($A) { 'min' { [void][N]::ShowWindow($h, 6) } 'max' { [void][N]::ShowWindow($h, 3) } 'restore' { [void][N]::ShowWindow($h, 9) }
      'close' { Get-Process notepad -EA 0 | Stop-Process -Force } 'open' { Start-Process "$env:SystemRoot\System32\notepad.exe" } }
    "notepad $A"
  }
  'wmclose' { [void][N]::PostMessage((Hwnd TruckSidecar), 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); "WM_CLOSE sent" }
  'keys' {
    # ให้ sidecar ได้ focus (ผ่าน UIA SetFocus ที่ช่อง ถ.พ.) แล้วกด Ctrl+Shift+F12
    $h = Hwnd TruckSidecar
    $e = [Windows.Automation.AutomationElement]::FromHandle($h)
    $f = $e.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'ถ.พ.')))
    [void][N]::SetForegroundWindow($h); if ($f) { try { $f.SetFocus() } catch {} }
    Start-Sleep -Milliseconds 400
    "foreground is sidecar: " + ([N]::GetForegroundWindow() -eq $h)
    [N]::keybd_event(0x11,0,0,[UIntPtr]::Zero); [N]::keybd_event(0x10,0,0,[UIntPtr]::Zero); [N]::keybd_event(0x7B,0,0,[UIntPtr]::Zero)
    [N]::keybd_event(0x7B,0,2,[UIntPtr]::Zero); [N]::keybd_event(0x10,0,2,[UIntPtr]::Zero); [N]::keybd_event(0x11,0,2,[UIntPtr]::Zero)
    "Ctrl+Shift+F12 sent"
  }
}
```

> Note: `keys` uses `keybd_event`, so the sidecar must be the foreground window. The script calls `SetForegroundWindow` + UIA `SetFocus` on the ถ.พ. field first, and prints `foreground is sidecar: True` to confirm.

## Appendix E: `h.sh` (bash helpers for the §G scenarios; source it before use)

```bash
SP=/c/Users/peera/AppData/Local/Temp/claude/C--Users-peera-OneDrive-Desktop-TRUCK93A/3b30cab4-fac2-42b3-beb8-efab3ef276a3/scratchpad
U(){ powershell -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w $SP/ui.ps1)" "$@"; }
SIM(){ powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\Users\peera\OneDrive\Desktop\TruckSidecar\mock\sim.ps1' "$@" >/dev/null; }
JET(){ /c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w $SP/jet.ps1)" "$1"; }
REC(){ python -c "
import json,glob
for f in sorted(glob.glob(r'C:/Users/peera/OneDrive/Desktop/TruckSidecar/mock/run/data/records/*.json')):
    r=json.load(open(f,encoding='utf-8'))
    print('   REC #%s %s chem=%s t2=%s W2=%s fills=%s' % (r['TrackId'].lstrip('0'), r['Plate'], r.get('Chemical'), r['Ticket2'].lstrip('0') or '-', r['W2'], ','.join(x['Tank'] for x in r['Fills'])))
"; }
VIEW(){ U dump | grep -E "^Text +\[ระดับถัง|^Edit +\[(ทะเบียนรถ|Track ID)\]|รถรอบันทึก|^Text +\[[0-9]{2}-[0-9]{4}  #|ชั่งเข้า .* · |ชั่งออกแล้ว|ขาด [0-9]|ชั่งออก [0-9]" | sed 's/^/   /'; }
FILL(){ U set "ถ.พ." 1.30 >/dev/null; U set "อุณหภูมิ (°C)" 30 >/dev/null; U set "ใบนำส่ง เล่มที่" 1 >/dev/null; U set "ใบนำส่ง เลขที่" "$1" >/dev/null; U click "$2" >/dev/null; U click "บันทึก" >/dev/null; sleep 3; }
```

> `SIM` = sim.ps1 (normal PS, relaunches itself as x86) · `JET "<SQL>"` = jet.ps1 on mock.mdb (x86) · `REC` = print every record · `VIEW` = the form/chip lines from UIA · `FILL <number> "<tank>"` = fill the documents + pick a tank + บันทึก
> Note: `--cctest` needs #90001/#90012 in mock.mdb (the state after C4). After `sim reset` for §G, redo C4 before running `--cctest` again.
