using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TruckSidecar;

public sealed class FillEntry
{
    public string Tank { get; set; } = "";
    public double? LevelBefore { get; set; }
    public double? LevelAfter { get; set; }
    /// <summary>เวลาที่พนักงานบันทึกระดับหลังเติม (เก็บไว้ตรวจย้อนกลับ) — ฐานการคาดการณ์ใช้เวลาชั่งออกแทน ดู LatestAfterReadings</summary>
    public DateTime? AfterRecordedAt { get; set; }
}

public sealed class UnloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";

    // ผูกกับโปรแกรมเดิม: ทะเบียนซ้ำได้หลายรอบ จึงใช้คู่กับ Track ID ของ data.mdb
    public string Plate { get; set; } = "";
    public string TrackId { get; set; } = "";   // = WDATA.TICKET1

    // v2: สารเคมีของเที่ยวนี้ (จาก WDATA.PRODUCT ผ่าน productMap) ใช้เลือกแท็บ + กันเลือกถังผิดสาร
    public string Chemical { get; set; } = "";
    public string Product { get; set; } = "";
    /// <summary>"auto" = ตรวจจับจาก data.mdb อัตโนมัติ, "picker" = เลือกเองจากปุ่ม ดึงจากตาชั่ง</summary>
    public string Source { get; set; } = "";

    // คัดลอกจาก WDATA (อ่านอย่างเดียว) เพื่อให้ Central DB มีน้ำหนักคู่กับข้อมูลสารเคมี
    public string Ticket2 { get; set; } = "";
    public DateTime? WeighInAt { get; set; }
    public double? W1 { get; set; }
    public DateTime? WeighOutAt { get; set; }
    public double? W2 { get; set; }

    public void ApplyScale(ScaleRow s)
    {
        Plate = s.Truck;
        TrackId = s.Ticket1;
        WeighInAt = s.WeighInAt;
        W1 = s.W1;
        if (s.Product.Length > 0) Product = s.Product;
        if (s.WeighedOut)
        {
            Ticket2 = s.Ticket2;
            WeighOutAt = s.WeighOutAt;
            W2 = s.W2;
        }
    }

    public double? Sg { get; set; }
    public double? TempC { get; set; }
    public string BillBook { get; set; } = "";
    public string BillNo { get; set; } = "";
    public List<FillEntry> Fills { get; set; } = new();

    public bool Synced { get; set; }

    public const string KindReceive = "receive", KindWash = "wash";
    /// <summary>"receive" = รับสาร (W1 &gt; W2), "wash" = ล้างถัง/นำออก (W2 &gt; W1) — record เก่าไม่มีค่า = รับสาร</summary>
    public string Kind { get; set; } = KindReceive;
    [JsonIgnore] public bool IsWash => Kind == KindWash;

    /// <summary>สุทธิแบบมีเครื่องหมาย W1 − W2: บวก = รับเข้า, ลบ = นำออก (ล้างถัง)</summary>
    [JsonIgnore] public double? NetKg => W1 is { } a && W2 is { } b ? a - b : null;

    /// <summary>ชั่งเข้า-ออกครบ + เลือกถังแล้ว → นับยอดได้ (ยังไม่มีระดับถัง = ปันเท่ากัน/ประมาณการ)</summary>
    [JsonIgnore] public bool Countable => W1 != null && W2 != null && Ticket2.Length > 0 && Fills.Count > 0;

    [JsonIgnore]
    public List<string> Missing
    {
        get
        {
            var m = new List<string>();
            if (string.IsNullOrEmpty(Ticket2)) m.Add("ชั่งออก");
            if (!IsWash)
            {
                // ล้างถังไม่มีใบนำส่ง/ไม่วัด ถ.พ. — ต้องการแค่ถัง + ระดับก่อน/หลัง
                if (Sg == null) m.Add("ถ.พ.");
                if (TempC == null) m.Add("อุณหภูมิ");
                if (string.IsNullOrWhiteSpace(BillBook)) m.Add("ใบนำส่ง เล่มที่");
                if (string.IsNullOrWhiteSpace(BillNo)) m.Add("ใบนำส่ง เลขที่");
            }
            if (Fills.Count == 0) m.Add(IsWash ? "ถังที่ล้าง" : "ถังที่เติม");
            foreach (var f in Fills)
            {
                if (f.LevelBefore == null) m.Add($"ระดับก่อนเติม {f.Tank}");
                if (f.LevelAfter == null) m.Add($"ระดับหลังเติม {f.Tank}");
            }
            return m;
        }
    }

    [JsonIgnore] public bool IsComplete => Missing.Count == 0;

    // ── ขั้นตอนของเที่ยว (คำนวณจากข้อมูล ไม่เก็บลงไฟล์) ใช้วาดแถบสถานะในฟอร์ม ──
    [JsonIgnore] public bool StepWeighIn => TrackId.Length > 0;
    [JsonIgnore] public bool StepDocs => Fills.Count > 0 &&
        (IsWash || Sg != null && TempC != null && !string.IsNullOrWhiteSpace(BillBook) && !string.IsNullOrWhiteSpace(BillNo));
    [JsonIgnore] public bool StepLevels => Fills.Count > 0 && Fills.All(f => f.LevelBefore != null && f.LevelAfter != null);
    [JsonIgnore] public bool StepWeighOut => Ticket2.Length > 0;

    /// <summary>สารเคมีของเที่ยว: ใช้ Chemical ถ้ามี ไม่งั้นเดาจากถังที่เลือก (record จาก v1)</summary>
    public string ChemicalOr(IReadOnlyList<TankConfig> tanks) =>
        Chemical.Length > 0 ? Chemical
        : Fills.Select(f => tanks.FirstOrDefault(t => t.Name == f.Tank)?.Chemical).FirstOrDefault(c => c != null) ?? "";

    public UnloadRecord Clone() => JsonSerializer.Deserialize<UnloadRecord>(JsonSerializer.Serialize(this))!;
}

/// <summary>
/// เก็บ record ลง SQLite (data\sidecar.db) ตาราง SIDECAR_UNLOAD + SIDECAR_FILL (schema เดียวกับ chem.db
/// ให้ Uroboros_mini ATTACH แล้ว INSERT…SELECT ได้ตรงๆ) + SIDECAR_AUDIT ต่อท้ายทุกครั้งที่บันทึก (ไม่ลบ ไม่แก้ย้อนหลัง)
/// เปิดครั้งแรก: นำเข้า records\*.json ของเวอร์ชันเก่า แล้วย้ายโฟลเดอร์ไปเก็บเป็น records_migrated_* (ไม่ลบ)
/// </summary>
public sealed class RecordStore
{
    static readonly JsonSerializerOptions Line = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    const string Ts = "yyyy-MM-dd HH:mm:ss";   // รูปแบบเดียวกับที่ chem.db ใช้

    public const string Schema = """
PRAGMA foreign_keys = ON;
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
CREATE TABLE IF NOT EXISTS SIDECAR_AUDIT (
  SEQ      INTEGER PRIMARY KEY AUTOINCREMENT,
  AT       TEXT NOT NULL,
  USER     TEXT,
  MACHINE  TEXT,
  ACTION   TEXT NOT NULL,
  DETAIL   TEXT
);
""";

    readonly string _cs;
    readonly Dictionary<string, UnloadRecord> _records = new();

    public RecordStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "sidecar.db");
        _cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,      // ไม่ถือ handle ค้าง → Uroboros อ่าน/backup ได้ตลอด
            DefaultTimeout = 30,
        }.ToString();

        try
        {
            using var c = Open();
            Exec(c, Schema);
            EnsureColumns(c);
            LoadAll(c);
        }
        catch (SqliteException ex)
        {
            // db เสียต้องไม่ทำให้ sidecar ล้มตอนเปิด (watchdog จะเปิดใหม่วนไม่จบ)
            // → ย้ายไฟล์เสียไปเก็บไว้ให้ผู้ดูแลตรวจ แล้วเริ่ม db ใหม่
            var bad = $"{dbPath}.bad_{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
            SqliteConnection.ClearAllPools();
            if (!File.Exists(dbPath)) throw;
            File.Move(dbPath, bad);
            BadFiles.Add($"{Path.GetFileName(bad)} ({ex.Message})");
            _records.Clear();
            using var c = Open();
            Exec(c, Schema);
            EnsureColumns(c);
        }

        MigrateJson(Path.Combine(dataDir, "records"));
    }

    /// <summary>ไฟล์ที่อ่านไม่ได้ (db เสีย / json เก่าที่นำเข้าไม่ได้) — UI แจ้งเตือน</summary>
    public List<string> BadFiles { get; } = new();

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        Exec(c, "PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL;");
        return c;
    }

    /// <summary>
    /// คอลัมน์ที่เพิ่มหลัง schema แรก (ไม่อยู่ใน Schema เพื่อให้ DDL ยังตรงกับ chem.db ของ Uroboros —
    /// Uroboros merge ด้วยรายชื่อคอลัมน์ คอลัมน์เกินจึงไม่กระทบ)
    /// </summary>
    static void EnsureColumns(SqliteConnection c)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(SIDECAR_UNLOAD);";
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) cols.Add(rd.GetString(1));
        }
        if (!cols.Contains("KIND")) Exec(c, "ALTER TABLE SIDECAR_UNLOAD ADD COLUMN KIND TEXT;");
    }

    static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// นำเข้า records\*.json จากเวอร์ชันเก่า (เฉพาะที่ยังไม่มี หรือ Version ใหม่กว่า) แล้วย้ายทั้งโฟลเดอร์ไป
    /// records_migrated_yyyyMMddHHmmss เก็บเป็นสำรอง — ไม่ลบ json ทิ้ง
    /// </summary>
    void MigrateJson(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var files = Directory.EnumerateFiles(dir, "*.json").ToList();
        int imported = 0;
        foreach (var f in files)
        {
            try
            {
                var r = JsonSerializer.Deserialize<UnloadRecord>(File.ReadAllText(f));
                if (r == null || string.IsNullOrWhiteSpace(r.Id)) continue;
                if (_records.TryGetValue(r.Id, out var cur) && cur.Version >= r.Version) continue;
                WriteRecord(r);
                _records[r.Id] = r;
                imported++;
            }
            catch (Exception ex)
            {
                BadFiles.Add($"{Path.GetFileName(f)} ({ex.Message})");
            }
        }
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        try { Directory.Move(dir, $"{dir}_migrated_{stamp}"); }
        catch (Exception ex) { BadFiles.Add($"ย้าย {dir} ไม่ได้: {ex.Message}"); return; }
        Audit("migrate-json", new { from = dir, files = files.Count, imported, backup = $"{Path.GetFileName(dir)}_migrated_{stamp}" });
    }

    void LoadAll(SqliteConnection c)
    {
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT ID,TICKET1,TICKET2,PLATE,CHEMICAL,PRODUCT,SOURCE,WEIGH_IN_AT,W1,WEIGH_OUT_AT,W2,
                                       SG,TEMP_C,BILL_BOOK,BILL_NO,VERSION,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY,SYNCED,KIND
                                FROM SIDECAR_UNLOAD;";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var r = new UnloadRecord
                {
                    Id = rd.GetString(0), TrackId = S(rd, 1), Ticket2 = S(rd, 2), Plate = S(rd, 3),
                    Chemical = S(rd, 4), Product = S(rd, 5), Source = S(rd, 6),
                    WeighInAt = D(rd, 7), W1 = N(rd, 8), WeighOutAt = D(rd, 9), W2 = N(rd, 10),
                    Sg = N(rd, 11), TempC = N(rd, 12), BillBook = S(rd, 13), BillNo = S(rd, 14),
                    Version = rd.IsDBNull(15) ? 0 : rd.GetInt32(15),
                    CreatedAt = D(rd, 16) ?? default, CreatedBy = S(rd, 17),
                    UpdatedAt = D(rd, 18) ?? default, UpdatedBy = S(rd, 19),
                    Synced = !rd.IsDBNull(20) && rd.GetInt32(20) != 0,
                    Kind = S(rd, 21) == UnloadRecord.KindWash ? UnloadRecord.KindWash : UnloadRecord.KindReceive,
                };
                _records[r.Id] = r;
            }
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT UNLOAD_ID,TANK,LEVEL_BEFORE,LEVEL_AFTER,AFTER_AT FROM SIDECAR_FILL ORDER BY UNLOAD_ID, SEQ;";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                if (_records.TryGetValue(rd.GetString(0), out var r))
                    r.Fills.Add(new FillEntry { Tank = S(rd, 1), LevelBefore = N(rd, 2), LevelAfter = N(rd, 3), AfterRecordedAt = D(rd, 4) });
        }

        static string S(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        static double? N(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
        static DateTime? D(SqliteDataReader r, int i) =>
            !r.IsDBNull(i) && DateTime.TryParseExact(r.GetString(i), Ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)
                ? d : null;
    }

    public IEnumerable<UnloadRecord> All => _records.Values;
    public bool HasIncomplete => _records.Values.Any(r => !r.IsComplete);

    public UnloadRecord? FindByTrackId(string trackId) =>
        _records.Values.FirstOrDefault(r => r.TrackId.Equals(trackId.Trim(), StringComparison.OrdinalIgnoreCase));

    public UnloadRecord? FindByTicket2(string ticket2) =>
        ticket2.Trim().Length == 0 ? null
        : _records.Values.FirstOrDefault(r => r.Ticket2.Equals(ticket2.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ระดับหลังเติมล่าสุดที่พนักงานบันทึก แยกตามถัง
    /// เวลาของค่า = เวลาชั่งออก (เติมเสร็จจริง) ไม่ใช่เวลาที่กดบันทึก — พนักงานอาจกรอกย้อนหลังหลายชั่วโมง
    /// ถ้าใช้เวลาบันทึก ค่าเก่าจะดูใหม่กว่า API รอบหลังเติมและทับค่าจริง (Predictor เลือกค่าที่ใหม่กว่า)
    /// ยังไม่ชั่งออก → ใช้เวลาบันทึกไปก่อน
    /// </summary>
    public Dictionary<string, (DateTime At, double Level)> LatestAfterReadings()
    {
        var d = new Dictionary<string, (DateTime, double)>();
        foreach (var r in _records.Values)
            foreach (var f in r.Fills)
            {
                if (f.LevelAfter is not { } lvl || (r.WeighOutAt ?? f.AfterRecordedAt) is not { } at) continue;
                if (!d.TryGetValue(f.Tank, out var cur) || at > cur.Item1) d[f.Tank] = (at, lvl);
            }
        return d;
    }

    /// <param name="action">"save" = พนักงานบันทึก, "scale" = ระบบดึงผลชั่งออกจาก data.mdb</param>
    public void Save(UnloadRecord r, string action = "save")
    {
        var now = DateTime.Now;
        var who = $@"{Environment.UserDomainName}\{Environment.UserName}";
        if (r.Version == 0) { r.CreatedAt = now; r.CreatedBy = who; }
        r.Version++;
        r.UpdatedAt = now;
        r.UpdatedBy = who;
        r.Synced = false;

        // record + audit อยู่ใน transaction เดียวกัน: ไฟดับกลางทางจะไม่มีครึ่งๆ กลางๆ
        using var c = Open();
        using var tx = c.BeginTransaction();
        WriteRecord(c, tx, r);
        InsertAudit(c, tx, now, who, action, new { complete = r.IsComplete, record = r });
        tx.Commit();
        _records[r.Id] = r;
    }

    /// <summary>บันทึกเหตุการณ์ที่ไม่ใช่การแก้ record (เช่น crosscheck, dismiss) ลง SIDECAR_AUDIT</summary>
    public void Audit(string action, object detail)
    {
        var who = $@"{Environment.UserDomainName}\{Environment.UserName}";
        using var c = Open();
        InsertAudit(c, null, DateTime.Now, who, action, detail);
    }

    static void InsertAudit(SqliteConnection c, SqliteTransaction? tx, DateTime at, string who, string action, object detail)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO SIDECAR_AUDIT (AT,USER,MACHINE,ACTION,DETAIL) VALUES ($at,$u,$m,$a,$d);";
        cmd.Parameters.AddWithValue("$at", at.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$u", who);
        cmd.Parameters.AddWithValue("$m", Environment.MachineName);
        cmd.Parameters.AddWithValue("$a", action);
        cmd.Parameters.AddWithValue("$d", JsonSerializer.Serialize(detail, Line));
        cmd.ExecuteNonQuery();
    }

    /// <summary>ส่ง record ที่ยังไม่ sync ไป Central DB ถ้าไม่ได้ตั้งค่า URL จะไม่ทำอะไร</summary>
    public async Task<int> SyncAsync(HttpClient http, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0;
        int sent = 0;
        foreach (var r in _records.Values.Where(r => !r.Synced).OrderBy(r => r.UpdatedAt).ToList())
        {
            var res = await http.PostAsJsonAsync(url, r, Line);
            if (!res.IsSuccessStatusCode) break;
            r.Synced = true;
            WriteRecord(r);
            sent++;
        }
        return sent;
    }

    public int UnsyncedCount => _records.Values.Count(r => !r.Synced);

    void WriteRecord(UnloadRecord r)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        WriteRecord(c, tx, r);
        tx.Commit();
    }

    /// <summary>upsert แถว SIDECAR_UNLOAD แล้วเขียน SIDECAR_FILL ของ record นี้ใหม่ทั้งชุด</summary>
    static void WriteRecord(SqliteConnection c, SqliteTransaction tx, UnloadRecord r)
    {
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO SIDECAR_UNLOAD
  (ID,TICKET1,TICKET2,PLATE,CHEMICAL,PRODUCT,SOURCE,WEIGH_IN_AT,W1,WEIGH_OUT_AT,W2,NET_KG,
   SG,TEMP_C,BILL_BOOK,BILL_NO,IS_COMPLETE,VERSION,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY,SYNCED,KIND)
VALUES
  ($id,$t1,$t2,$plate,$chem,$prod,$src,$win,$w1,$wout,$w2,$net,
   $sg,$temp,$bbook,$bno,$complete,$ver,$cat,$cby,$uat,$uby,$sync,$kind)
ON CONFLICT(ID) DO UPDATE SET
  TICKET1=excluded.TICKET1, TICKET2=excluded.TICKET2, PLATE=excluded.PLATE, CHEMICAL=excluded.CHEMICAL,
  PRODUCT=excluded.PRODUCT, SOURCE=excluded.SOURCE, WEIGH_IN_AT=excluded.WEIGH_IN_AT, W1=excluded.W1,
  WEIGH_OUT_AT=excluded.WEIGH_OUT_AT, W2=excluded.W2, NET_KG=excluded.NET_KG, SG=excluded.SG,
  TEMP_C=excluded.TEMP_C, BILL_BOOK=excluded.BILL_BOOK, BILL_NO=excluded.BILL_NO,
  IS_COMPLETE=excluded.IS_COMPLETE, VERSION=excluded.VERSION, CREATED_AT=excluded.CREATED_AT,
  CREATED_BY=excluded.CREATED_BY, UPDATED_AT=excluded.UPDATED_AT, UPDATED_BY=excluded.UPDATED_BY,
  SYNCED=excluded.SYNCED, KIND=excluded.KIND;";
            var p = cmd.Parameters;
            p.AddWithValue("$id", r.Id);
            p.AddWithValue("$t1", Txt(r.TrackId));
            p.AddWithValue("$t2", Txt(r.Ticket2));
            p.AddWithValue("$plate", Txt(r.Plate));
            p.AddWithValue("$chem", Txt(r.Chemical));
            p.AddWithValue("$prod", Txt(r.Product));
            p.AddWithValue("$src", Txt(r.Source));
            p.AddWithValue("$win", Dt(r.WeighInAt));
            p.AddWithValue("$w1", Num(r.W1));
            p.AddWithValue("$wout", Dt(r.WeighOutAt));
            p.AddWithValue("$w2", Num(r.W2));
            p.AddWithValue("$net", Num(r.NetKg));     // มีเครื่องหมาย: ลบ = นำออก (ล้างถัง)
            p.AddWithValue("$sg", Num(r.Sg));
            p.AddWithValue("$temp", Num(r.TempC));
            p.AddWithValue("$bbook", Txt(r.BillBook));
            p.AddWithValue("$bno", Txt(r.BillNo));
            p.AddWithValue("$complete", r.IsComplete ? 1 : 0);
            p.AddWithValue("$ver", r.Version);
            p.AddWithValue("$cat", Dt(r.CreatedAt));
            p.AddWithValue("$cby", Txt(r.CreatedBy));
            p.AddWithValue("$uat", Dt(r.UpdatedAt));
            p.AddWithValue("$uby", Txt(r.UpdatedBy));
            p.AddWithValue("$sync", r.Synced ? 1 : 0);
            p.AddWithValue("$kind", r.Kind);
            cmd.ExecuteNonQuery();
        }
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM SIDECAR_FILL WHERE UNLOAD_ID = $u;";
            del.Parameters.AddWithValue("$u", r.Id);
            del.ExecuteNonQuery();
        }
        int seq = 0;
        foreach (var f in r.Fills)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO SIDECAR_FILL (UNLOAD_ID,SEQ,TANK,LEVEL_BEFORE,LEVEL_AFTER,AFTER_AT) VALUES ($u,$s,$t,$b,$a,$at);";
            cmd.Parameters.AddWithValue("$u", r.Id);
            cmd.Parameters.AddWithValue("$s", ++seq);
            cmd.Parameters.AddWithValue("$t", Txt(f.Tank));
            cmd.Parameters.AddWithValue("$b", Num(f.LevelBefore));
            cmd.Parameters.AddWithValue("$a", Num(f.LevelAfter));
            cmd.Parameters.AddWithValue("$at", Dt(f.AfterRecordedAt));
            cmd.ExecuteNonQuery();
        }

        static object Txt(string? s) => string.IsNullOrEmpty(s) ? DBNull.Value : s;
        static object Num(double? d) => d is { } v ? v : DBNull.Value;
        static object Dt(DateTime? d) => d is { } v && v != default ? v.ToString(Ts, CultureInfo.InvariantCulture) : DBNull.Value;
    }
}

/// <summary>
/// TICKET1 ที่พนักงานกด "ไม่ใช่รถสารเคมี" — เก็บลงไฟล์เพื่อไม่ให้เด้งกลับมาหลังเปิดโปรแกรมใหม่
/// </summary>
public sealed class DismissedSet
{
    readonly string _path;
    readonly HashSet<string> _set;

    public DismissedSet(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "dismissed.json");
        try { _set = File.Exists(_path) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch { _set = new(); }
    }

    public bool Contains(string ticket1) => _set.Contains(ticket1);

    public void Add(string ticket1)
    {
        if (!_set.Add(ticket1)) return;
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_set));
        File.Move(tmp, _path, overwrite: true);
    }
}
