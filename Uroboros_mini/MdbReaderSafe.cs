#nullable enable
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Uroboros.MiniUploader;

internal static class MdbReaderSafe
{
    public const string MediaFolderName = "media";
    public const string MediaMdbName = "data.mdb";
    public const string MediaChemDbName = "chem.db";

    private const string SectionName = "Production";
    private const string IniKeySrcMdb = "mdb_file_location";
    private const string IniKeySrcTs = "timestamp";
    private const string ChemStampFileName = "chem.db.stamp";
    private const string ChemPointerFileName = "chem.current.txt";

    private static string MediaDir(string startupPath) => Path.Combine(startupPath, MediaFolderName);
    private static string MediaMdbPath(string startupPath) => Path.Combine(MediaDir(startupPath), MediaMdbName);
    private static string MediaChemDbPath(string startupPath) => Path.Combine(MediaDir(startupPath), MediaChemDbName);
    private static string MediaChemStampPath(string startupPath) => Path.Combine(MediaDir(startupPath), ChemStampFileName);
    private static string MediaChemPointerPath(string startupPath) => Path.Combine(MediaDir(startupPath), ChemPointerFileName);
    private static string MediaMdbHashPath(string startupPath) => Path.Combine(MediaDir(startupPath), MediaMdbName + ".sha256");

    public static string ResolveActiveChemDbPath(string startupPath)
    {
        var mediaDir = MediaDir(startupPath);
        Directory.CreateDirectory(mediaDir);

        var pointerPath = MediaChemPointerPath(startupPath);
        var fallback = MediaChemDbPath(startupPath);

        if (!File.Exists(pointerPath))
            return fallback;

        try
        {
            var fileName = (File.ReadAllText(pointerPath, Encoding.UTF8) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                return fallback;

            var full = Path.Combine(mediaDir, fileName);
            return File.Exists(full) ? full : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public enum MdbSyncStatus
    {
        Synced,
        UpToDate,
        ConfigNotFound,
        SourceNotFound,
        ErrorOccurred
    }

    public static MdbSyncStatus SyncMdbToMediaAtStartup(
        string startupPath,
        string configPathOrFileName,
        out string? message,
        bool configIsFileName = true)
    {
        var cfgPath = configIsFileName
            ? Path.Combine(startupPath, configPathOrFileName)
            : configPathOrFileName;

        return SyncMdbToMediaAtStartupCore(startupPath, cfgPath, out message);
    }

    private static MdbSyncStatus SyncMdbToMediaAtStartupCore(
        string startupPath,
        string configPath,
        out string? message)
    {
        message = null;

        try
        {
            if (!File.Exists(configPath))
            {
                message = $"config.ini not found ({configPath})";
                return MdbSyncStatus.ConfigNotFound;
            }

            var ini = new IniFile(configPath);
            var srcMdb = Unquote(ini.ReadString(SectionName, IniKeySrcMdb, string.Empty));

            if (string.IsNullOrWhiteSpace(srcMdb) || !File.Exists(srcMdb))
            {
                message = $"Source MDB not found ({srcMdb})";
                return MdbSyncStatus.SourceNotFound;
            }

            // ไม่เชื่อเวลาแก้ไขของไฟล์: ขณะโปรแกรมชั่งเปิด data.mdb ค้าง NTFS อาจไม่อัปเดต mtime
            // → คัดลอกทุกรอบ (ไฟล์เล็ก) แล้วเทียบ SHA-256 ของเนื้อไฟล์กับสำเนาใน media แทน
            var mediaDir = MediaDir(startupPath);
            Directory.CreateDirectory(mediaDir);

            var dstMdb = MediaMdbPath(startupPath);
            var tmpMdb = Path.Combine(mediaDir, $"data.copy.{Guid.NewGuid():N}.mdb");
            try
            {
                // FileShare.ReadWrite: อ่านได้แม้โปรแกรมชั่งเปิดไฟล์เขียนอยู่ (File.Copy อาจโดน sharing violation)
                using (var src = new FileStream(srcMdb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = File.Create(tmpMdb))
                    src.CopyTo(dst);

                var hash = HashFile(tmpMdb);
                if (File.Exists(dstMdb) && string.Equals(hash, MediaMdbHash(startupPath), StringComparison.Ordinal))
                {
                    message = $"MDB is up-to-date (hash={hash})";
                    return MdbSyncStatus.UpToDate;
                }

                File.Move(tmpMdb, dstMdb, overwrite: true);
                File.WriteAllText(MediaMdbHashPath(startupPath), hash, Encoding.UTF8);
            }
            finally
            {
                TryDelete(tmpMdb);
            }

            // ข้อมูลอ้างอิงเท่านั้น (ไม่ใช้ตัดสินว่าต้อง sync หรือไม่แล้ว)
            var srcStamp = ToStamp(File.GetLastWriteTimeUtc(srcMdb));
            ini.WriteString(SectionName, IniKeySrcTs, srcStamp);
            ini.Save();

            message = @"MDB synced to media\data.mdb (stamp=" + srcStamp + ", hash=" + MediaMdbHash(startupPath) + ")";
            return MdbSyncStatus.Synced;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return MdbSyncStatus.ErrorOccurred;
        }
    }

    /// <summary>SHA-256 ของ media\data.mdb (16 hex แรก) — ว่าง = ยังไม่มีสำเนาหรือสำเนาเสีย ต้อง sync ใหม่</summary>
    /// (ไม่คำนวณจากไฟล์เอง: ลบไฟล์ .sha256 = สั่งให้รอบหน้าคัดลอกใหม่)
    public static string MediaMdbHash(string startupPath) =>
        File.Exists(MediaMdbPath(startupPath))
            ? (SafeReadAllText(MediaMdbHashPath(startupPath)) ?? string.Empty).Trim()
            : string.Empty;

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs))[..16].ToLowerInvariant();
    }

    public static string Unquote(string? s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length >= 2 && t.StartsWith("\"", StringComparison.Ordinal) && t.EndsWith("\"", StringComparison.Ordinal))
            return t[1..^1];
        return t;
    }

    public static bool ConvertMediaMdbToChemDb(string startupPath, ILogger? log = null, CancellationToken ct = default, string? sidecarDb = null)
    {
        var mediaDir = MediaDir(startupPath);
        Directory.CreateDirectory(mediaDir);

        var mdbPath = MediaMdbPath(startupPath);
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException(@"media\data.mdb not found.", mdbPath);

        var stampPath = MediaChemStampPath(startupPath);
        // stamp = mdb + sidecar.db → record ที่บันทึกตอน data.mdb ไม่เปลี่ยนก็ยัง convert ใหม่
        sidecarDb = SidecarMerge.ResolvePath(sidecarDb, startupPath);
        // ใช้ hash เนื้อไฟล์ ไม่ใช้ mtime (File.Copy/สำเนาคง mtime ของต้นทาง ซึ่งอาจไม่ขยับขณะโปรแกรมชั่งเปิดไฟล์ค้าง)
        var mdbStamp = MediaMdbHash(startupPath) + SidecarMerge.Stamp(sidecarDb);

        if (File.Exists(stampPath))
        {
            var oldStamp = (SafeReadAllText(stampPath) ?? string.Empty).Trim();
            if (string.Equals(oldStamp, mdbStamp, StringComparison.Ordinal))
            {
                log?.Info($"[MDB] Convert skip (active chem snapshot already matches mdb stamp={mdbStamp})");
                return false;
            }
        }

        var tempChem = Path.Combine(mediaDir, $"chem.convert.{Guid.NewGuid():N}.db");
        log?.Info($"[MDB] Converting: {mdbPath} -> {tempChem} (stamp={mdbStamp})");

        try
        {
            ct.ThrowIfCancellationRequested();
            MdbToSqliteConverter.ConvertToChemSqlite(mdbPath, tempChem, log, sidecarDb);
            SqliteConnection.ClearAllPools();

            ActivateChemSnapshotFromFile(
                startupPath: startupPath,
                sourceDbFile: tempChem,
                snapshotStamp: mdbStamp,
                log: log,
                keep: 2);

            File.WriteAllText(stampPath, mdbStamp, Encoding.UTF8);
            log?.Info($"[MDB] Convert OK: active chem snapshot updated (stamp={mdbStamp})");
            return true;
        }
        catch
        {
            // สำเนาอาจถูกคัดลอกตอนโปรแกรมชั่งเขียนไม่เสร็จ → ลืม hash ให้รอบหน้าคัดลอกใหม่ (ไม่ค้างสำเนาเสียไปตลอด)
            TryDelete(MediaMdbHashPath(startupPath));
            throw;
        }
        finally
        {
            TryDelete(tempChem);
        }
    }

    public static string ActivateChemSnapshotFromFile(
        string startupPath,
        string sourceDbFile,
        string snapshotStamp,
        ILogger? log = null,
        int keep = 5)
    {
        if (!File.Exists(sourceDbFile))
            throw new FileNotFoundException("Source DB file missing.", sourceDbFile);

        var mediaDir = MediaDir(startupPath);
        Directory.CreateDirectory(mediaDir);

        var safeStamp = MakeSafeFileStamp(snapshotStamp);
        var snapshotFileName = $"chem_{safeStamp}.db";
        var snapshotFullPath = Path.Combine(mediaDir, snapshotFileName);

        if (!File.Exists(snapshotFullPath))
            File.Copy(sourceDbFile, snapshotFullPath, overwrite: true);

        ValidateSqlite(snapshotFullPath);
        SetActiveChemDbPath(startupPath, snapshotFileName);

        var compatChem = MediaChemDbPath(startupPath);
        if (!File.Exists(compatChem))
        {
            try { File.Copy(snapshotFullPath, compatChem, overwrite: true); } catch { }
        }

        CleanupOldChemSnapshots(startupPath, keep, log);
        log?.Info($"[CHEM] Active snapshot = {snapshotFullPath}");
        return snapshotFullPath;
    }

    private static void ValidateSqlite(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Cache=Shared;Pooling=False;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 1;";
        _ = cmd.ExecuteScalar();
    }

    private static void SetActiveChemDbPath(string startupPath, string dbFileNameOnly)
    {
        var pointer = MediaChemPointerPath(startupPath);
        var tmp = pointer + ".tmp";

        File.WriteAllText(tmp, dbFileNameOnly, new UTF8Encoding(true));

        if (File.Exists(pointer))
            File.Replace(tmp, pointer, null, ignoreMetadataErrors: true);
        else
            File.Move(tmp, pointer);
    }

    private static void CleanupOldChemSnapshots(string startupPath, int keep, ILogger? log)
    {
        try
        {
            var mediaDir = MediaDir(startupPath);
            var active = Path.GetFileName(ResolveActiveChemDbPath(startupPath));

            var files = Directory.GetFiles(mediaDir, "chem_*.db")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            var kept = 0;
            foreach (var fi in files)
            {
                if (string.Equals(fi.Name, active, StringComparison.OrdinalIgnoreCase))
                {
                    kept++;
                    continue;
                }

                if (kept < keep)
                {
                    kept++;
                    continue;
                }

                try { fi.Delete(); } catch { }
            }
        }
        catch (Exception ex)
        {
            log?.Warn($"[CHEM] Cleanup snapshot warning: {ex.Message}");
        }
    }

    private static string MakeSafeFileStamp(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var arr = s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray();
        return new string(arr)
            .Replace(":", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string? SafeReadAllText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
        catch { return null; }
    }

    private static string ToStamp(DateTime utc)
        => utc.ToUniversalTime().ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture);

    private sealed class IniFile
    {
        private readonly string _path;
        private readonly List<string> _lines;

        public IniFile(string path)
        {
            _path = path;
            _lines = File.ReadAllLines(_path, Encoding.UTF8).ToList();
        }

        public string ReadString(string section, string key, string defaultValue)
        {
            var secIdx = FindSectionIndex(section);
            if (secIdx < 0) return defaultValue;

            for (var i = secIdx + 1; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (IsSectionLine(line)) break;

                var kv = ParseKeyValue(line);
                if (kv.hasValue && string.Equals(kv.key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.value;
            }

            return defaultValue;
        }

        public void WriteString(string section, string key, string value)
        {
            var secIdx = FindSectionIndex(section);
            if (secIdx < 0)
            {
                _lines.Add(string.Empty);
                _lines.Add("[" + section + "]");
                _lines.Add(key + "=" + value);
                return;
            }

            for (var i = secIdx + 1; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (IsSectionLine(line)) break;

                var kv = ParseKeyValue(line);
                if (kv.hasValue && string.Equals(kv.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines[i] = key + "=" + value;
                    return;
                }
            }

            var insertAt = _lines.Count;
            for (var i = secIdx + 1; i < _lines.Count; i++)
            {
                if (IsSectionLine(_lines[i]))
                {
                    insertAt = i;
                    break;
                }
            }

            _lines.Insert(insertAt, key + "=" + value);
        }

        public void Save()
        {
            File.WriteAllLines(_path, _lines, Encoding.UTF8);
        }

        private int FindSectionIndex(string section)
        {
            var target = "[" + section.Trim() + "]";
            for (var i = 0; i < _lines.Count; i++)
            {
                if (string.Equals(_lines[i].Trim(), target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static bool IsSectionLine(string line)
        {
            var t = line.Trim();
            return t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal);
        }

        private static (bool hasValue, string key, string value) ParseKeyValue(string line)
        {
            var t = line.Trim();
            if (t.Length == 0) return (false, string.Empty, string.Empty);
            if (t.StartsWith(";", StringComparison.Ordinal)) return (false, string.Empty, string.Empty);

            var p = t.IndexOf('=');
            if (p <= 0) return (false, string.Empty, string.Empty);

            var k = t[..p].Trim();
            var v = t[(p + 1)..].Trim();
            return (true, k, v);
        }
    }
}

internal static class MdbToSqliteConverter
{
    private const string CreateSql = """
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS PRODUCT (
  CODE TEXT,
  NAME TEXT
);
CREATE INDEX IF NOT EXISTS IX_PRODUCT_CODE ON PRODUCT(CODE);
CREATE INDEX IF NOT EXISTS IX_PRODUCT_NAME ON PRODUCT(NAME);

CREATE TABLE IF NOT EXISTS COMPANY (
  CODE TEXT,
  NAME TEXT
);
CREATE INDEX IF NOT EXISTS IX_COMPANY_CODE ON COMPANY(CODE);
CREATE INDEX IF NOT EXISTS IX_COMPANY_NAME ON COMPANY(NAME);

CREATE TABLE IF NOT EXISTS CARTYPE (
  CODE TEXT,
  NAME TEXT
);
CREATE INDEX IF NOT EXISTS IX_CARTYPE_CODE ON CARTYPE(CODE);

CREATE TABLE IF NOT EXISTS WDATA (
  STAT    TEXT,
  TRUCK   TEXT,
  CARTYPE TEXT,
  COMPANY TEXT,
  PRODUCT TEXT,
  DAYIN   TEXT,
  TMIN    TEXT,
  W1      REAL,
  DAYOUT  TEXT,
  TMOUT   TEXT,
  W2      REAL,
  TICKET1 TEXT,
  TICKET2 TEXT
);
CREATE INDEX IF NOT EXISTS IX_WDATA_DAYIN    ON WDATA(DAYIN);
CREATE INDEX IF NOT EXISTS IX_WDATA_DAYOUT   ON WDATA(DAYOUT);
CREATE INDEX IF NOT EXISTS IX_WDATA_STAT     ON WDATA(STAT);
CREATE INDEX IF NOT EXISTS IX_WDATA_PRODUCT  ON WDATA(PRODUCT);
CREATE INDEX IF NOT EXISTS IX_WDATA_COMPANY  ON WDATA(COMPANY);
CREATE INDEX IF NOT EXISTS IX_WDATA_TICKET1  ON WDATA(TICKET1);

-- ── Sidecar chemical-unload records (TruckSidecar) ─────────────────────────
-- One row per unload trip. Links to WDATA via TICKET1 (soft FK: WDATA holds
-- only completed STAT='2' rows, so in-progress trips have no parent yet).
CREATE TABLE IF NOT EXISTS SIDECAR_UNLOAD (
  ID           TEXT PRIMARY KEY,
  TICKET1      TEXT,
  TICKET2      TEXT,
  PLATE        TEXT,
  CHEMICAL     TEXT,
  PRODUCT      TEXT,
  SOURCE       TEXT,
  WEIGH_IN_AT  TEXT,
  W1           REAL,
  WEIGH_OUT_AT TEXT,
  W2           REAL,
  NET_KG       REAL,
  SG           REAL,
  TEMP_C       REAL,
  BILL_BOOK    TEXT,
  BILL_NO      TEXT,
  IS_COMPLETE  INTEGER,
  VERSION      INTEGER,
  CREATED_AT   TEXT,
  CREATED_BY   TEXT,
  UPDATED_AT   TEXT,
  UPDATED_BY   TEXT,
  SYNCED       INTEGER
);
CREATE INDEX IF NOT EXISTS IX_SIDECAR_UNLOAD_TICKET1 ON SIDECAR_UNLOAD(TICKET1);

-- One row per tank filled on a trip. Real FK to SIDECAR_UNLOAD (enforced).
CREATE TABLE IF NOT EXISTS SIDECAR_FILL (
  UNLOAD_ID    TEXT NOT NULL,
  SEQ          INTEGER,
  TANK         TEXT,
  LEVEL_BEFORE REAL,
  LEVEL_AFTER  REAL,
  AFTER_AT     TEXT,
  FOREIGN KEY (UNLOAD_ID) REFERENCES SIDECAR_UNLOAD(ID) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_SIDECAR_FILL_UNLOAD ON SIDECAR_FILL(UNLOAD_ID);
""";

    /// <summary>Creates the chem.db schema (tables + indexes + PRAGMAs) on an open connection.</summary>
    public static void EnsureChemSchema(SqliteConnection sqlite)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = CreateSql;
        cmd.ExecuteNonQuery();
    }

    public static void ConvertToChemSqlite(string mdbPath, string sqlitePath, ILogger? log = null, string? sidecarDb = null)
    {
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("MDB not found.", mdbPath);

        var dir = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        // sidecar.db: snapshot ด้วย backup API แล้ว ATTACH (ต้องทำก่อนเปิด transaction)
        var sidecarSnap = SidecarMerge.Snapshot(sidecarDb, string.IsNullOrWhiteSpace(dir) ? "." : dir, log);
        try
        {
            ConvertToChemSqliteCore(mdbPath, sqlitePath, log, sidecarDb, sidecarSnap);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (sidecarSnap is not null) try { File.Delete(sidecarSnap); } catch { /* temp copy; next run cleans */ }
        }
    }

    private static void ConvertToChemSqliteCore(string mdbPath, string sqlitePath, ILogger? log, string? sidecarDb, string? sidecarSnap)
    {
        using var sqlite = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadWriteCreate;Cache=Shared;Pooling=False;");
        sqlite.Open();

        EnsureChemSchema(sqlite);
        var sidecarAttached = SidecarMerge.Attach(sqlite, sidecarSnap, log);

        using var tx = sqlite.BeginTransaction();
        ExecNonQuery(sqlite, "DELETE FROM SIDECAR_FILL;", tx);    // child first (FK)
        ExecNonQuery(sqlite, "DELETE FROM SIDECAR_UNLOAD;", tx);
        ExecNonQuery(sqlite, "DELETE FROM WDATA;", tx);
        ExecNonQuery(sqlite, "DELETE FROM PRODUCT;", tx);
        ExecNonQuery(sqlite, "DELETE FROM COMPANY;", tx);
        ExecNonQuery(sqlite, "DELETE FROM CARTYPE;", tx);

        CopyTableProduct(mdbPath, sqlite, tx, log);
        CopyTableCompany(mdbPath, sqlite, tx, log);
        CopyTableCarType(mdbPath, sqlite, tx, log);
        CopyTableWData(mdbPath, sqlite, tx, log);

        // Merge TruckSidecar chemical-unload records (best-effort; never fails the convert).
        if (sidecarAttached) SidecarMerge.MergeAttached(sqlite, tx, sidecarDb!, log);

        tx.Commit();
        if (sidecarAttached) SidecarMerge.Detach(sqlite);
    }

    private static void CopyTableProduct(string mdbPath, SqliteConnection sqlite, SqliteTransaction tx, ILogger? log)
    {
        const string sql = "SELECT CODE, NAME FROM PRODUCT";
        using var insert = sqlite.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO PRODUCT(CODE, NAME) VALUES ($c, $n);";
        var pc = AddP(insert, "$c");
        var pn = AddP(insert, "$n");

        StreamMdbRows(mdbPath, sql, log, rec =>
        {
            pc.Value = SafeText(rec, 0);
            pn.Value = SafeText(rec, 1);
            insert.ExecuteNonQuery();
        });
    }

    private static void CopyTableCompany(string mdbPath, SqliteConnection sqlite, SqliteTransaction tx, ILogger? log)
    {
        const string sql = "SELECT CODE, NAME FROM COMPANY";
        using var insert = sqlite.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO COMPANY(CODE, NAME) VALUES ($c, $n);";
        var pc = AddP(insert, "$c");
        var pn = AddP(insert, "$n");

        StreamMdbRows(mdbPath, sql, log, rec =>
        {
            pc.Value = SafeText(rec, 0);
            pn.Value = SafeText(rec, 1);
            insert.ExecuteNonQuery();
        });
    }

    private static void CopyTableCarType(string mdbPath, SqliteConnection sqlite, SqliteTransaction tx, ILogger? log)
    {
        const string sql = "SELECT CODE, NAME FROM CARTYPE";
        using var insert = sqlite.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO CARTYPE(CODE, NAME) VALUES ($c, $n);";
        var pc = AddP(insert, "$c");
        var pn = AddP(insert, "$n");

        StreamMdbRows(mdbPath, sql, log, rec =>
        {
            pc.Value = SafeText(rec, 0);
            pn.Value = SafeText(rec, 1);
            insert.ExecuteNonQuery();
        });
    }

    private static void CopyTableWData(string mdbPath, SqliteConnection sqlite, SqliteTransaction tx, ILogger? log)
    {
        const string sql =
            "SELECT STAT, TRUCK, CARTYPE, COMPANY, PRODUCT, DAYIN, TMIN, W1, DAYOUT, TMOUT, W2, TICKET1, TICKET2 " +
            "FROM WDATA WHERE STAT = '2'";

        using var insert = sqlite.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText =
            "INSERT INTO WDATA(STAT,TRUCK,CARTYPE,COMPANY,PRODUCT,DAYIN,TMIN,W1,DAYOUT,TMOUT,W2,TICKET1,TICKET2) " +
            "VALUES ($st,$tr,$ct,$co,$pr,$din,$tmin,$w1,$dout,$tmout,$w2,$t1,$t2);";

        var p0 = AddP(insert, "$st");
        var p1 = AddP(insert, "$tr");
        var p2 = AddP(insert, "$ct");
        var p3 = AddP(insert, "$co");
        var p4 = AddP(insert, "$pr");
        var p5 = AddP(insert, "$din");
        var p6 = AddP(insert, "$tmin");
        var p7 = AddP(insert, "$w1");
        var p8 = AddP(insert, "$dout");
        var p9 = AddP(insert, "$tmout");
        var p10 = AddP(insert, "$w2");
        var p11 = AddP(insert, "$t1");
        var p12 = AddP(insert, "$t2");

        StreamMdbRows(mdbPath, sql, log, rec =>
        {
            p0.Value = SafeText(rec, 0);
            p1.Value = SafeText(rec, 1);
            p2.Value = SafeText(rec, 2);
            p3.Value = SafeText(rec, 3);
            p4.Value = SafeText(rec, 4);
            p5.Value = SafeDateIso(rec, 5);
            p6.Value = SafeText(rec, 6);
            p7.Value = SafeReal(rec, 7);
            p8.Value = SafeDateIso(rec, 8);
            p9.Value = SafeText(rec, 9);
            p10.Value = SafeReal(rec, 10);
            p11.Value = SafeText(rec, 11);
            p12.Value = SafeText(rec, 12);
            insert.ExecuteNonQuery();
        });
    }

    private static SqliteParameter AddP(SqliteCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
        return p;
    }

    private static void ExecNonQuery(SqliteConnection conn, string sql, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string SafeText(IDataRecord rec, int i)
    {
        if (i < 0 || i >= rec.FieldCount) return string.Empty;
        if (rec.IsDBNull(i)) return string.Empty;
        return (rec.GetValue(i)?.ToString() ?? string.Empty).Trim();
    }

    private static double SafeReal(IDataRecord rec, int i)
    {
        if (i < 0 || i >= rec.FieldCount) return 0;
        if (rec.IsDBNull(i)) return 0;

        var v = rec.GetValue(i);
        if (v is double d) return d;
        if (v is float f) return f;
        if (v is decimal m) return (double)m;

        if (double.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) return x;
        if (double.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var y)) return y;
        return 0;
    }

    private static string SafeDateIso(IDataRecord rec, int i)
    {
        if (i < 0 || i >= rec.FieldCount) return string.Empty;
        if (rec.IsDBNull(i)) return string.Empty;

        try
        {
            var dt = Convert.ToDateTime(rec.GetValue(i), CultureInfo.InvariantCulture);
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void StreamMdbRows(
        string mdbPath,
        string sql,
        ILogger? log,
        Action<IDataRecord> rowHandler)
    {
        using var conn = OpenMdb(mdbPath, log);
        using var cmd = new OleDbCommand(sql, conn);
        using var r = cmd.ExecuteReader(CommandBehavior.SequentialAccess);
        if (r is null) return;

        while (r.Read())
            rowHandler(r);
    }

    private static IEnumerable<string> BuildMdbConnectionStrings(string mdbPath)
    {
        yield return $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={mdbPath};Persist Security Info=False;";
        yield return $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};Persist Security Info=False;";

        if (!Environment.Is64BitProcess)
            yield return $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={mdbPath};Persist Security Info=False;";
    }

    private static OleDbConnection OpenMdb(string mdbPath, ILogger? log = null)
    {
        log?.Info($"[MDB] Opening MDB '{mdbPath}' (process={(Environment.Is64BitProcess ? "x64" : "x86")})");

        Exception? lastEx = null;
        var errors = new List<string>();

        foreach (var cs in BuildMdbConnectionStrings(mdbPath))
        {
            var provider = ExtractProviderName(cs);
            log?.Info($"[MDB] Trying OLE DB provider '{provider}' for '{mdbPath}'");

            OleDbConnection? conn = null;
            try
            {
                conn = new OleDbConnection(cs);
                conn.Open();
                log?.Info($"[MDB] Using OLE DB provider '{provider}' for '{mdbPath}'");
                return conn;
            }
            catch (Exception ex)
            {
                conn?.Dispose();
                lastEx = ex;
                errors.Add($"{provider}: {ex.Message}");
                log?.Warn($"[MDB] Provider '{provider}' failed for '{mdbPath}': {ex.Message}");
            }
        }

        var message = BuildNoProviderMessage(mdbPath, errors);
        throw new InvalidOperationException(message, lastEx);
    }

    private static string ExtractProviderName(string connectionString)
    {
        const string key = "Provider=";
        var start = connectionString.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "(unknown)";

        start += key.Length;
        var end = connectionString.IndexOf(';', start);
        return end >= 0 ? connectionString[start..end] : connectionString[start..];
    }

    private static string BuildNoProviderMessage(string mdbPath, List<string> errors)
    {
        var providerList = "Microsoft.ACE.OLEDB.16.0, Microsoft.ACE.OLEDB.12.0";
        var detail = errors.Count == 0 ? "No provider attempts were recorded." : string.Join(" | ", errors);
        var jetNote = Environment.Is64BitProcess
            ? " Jet 4.0 was not attempted because this process is 64-bit."
            : " Jet 4.0 was attempted as an x86 fallback.";

        return
            $"Unable to open MDB '{mdbPath}'. Install the Microsoft Access Database Engine (ACE OLEDB) 64-bit provider on this server and retry. " +
            $"Providers attempted: {providerList}.{jetNote} Errors: {detail}";
    }
}
