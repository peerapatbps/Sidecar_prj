# TruckSidecar — Design & Architecture

## 1. Problem

At the plant, receiving one chemical delivery today means **three separate acts**:
1. weigh the truck in/out on the legacy program `truck93a.exe` (Truck Scale V9.3A),
2. run the program, and
3. hand-write a paper slip (specific gravity, temperature, delivery-note book/number, tank levels before/after).

The paper is unsearchable and untraceable, and the legacy program is a **legally certified weighing instrument that must not be modified**. We need one screen, one action, and a traceable digital record — without touching the certified program.

## 2. Guiding constraints

| Constraint | Consequence in the design |
|---|---|
| Legacy exe must not be modified | Sidecar is a **separate process**; no DLL injection, no patching |
| `data.mdb` is the certified data source | Read from a **temp copy**, read-only; never lock or write it |
| Weighing values must stay from the certified program | Plate / Track ID / weights are **read-only**, never entered by hand |
| Must be hard to skip (paper habit) | Sidecar is **glued** to the legacy window; nags on incomplete tickets |
| Plant PC is old / x86 | Target **x86**, self-contained single-exe (no runtime install) |

## 3. Component overview

```
              ┌────────────────────────── plant PC ──────────────────────────┐
              │                                                               │
  data.mdb ◄──┼── (read-only temp copy) ── ScaleDb ──┐                        │
 (WDATA)      │                                      │                        │
              │   truck93a.exe (certified)           ▼                        │
              │   ┌───────────────┐        ┌───────────────────────────┐      │
              │   │ weight display│◄─glued─│  TruckSidecar (this app)  │      │
              │   │  buttons ...  │        │  • pick weigh row         │      │
              │   └───────────────┘        │  • SG/temp/bill/levels    │      │
              │                            │  • tank status + predict  │      │
              │                            │  • interpolate → outbox   │      │
              │                            └───────────┬───────────────┘      │
              │                                        │                      │
              │   JSON record store + audit.jsonl ◄────┤ (full traceable rec) │
              │   aquadat_outbox.db (SQLite)     ◄─────┘ (daily income totals) │
              └───────────────────────────────────────┼──────────────────────┘
                                                       │
  chem summary API ─────────────────────────────────► │ (tank levels, 2-hourly)
  http://172.16.198.150:443/api/chem/summary           │
                                                        ▼
                              Aquadat GraphQL  https://aquadat.mwa.co.th/graphql
                              mutation update_transaction_datavalues (daily 24:00 totals)
```

Two data sinks, on purpose:
- **JSON record store + `audit.jsonl`** — the complete, append-only, traceable record of everything the operator entered plus the scale data. This is the source of truth for audit.
- **`aquadat_outbox.db`** (SQLite) — only the aggregated **chemical income in tonnes** per Aquadat configID, ready to push.

## 4. How it stays compatible with the legacy app

### 4.1 Window docking (no code contact)
`Native.cs` uses Win32 only:
- `EnumWindows` + process match → find the legacy main window (largest visible top-level of `truck93a`).
- A 150 ms timer mirrors the legacy window's **open / move / minimize / restore / close**: the sidecar follows so the two read as one screen. Verified with a stand-in app: open→dock, move→follow, minimize→hide, restore→show, close→hide, reopen→re-dock.
- `WS_EX_TOOLWINDOW` + `ShowWithoutActivation` → the sidecar never steals focus and is not in Alt-Tab.
- Z-order is pinned **one level above** the legacy window only, so it doesn't float over unrelated apps.

### 4.2 Full-screen case (AppBar)
If the legacy window is **maximized**, docking beside it would cover its right-side buttons (◀ ▶ W1 W2). Instead the sidecar registers as a **right-edge AppBar** (`SHAppBarMessage`), exactly like the Windows taskbar: Windows shrinks the maximized window to the left so nothing overlaps. Verified on 1920px: target 0–1540, sidecar 1540–1920; on restore the work area returns to normal. The AppBar is released when the target is un-maximized or the sidecar closes.

### 4.3 Reading the scale data
`ScaleDb.cs` copies `data.mdb` to a temp file (`FileShare.ReadWrite|Delete`) and reads the copy via Jet OLEDB in read-only mode. The original file is never locked, so the certified program keeps working untouched.
- `WDATA.STAT` text: `'1'` = weighed in / not out yet, `'2'` = weighed out.
- Track ID = `TICKET1` (present from weigh-in through weigh-out); `TICKET2`/`W2` appear only after weigh-out.
- `WDATA.PRODUCT`: `01` = อลูมิเนียมซัลเฟต (ALUM), `03` = PACl, `02` = ทั่วไป — mapped by `productMap` in config; unmapped codes are ignored.

### 4.4 Auto-detection (v2, `ScaleWatcher.cs`)
Only one chemical truck is normally on the scale at a time (driver walks over and back), so a change to `data.mdb` ≈ "the truck on the scale now".
- Every `watchSeconds` (3 s) the sidecar compares `LastWriteTime`+`Length` of `data.mdb`; on change it queries a temp copy. A forced query also runs every `forceCheckSeconds` (30 s) because NTFS can report a stale mtime while VB6 holds the file open.
- **Weigh-in (rule 2):** `STAT='1' AND TICKET2 empty` weighed in within `inWindowHours` (24 h; Jet pre-filters `DAYIN>=yesterday`, C# checks `DAYIN+TMIN`), newest `TICKET1` = the primary truck → form auto-fills. Other un-recorded rows (two Alum trucks can arrive minutes apart) are listed as chips in "รถรอบันทึก" so none are missed.
- **Weigh-out (rule 4):** `STAT='2' AND DAYOUT=today`, newest `TICKET2`. Uses `DAYOUT` (not `DAYIN`) because some trucks weigh out after midnight, and matches by `TICKET1` because TICKET1/TICKET2 are independent counters (first-in can be last-out). `TICKET2`/`W2` are written into the existing record (audit action `scale`). Records older than today are caught up by a direct `TICKET1` lookup.
- Rows the operator marks **"ไม่ใช่รถสารเคมี"** go to `data/dismissed.json` and never pop up again.

### 4.5 Crosscheck before save (v2, rule 5)
`SidecarController.CrossCheckAsync` re-reads the `TICKET1` row from `data.mdb` at save time. **Blocks:** row missing (cancelled in the scale program), plate or weigh-in date differs, `TICKET1` or `TICKET2` already bound to another record, tank of a different chemical. **Warns:** PRODUCT ≠ record chemical, SG/temperature/level out of range. If the mdb can't be read the operator may explicitly save without the check. Every block/warn is logged to `audit.jsonl` (action `crosscheck`).

## 5. Workflow (operator's view, v2)

UI = WPF "liquid glass" window (Win10 blur-behind; `glass`: `blur` | `acrylic` | `off`) with tabs **ALUM · PACL · ส่งยอดสารเคมี**. ALUM/PACL each show a bar chart of that chemical's tanks (fill = API level, dashed line = predicted now, red dashed = low %, status chip ON/STBY/REPAIR) above the same "รถรอบันทึก" list + "บันทึกการรับ" form.

1. **Weigh-in** on the legacy program as always → within ~3 s the sidecar switches to the truck's tab (by PRODUCT) and fills plate + Track ID (read-only). If the operator is typing in another tab, the tab is not switched — a toast says the form is waiting.
2. Operator enters **ถ.พ., อุณหภูมิ, ใบนำส่ง เล่ม/เลขที่**, taps the **tank pill(s)** to fill, and saves → the record exists with empty before/after levels (step "ใบนำส่ง" ✓).
3. Operator fills **before/after levels** (m³) after unloading; "เติมเข้า" is computed.
4. **Weigh-out** → the sidecar captures `TICKET2`/`W2`/net weight automatically; if the record is open, unsaved typing is kept.
5. When complete → chemical income (t) is interpolated across tanks and queued per configID (daily 24:00 totals).
6. Tab **ส่งยอดสารเคมี**: log in (username prefilled, password typed each time), see daily totals per configID (ส่งแล้ว / ค้างส่ง), press **ส่ง Chemical income**.
7. **"ดึงจากตาชั่ง"** stays as a manual fallback picker.

Incomplete tickets stay in "รถรอบันทึก" (orange); if the legacy program is closed/minimized while something is incomplete, the sidecar refuses to hide and shows a red header.

## 6. Interpolation & the Aquadat payload

- Net delivered tonnes = `|W1 − W2| / 1000`.
- Split across tanks by fill volume `Σ(after−before)`; each tank maps to an income configID.
- Aggregate per configID: **Alum A+B=8037, Alum C+D=8036, PACL A+B=8423, PACL C+D=8424**.
- **Daily cut-off**: `time` is always `"24:00"`; the value sent for a configID is the **sum of all that day's trips**, which the server upserts. Re-pushing after another trip overwrites with the higher daily total.

Verified examples (`--aqtest`):
- 1 truck → PACL C, net 14.2 t ⇒ `8424 = 14.2`
- PACL A (3 m³) + PACL C (9 m³), net 12 t ⇒ `8423 = 3`, `8424 = 9`
- Alum A+B (10+10 m³), net 19 t ⇒ `8037 = 19`

## 7. Authentication (and why no brute-force)

The existing Uroboros app keeps its Aquadat password current by cycling through the forced 8-identical-digit passwords. **That loop is not reproduced here.** The sidecar uses a plain **interactive login**: the operator types the username (prefilled from config) and password; a single POST to the Enroll endpoint returns a token held **in memory only** (never written to disk). If the token expires, the operator logs in again. This keeps credential handling explicit and human-driven, and avoids automated password guessing entirely.

## 8. Traceability & safeguards

- **Append-only audit** (`audit.jsonl`): every save records user, machine, time, version, and whether the record was complete; edits add a new version rather than overwriting.
- **Read-only scale fields**: weights/plate/Track ID can only come from the certified program via the picker; duplicates of a Track ID are blocked.
- **Validation warnings**: SG outside 1.0–1.6, level over tank capacity, after-level not greater than before.
- **Outbox `detail_json`**: keeps the per-tank allocation behind each aggregated income value, so any Aquadat number can be traced back to the trucks that produced it.
- **Separation of stores**: pushing to Aquadat (aggregated tonnes) never risks the full traceable record (JSON/audit), and vice-versa.

## 9. What it deliberately does NOT do

- No writes to `data.mdb`; no changes to `truck93a.exe`.
- No typing/clicking into the legacy program on the operator's behalf.
- No storing of the Aquadat password; no automated password guessing.
- No covering of the legacy weight display.

## 10. Build & verify

```
dotnet publish -c Release -o publish          # self-contained x86 single exe
TruckSidecar.exe --preview out.png            # render UI to image (no docking/writes)
TruckSidecar.exe --aqtest  out.txt            # print interpolation + Aquadat payload
```

Config lives in `config.json` (tank→configID map, mdb path, API URLs, poll intervals, prefilled username). See the handoff at [../handoff/handoff2509.md](../handoff/handoff2509.md) for current status and open items.
