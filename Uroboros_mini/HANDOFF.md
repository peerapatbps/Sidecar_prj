# Uroboros Mini Handoff

## Overview

`Uroboros.MiniUploader` is a standalone `.NET 10` Windows console app built to handle only the `MDB upload` workflow from the main Uroboros system.

Goal:
- run automatically when opened
- avoid dependency on BellBeast pause/play flow
- keep existing Google Drive credential/config behavior
- support simple scheduling and duplicate prevention

Target directory:
- `C:\Users\peera\OneDrive\Desktop\Uroboros_mini`

## Current Behavior

Workflow:
1. Read `config_\config.ini`
2. Read `Production.mdb_file_location`
3. Copy source MDB into `media\data.mdb`
4. Compare/update `Production.timestamp`
5. Convert `media\data.mdb` into SQLite `chem` snapshot
6. Resolve active snapshot via `chem.current.txt`
7. Upload active snapshot to Google Drive as `chem.db`

Scheduling:
- polling loop controlled by `mini-uploader.json`
- optional `DailyTimes`
- duplicate slot protection via `uploader-state.json`
- single-instance protection via named mutex

## Important Files

Project files:
- [Program.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/Program.cs)
- [MdbReaderSafe.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/MdbReaderSafe.cs)
- [GoogleDriveMini.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/GoogleDriveMini.cs)
- [Logging.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/Logging.cs)
- [SingleInstanceGuard.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/SingleInstanceGuard.cs)
- [Uroboros.MiniUploader.csproj](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/Uroboros.MiniUploader.csproj)
- [app.manifest](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/app.manifest)

Runtime config:
- [mini-uploader.json](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/mini-uploader.json)
- [config.ini](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/config_/config.ini)

Assets:
- [uroboros-mini.ico](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/uroboros-mini.ico)
- [uroboros-mini.png](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/uroboros-mini.png)

## Runtime Layout Expected Beside EXE

Required:
- `Uroboros.MiniUploader.exe`
- `credentials.json`
- `token_store\`
- `config_\config.ini`
- `mini-uploader.json`

Generated during runtime:
- `logs\`
- `uploader-state.json`
- `media\`

## Config Simplification

`config_\config.ini` was intentionally reduced to only the keys used by the mini uploader:

```ini
[Production]
mdb_file_location="C:\Users\peera\OneDrive\Desktop\data.mdb"
timestamp=
```

Removed from mini config because not used here:
- lab settings
- token text fields from old config
- download-related keys
- pump limit settings

## Credential and Token Notes

Kept behavior:
- `credentials.json` remains a file beside the executable
- Google token storage remains beside the executable

Changed naming:
- old name: `token.json`
- new name: `token_store`

Important:
- `token_store` is a folder, not a JSON file
- the code uses Google `FileDataStore`

Reference:
- [GoogleDriveMini.cs](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/GoogleDriveMini.cs)

## Logging Behavior

Logs are written to:
- `logs\`

Current retention model:
- 7 rotating daily slots
- file naming pattern like `day1_20260609.log`
- on the 8th day, the matching slot is deleted and reused

Important:
- deletion is done with `File.Delete`
- deleted log files do not go to Recycle Bin

## Admin Behavior

The executable is configured to always request Administrator privileges through:
- [app.manifest](C:/Users/peera/OneDrive/Desktop/Uroboros_mini/app.manifest)

Manifest setting:
- `requestedExecutionLevel level="requireAdministrator"`

## Icon

The mini app uses a modified icon derived from the original Uroboros icon:
- green background
- `MINI` badge

Project config points to:
- `uroboros-mini.ico`

## Build Status

Last verified:
- `dotnet build C:\Users\peera\OneDrive\Desktop\Uroboros_mini\Uroboros.MiniUploader.csproj`

Result:
- build succeeded

Output:
- `C:\Users\peera\OneDrive\Desktop\Uroboros_mini\bin\Debug\net10.0`

## How To Use

### 1. Prepare files beside the executable

In the runtime folder, make sure these exist:
- `Uroboros.MiniUploader.exe`
- `credentials.json`
- `token_store\`
- `config_\config.ini`
- `mini-uploader.json`

Typical runtime folder now:
- `C:\Users\peera\OneDrive\Desktop\Uroboros_mini\bin\Debug\net10.0`

### 2. Configure source MDB

Edit `config_\config.ini` and set:

```ini
[Production]
mdb_file_location="C:\path\to\data.mdb"
timestamp=
```

Notes:
- `mdb_file_location` must point to the real source MDB file on the client machine
- `timestamp` can be left blank initially
- after a successful sync, the app updates `timestamp` automatically

### 3. Configure schedule

Edit `mini-uploader.json`

Important fields:
- `PollIntervalSeconds`
- `ScheduleWindowMinutes`
- `DailyTimes`

Examples:

Run continuously every poll cycle:

```json
"DailyTimes": []
```

Run only at specific times each day:

```json
"DailyTimes": ["06:00", "12:00", "18:00"]
```

Behavior:
- if `DailyTimes` is empty, the app checks every poll cycle
- if `DailyTimes` has values, the app runs only inside those time windows
- duplicate runs in the same slot are blocked by `uploader-state.json`

### 4. Start the app

Run:
- `Uroboros.MiniUploader.exe`

Behavior on start:
- Windows will ask for Administrator permission
- the app starts its loop automatically
- no BellBeast interaction is needed

### 5. What happens during operation

When a run is triggered:
1. read `config_\config.ini`
2. read `Production.mdb_file_location`
3. copy source MDB into `media\data.mdb`
4. compare/update `Production.timestamp`
5. convert MDB into SQLite snapshot
6. set active `chem` snapshot
7. upload to Google Drive as `chem.db`

If the source MDB has not changed:
- the app skips upload

If another instance is already open:
- the new instance exits

### 6. Check results

Look at:
- `logs\`
- `uploader-state.json`
- `media\`

Meaning:
- `logs\` shows runtime history
- `uploader-state.json` stores last processed slot and last run result
- `media\` stores synced MDB and generated chem snapshots

### 7. Stop the app

If running in a console:
- press `Ctrl+C`

If launched from Explorer:
- close the program window

### 8. Common problems

If Google auth fails:
- verify `credentials.json`
- verify `token_store\`

If MDB cannot be opened:
- verify `mdb_file_location`
- verify the MDB file exists
- verify ACE OLEDB is installed on the machine

If upload does not run:
- verify `DailyTimes`
- verify local machine time
- check `uploader-state.json` and log files

## TruckSidecar integration (2026-09-26)

The sidecar's chemical-unload records are now merged into `chem.db` before upload,
so the mothership (Uroboros backend → BellBeast) receives them alongside WDATA.

- **Trigger:** unchanged — the MDB file datetime. When the MDB changes, the convert
  runs, sidecar records are merged, and the whole `chem.db` is uploaded (full replace,
  matching how the backend swaps its DB). Sidecar-only edits made *after* the last MDB
  change (e.g. SG / temp / tank levels typed later) ride along on the next MDB change.
- **Source:** `mini-uploader.json` → `SidecarRecordsDir` = full path to the sidecar's
  `data\records` folder (same machine). Empty = disabled (merge skipped). Best-effort:
  any sidecar read/parse error is logged and swallowed — it never fails the core upload.
- **Scope:** every record is included, incomplete trips too, flagged by `IS_COMPLETE`.
- **New tables in `chem.db`:**
  - `SIDECAR_UNLOAD` — one row per trip. **PK** = `ID` (record GUID). **Soft FK**
    `TICKET1` → `WDATA.TICKET1` (soft because WDATA holds only completed `STAT='2'`
    trips, so in-progress records have no parent row yet). `NET_KG` = |W1−W2|.
  - `SIDECAR_FILL` — one row per tank filled. **Real, enforced FK** `UNLOAD_ID` →
    `SIDECAR_UNLOAD.ID` (`PRAGMA foreign_keys=ON`, `ON DELETE CASCADE`).
- **WDATA change:** the copy now also carries `TICKET1` + `TICKET2` (they exist in the
  MDB but weren't copied before) so the link resolves. Backward-compatible — the backend
  reads WDATA by named columns, not `SELECT *`.
- **Self-test (no MDB/dongle):** `Uroboros.MiniUploader.exe --sidecar-test <recordsDir> [outDb]`
  builds a fresh chem schema, merges the records, and prints counts + a preview.
  Verified 2026-09-26 against 12 real mock records (12 unloads / 12 fills, NET_KG and
  IS_COMPLETE correct, timestamps Gregorian/invariant, in-progress trip included).
- Files touched: `MdbReaderSafe.cs` (schema + WDATA cols + `EnsureChemSchema` + convert
  signature), new `SidecarMerge.cs` (DTOs + reader + merger + self-test), `Program.cs`
  (`SidecarRecordsDir` config + CLI hook), `mini-uploader.json`.
- **Before deploy:** set `SidecarRecordsDir` to the plant sidecar's `data\records` path.
- IoT room heartbeat is untouched (`IotEnabled` still gates it independently).

## Known Limitations

- The app is still a console app, not a Windows Service
- Admin UAC prompt will appear on every manual launch
- `Waiting` logs still exist unless reduced later
- publish packaging for production deployment has not been prepared yet

## Good Next Steps

Recommended next tasks:
1. Create a clean `publish` output for deployment
2. Reduce non-essential waiting logs if disk churn is still high
3. Set real `DailyTimes` in `mini-uploader.json`
4. Add Windows Task Scheduler entry for auto-start
5. Test on the actual client machine with real MDB and Google credentials

## Packaging / launch (2026-09-26)

- Consolidated under `D:\Sidecar_prj\` (dev: `C:\Users\peera\OneDrive\Desktop\Sidecar_prj\`) with TruckSidecar / TruckLauncher / TruckWatchdog. Deploy home: `D:\Sidecar_prj\Uroboros_mini`.
- `OutputType` is now **WinExe** (no cmd window when started by the launcher; `Console.OutputEncoding` is guarded in `Main` for the no-console case). Logs still go to `logs\`. `--sidecar-test` prints only when run via `dotnet <dll>` (console host), not via the WinExe apphost.
- Manifest **keeps `requireAdministrator`** (chosen), so a UAC prompt still appears on each launch — that part cannot be hidden.
- `config_\config.ini` -> `mdb_file_location="D:\TRUCK93A\data.mdb"` (same MDB the sidecar reads).
- Published self-contained x64 (folder, ~212 files, no .NET 10 runtime needed on target). **Copy `credentials.json` + `token_store\` beside the exe on deploy** (not in the project; already placed in `publish\`).
- **TruckLauncher starts all 4**: truck93a -> sidecar -> watchdog -> uroboros_mini (`uroborosExe`/`uroborosProcess` in `launcher.json`). uroboros is started last (its UAC prompt is modal) via `UseShellExecute=true` + hidden window; a cancelled UAC is swallowed.
