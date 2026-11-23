using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TruckSidecar;

public sealed class OutboxRow
{
    public long RowId { get; init; }
    public string Date { get; init; } = "";   // yyyy-MM-dd
    public string Time { get; init; } = "";    // HH:mm
    public int ConfigId { get; init; }
    public double Value { get; init; }         // ตัน
    public string TransType { get; init; } = "";
    public string FlagValue { get; init; } = "0";
}

/// <summary>
/// Outbox แยกของ sidecar (SQLite) เก็บปริมาณเข้า Aquadat แบบ interpolate ต่อ configId
/// unique key = (date, time, config_id, source_track) กันซ้ำเมื่อบันทึกแก้ไขหลายรอบ
/// การส่งจริงทำโดย token ภายนอก — คลาสนี้ไม่ยุ่งกับการยืนยันตัวตน/รหัสผ่าน
/// </summary>
public sealed class AquadatOutbox
{
    readonly string _cs;

    public AquadatOutbox(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _cs = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDir, "aquadat_outbox.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        using var c = Open();
        Exec(c, @"
CREATE TABLE IF NOT EXISTS aq_outbox (
  rowid        INTEGER PRIMARY KEY AUTOINCREMENT,
  date         TEXT NOT NULL,
  time         TEXT NOT NULL,
  config_id    INTEGER NOT NULL,
  value        REAL NOT NULL,           -- ตัน (interpolate แล้ว)
  trans_type   TEXT NOT NULL DEFAULT '',
  flag_value   TEXT NOT NULL DEFAULT '0',
  source_track TEXT NOT NULL DEFAULT '',-- WDATA.TICKET1 ของเที่ยวที่มา
  detail_json  TEXT NOT NULL DEFAULT '',-- การปันส่วนรายถัง (ตรวจย้อนกลับ)
  synced       INTEGER NOT NULL DEFAULT 0,
  created_at   TEXT NOT NULL,
  sent_at      TEXT,
  response     TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox ON aq_outbox(date, time, config_id, source_track);
-- ตัดยอดรายวัน: ค่าที่ผู้ใช้แก้เอง (ต้องมีหมายเหตุ) มีผลเหนือยอดคำนวณ
CREATE TABLE IF NOT EXISTS aq_override (
  date      TEXT NOT NULL,
  config_id INTEGER NOT NULL,
  value     REAL NOT NULL,
  note      TEXT NOT NULL,
  by        TEXT,
  at        TEXT NOT NULL,
  PRIMARY KEY (date, config_id)
);
-- ตัดยอดรายวัน: ยืนยันแล้วส่ง (sig = ยอดที่ส่งทุก configID ณ ตอนยืนยัน — ยอดเปลี่ยนภายหลัง = ต้องยืนยันใหม่)
CREATE TABLE IF NOT EXISTS aq_confirm (
  date TEXT PRIMARY KEY,
  sig  TEXT NOT NULL,
  by   TEXT,
  at   TEXT NOT NULL
);");
    }

    static string Who => $@"{Environment.UserDomainName}\{Environment.UserName}";

    /// <summary>รายเที่ยวของวัน (ทะเบียน + การปันส่วนจาก detail_json) — ใช้แสดงรายการคำนวณตอนตัดยอด</summary>
    public List<(string Track, string Plate, bool Synced, List<TankAllocation> Allocations)> DayTrips(string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT source_track, MAX(detail_json), MIN(synced) FROM aq_outbox WHERE date=$d
GROUP BY source_track HAVING SUM(ABS(value)) > 0 ORDER BY source_track;";
        cmd.Parameters.AddWithValue("$d", date);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, bool, List<TankAllocation>)>();
        while (r.Read())
        {
            string plate = "";
            try { using var doc = JsonDocument.Parse(r.GetString(1)); if (doc.RootElement.TryGetProperty("Plate", out var p)) plate = p.GetString() ?? ""; }
            catch (JsonException) { }
            list.Add((r.GetString(0), plate, r.GetInt64(2) == 1, ParseAllocations(r.GetString(1))));
        }
        return list;
    }

    /// <summary>ยอดดิบ (มีเครื่องหมาย) ต่อ configID ของวัน + มีแถวค้างส่งไหม + เคยส่งแล้วบ้างไหม</summary>
    public (Dictionary<int, double> Raw, bool HasUnsynced, bool HasSynced) DayRaw(string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT config_id, SUM(value), MIN(synced), MAX(synced) FROM aq_outbox WHERE date=$d GROUP BY config_id;";
        cmd.Parameters.AddWithValue("$d", date);
        using var r = cmd.ExecuteReader();
        var raw = new Dictionary<int, double>();
        bool unsynced = false, synced = false;
        while (r.Read())
        {
            raw[r.GetInt32(0)] = Math.Round(r.GetDouble(1), 3);
            unsynced |= r.GetInt64(2) == 0;
            synced |= r.GetInt64(3) == 1;
        }
        return (raw, unsynced, synced);
    }

    /// <summary>วันที่มีข้อมูลตัดยอด (outbox / override / confirm) ภายใน days วันย้อนหลัง เรียงใหม่ → เก่า</summary>
    public List<string> ActiveDates(int days)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT date FROM aq_outbox WHERE date >= date('now','+7 hours',$d)
UNION SELECT date FROM aq_override WHERE date >= date('now','+7 hours',$d)
UNION SELECT date FROM aq_confirm WHERE date >= date('now','+7 hours',$d)
ORDER BY date DESC;";
        cmd.Parameters.AddWithValue("$d", $"-{Math.Max(0, days)} days");
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public Dictionary<int, (double Value, string Note, string By, string At)> Overrides(string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT config_id, value, note, IFNULL(by,''), at FROM aq_override WHERE date=$d;";
        cmd.Parameters.AddWithValue("$d", date);
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<int, (double, string, string, string)>();
        while (r.Read()) d[r.GetInt32(0)] = (r.GetDouble(1), r.GetString(2), r.GetString(3), r.GetString(4));
        return d;
    }

    /// <summary>แก้ยอดเอง (ค่า ≥ 0 + หมายเหตุ) → วันนั้นกลับเป็นรอยืนยัน</summary>
    public void SetOverride(string date, int configId, double value, string note)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "ยอดต้องไม่ติดลบ");
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("ต้องกรอกหมายเหตุ", nameof(note));
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO aq_override (date, config_id, value, note, by, at) VALUES ($d,$c,$v,$n,$b,datetime('now','+7 hours'))
ON CONFLICT(date, config_id) DO UPDATE SET value=excluded.value, note=excluded.note, by=excluded.by, at=excluded.at;
UPDATE aq_outbox SET synced=0 WHERE date=$d;";
        cmd.Parameters.AddWithValue("$d", date);
        cmd.Parameters.AddWithValue("$c", configId);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$n", note.Trim());
        cmd.Parameters.AddWithValue("$b", Who);
        cmd.ExecuteNonQuery();
    }

    /// <summary>กลับไปใช้ยอดคำนวณ → วันนั้นกลับเป็นรอยืนยัน</summary>
    public void ClearOverride(string date, int configId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM aq_override WHERE date=$d AND config_id=$c; UPDATE aq_outbox SET synced=0 WHERE date=$d;";
        cmd.Parameters.AddWithValue("$d", date);
        cmd.Parameters.AddWithValue("$c", configId);
        cmd.ExecuteNonQuery();
    }

    public (string Sig, string By, string At)? Confirmation(string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sig, IFNULL(by,''), at FROM aq_confirm WHERE date=$d;";
        cmd.Parameters.AddWithValue("$d", date);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1), r.GetString(2)) : null;
    }

    /// <summary>ยืนยันตัดยอดแล้วส่งสำเร็จ: ทุกแถวของวันเป็นส่งแล้ว + จำ sig ของยอดที่ส่ง</summary>
    public void SaveConfirm(string date, string sig, string response)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE aq_outbox SET synced=1, sent_at=datetime('now','+7 hours'), response=$r WHERE date=$d AND synced=0;
INSERT INTO aq_confirm (date, sig, by, at) VALUES ($d,$s,$b,datetime('now','+7 hours'))
ON CONFLICT(date) DO UPDATE SET sig=excluded.sig, by=excluded.by, at=excluded.at;";
            cmd.Parameters.AddWithValue("$d", date);
            cmd.Parameters.AddWithValue("$s", sig);
            cmd.Parameters.AddWithValue("$b", Who);
            cmd.Parameters.AddWithValue("$r", response);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }
    static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    /// <summary>
    /// บันทึก/แทนที่รายการของเที่ยวนี้: ลบของเดิม (source_track เดียวกัน) ที่ยังไม่ส่ง แล้วใส่ค่าใหม่
    /// เพื่อให้การแก้ไขระดับถังก่อนส่ง สะท้อนค่าใหม่ ไม่ทับซ้อน
    /// </summary>
    public void Upsert(string sourceTrack, string date, string time,
        IEnumerable<(int ConfigId, double Value)> byConfig, string detailJson, string transType = "")
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM aq_outbox WHERE source_track=$t AND synced=0;";
            del.Parameters.AddWithValue("$t", sourceTrack);
            del.ExecuteNonQuery();
        }
        var list = byConfig.ToList();
        // แก้ไขหลังส่งแล้วจนถังเปลี่ยนกลุ่ม (เช่น A → C): configID เดิมที่ส่งไปแล้วต้องถูกหักออกจากยอดวันนั้น
        // → ตั้งเป็น 0 และค้างส่ง ให้ PendingDaily ส่งยอดรวมใหม่ไปทับใน Aquadat
        using (var zero = c.CreateCommand())
        {
            zero.Transaction = tx;
            var keep = list.Count == 0 ? "" : $" AND config_id NOT IN ({string.Join(",", list.Select(x => x.ConfigId))})";
            zero.CommandText = "UPDATE aq_outbox SET value=0, detail_json=$dj, synced=0, sent_at=NULL, response=NULL " +
                               "WHERE source_track=$t AND synced=1" + keep + ";";
            zero.Parameters.AddWithValue("$t", sourceTrack);
            zero.Parameters.AddWithValue("$dj", detailJson);
            zero.ExecuteNonQuery();
        }
        foreach (var (cfg, val) in list)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO aq_outbox (date, time, config_id, value, trans_type, flag_value, source_track, detail_json, synced, created_at)
VALUES ($d, $tm, $cfg, $v, $tt, '0', $trk, $dj, 0, datetime('now','+7 hours'))
ON CONFLICT(date, time, config_id, source_track)
DO UPDATE SET value=excluded.value, detail_json=excluded.detail_json, synced=0, sent_at=NULL, response=NULL;";
            ins.Parameters.AddWithValue("$d", date);
            ins.Parameters.AddWithValue("$tm", time);
            ins.Parameters.AddWithValue("$cfg", cfg);
            ins.Parameters.AddWithValue("$v", val);
            ins.Parameters.AddWithValue("$tt", transType);
            ins.Parameters.AddWithValue("$trk", sourceTrack);
            ins.Parameters.AddWithValue("$dj", detailJson);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// ยอด "ตัดรายวัน" ที่ต้องส่ง: สำหรับทุก (date, config_id) ที่ยังมีเที่ยวค้างส่ง
    /// value = ผลรวมทุกเที่ยวของวันนั้น (สะสมทั้งวัน) และ time = "24:00" เสมอ
    /// การ upsert ฝั่ง Aquadat จะทับด้วยยอดรวมล่าสุด
    /// </summary>
    public List<OutboxRow> PendingDaily()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT date, config_id, ROUND(SUM(value), 3) AS total
FROM aq_outbox
GROUP BY date, config_id
HAVING SUM(CASE WHEN synced = 0 THEN 1 ELSE 0 END) > 0
ORDER BY date, config_id;";
        using var r = cmd.ExecuteReader();
        var list = new List<OutboxRow>();
        while (r.Read())
            list.Add(new OutboxRow
            {
                Date = r.GetString(0), ConfigId = r.GetInt32(1),
                Value = r.GetDouble(2), Time = "24:00", TransType = "", FlagValue = "0",
            });
        return list;
    }

    /// <summary>ยอดรายวันย้อนหลัง (ทั้งที่ส่งแล้วและค้างส่ง) สำหรับแสดงในแท็บ "ส่งยอดสารเคมี"</summary>
    public List<(string Date, int ConfigId, double Total, int Trips, bool Synced, string? SentAt)> DailySummary(int days)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT date, config_id, ROUND(SUM(value), 3), COUNT(DISTINCT CASE WHEN value > 0 THEN source_track END),
       MIN(synced), MAX(sent_at)
FROM aq_outbox
WHERE date >= date('now','+7 hours', $d)
GROUP BY date, config_id
ORDER BY date DESC, config_id;";
        cmd.Parameters.AddWithValue("$d", $"-{Math.Max(0, days)} days");
        using var r = cmd.ExecuteReader();
        var list = new List<(string, int, double, int, bool, string?)>();
        while (r.Read())
            list.Add((r.GetString(0), r.GetInt32(1), r.GetDouble(2), r.GetInt32(3), r.GetInt64(4) == 1,
                      r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    /// <summary>ทำเครื่องหมายส่งแล้ว ทุกเที่ยวของ (date, config_id) ที่ส่งยอดรวมไป</summary>
    public void MarkDaysSynced(IEnumerable<(string Date, int ConfigId)> days, string response)
    {
        var list = days.ToList();
        if (list.Count == 0) return;
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var (date, cfg) in list)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE aq_outbox SET synced=1, sent_at=datetime('now','+7 hours'), response=$r WHERE date=$d AND config_id=$c AND synced=0;";
            cmd.Parameters.AddWithValue("$r", response);
            cmd.Parameters.AddWithValue("$d", date);
            cmd.Parameters.AddWithValue("$c", cfg);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// รายเที่ยวย้อนหลัง (1 แถวต่อรถ 1 คันต่อวัน) พร้อมการปันส่วนรายถังจาก detail_json
    /// ใช้สรุป "มากี่คัน / ลงถังไหนเท่าไหร่ / ตัน + m³" — รถ 1 คันที่ลงหลาย configID ยังนับเป็น 1 คัน
    /// </summary>
    public List<(string Date, string Track, bool Synced, List<TankAllocation> Allocations)> DailyTrips(int days)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT date, source_track, MAX(detail_json), MIN(synced)
FROM aq_outbox
WHERE date >= date('now','+7 hours', $d)
GROUP BY date, source_track
HAVING SUM(ABS(value)) > 0
ORDER BY date DESC, source_track;";
        cmd.Parameters.AddWithValue("$d", $"-{Math.Max(0, days)} days");
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, bool, List<TankAllocation>)>();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetInt64(3) == 1, ParseAllocations(r.GetString(2))));
        return list;
    }

    static readonly JsonSerializerOptions DetailJson = new() { PropertyNameCaseInsensitive = true };

    static List<TankAllocation> ParseAllocations(string detailJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (doc.RootElement.TryGetProperty("allocations", out var a) && a.ValueKind == JsonValueKind.Array)
                return a.Deserialize<List<TankAllocation>>(DetailJson) ?? new();
        }
        catch (JsonException) { }
        return new();
    }

    /// <summary>จำนวนรถ (เที่ยว) ที่ยังค้างส่ง — นับตาม source_track ไม่ใช่ตามแถว configID</summary>
    public int UnsyncedTrips()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT source_track) FROM aq_outbox WHERE synced=0;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// วันตัดยอดที่เที่ยวนี้ลง outbox ไว้แล้ว (null = ยังไม่เคยลง) — ใช้วันเดิมเสมอเมื่อปันยอดใหม่
    /// กันยอดซ้ำข้ามวัน (เช่น record เก่าที่ลงตามวันชั่งออกก่อนเปลี่ยนเป็นวันชั่งเข้า)
    /// </summary>
    public string? DateOf(string sourceTrack)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT date FROM aq_outbox WHERE source_track=$t ORDER BY rowid LIMIT 1;";
        cmd.Parameters.AddWithValue("$t", sourceTrack);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>เที่ยวนี้มียอดที่ส่ง Aquadat ไปแล้วอย่างน้อย 1 configID</summary>
    public bool IsSynced(string sourceTrack)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM aq_outbox WHERE source_track=$t AND synced=1);";
        cmd.Parameters.AddWithValue("$t", sourceTrack);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    public void MarkSynced(IEnumerable<long> rowIds, string response)
    {
        var ids = rowIds.ToList();
        if (ids.Count == 0) return;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE aq_outbox SET synced=1, sent_at=datetime('now','+7 hours'), response=$r WHERE rowid IN ({string.Join(",", ids)});";
        cmd.Parameters.AddWithValue("$r", response);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// POST mutation update_transaction_datavalues ไปยัง Aquadat GraphQL ด้วย Bearer token ที่ได้รับมา
/// (มิเรอร์ WritePendingToAquadatAsync ของ Uroboros) — ไม่มีการ login หรือเดารหัสผ่านในที่นี้
/// </summary>
public static class AquadatClient
{
    public static async Task<string> PushAsync(HttpClient http, string url, string token, IReadOnlyList<OutboxRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return "";
        var inputs = rows.Select(r =>
        {
            string v = r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"{{date: \"{r.Date}\", configparam_id: {r.ConfigId}, time: \"{r.Time}\", value: \"{v}\", trans_type: \"{r.TransType}\", flag_value: \"{r.FlagValue}\"}}";
        });
        string mutation = "mutation {\n  update_transaction_datavalues(\n    input_data: [" +
                          string.Join(", ", inputs) +
                          "]\n  ) {\n    return_status\n    return_message\n    __typename\n  }\n}\n";

        var payload = JsonSerializer.Serialize(new { variables = new { }, query = mutation },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
        {
            var msg = errs[0].TryGetProperty("message", out var m) ? m.GetString() : body;
            throw new InvalidOperationException("Aquadat GraphQL error: " + msg);
        }
        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("update_transaction_datavalues", out var res) &&
            res.ValueKind == JsonValueKind.Object &&
            res.TryGetProperty("return_status", out var st) && st.ValueKind == JsonValueKind.False)
        {
            var msg = res.TryGetProperty("return_message", out var mm) ? mm.GetString() : "unknown";
            throw new InvalidOperationException("Aquadat write rejected: " + msg);
        }
        return body;
    }
}
