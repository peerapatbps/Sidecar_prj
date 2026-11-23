using System.Data.OleDb;
using System.Globalization;

namespace TruckSidecar;

/// <summary>หนึ่งแถวของ WDATA (STAT '1' = ชั่งเข้าแล้วรอชั่งออก, '2' = ชั่งออกแล้ว)</summary>
public sealed record ScaleRow(
    string Stat, string Truck, string Ticket1, DateTime? WeighInAt, double W1,
    string Ticket2, DateTime? WeighOutAt, double W2, string Remark1, string Remark2, string Product = "")
{
    public bool WeighedOut => Stat == "2";
    public string Ticket1Short => Ticket1.TrimStart('0');
    public string Ticket2Short => Ticket2.TrimStart('0');
}

/// <summary>ผลตรวจ 1 รอบ: รถที่ชั่งเข้าวันนี้ยังไม่ออก (กฎข้อ 2) + รถที่ชั่งออกวันนี้ (กฎข้อ 4)</summary>
public sealed record DetectResult(List<ScaleRow> Ins, List<ScaleRow> Outs);

/// <summary>
/// อ่าน WDATA จาก data.mdb แบบอ่านอย่างเดียว: คัดลอกไฟล์ไปไว้ชั่วคราวก่อนแล้วอ่านจากสำเนา
/// จึงไม่ถือ lock หรือเข้าไปยุ่งกับไฟล์ที่โปรแกรมชั่งกำลังใช้อยู่
/// (Jet 4.0 มีแค่ 32 บิต โปรแกรมนี้จึง build เป็น x86)
/// </summary>
public static class ScaleDb
{
    const string Columns = "STAT, TRUCK, TICKET1, DAYIN, TMIN, W1, TICKET2, DAYOUT, TMOUT, W2, REMARK1, REMARK2, PRODUCT";

    /// <summary>รถที่ยังไม่ชั่งออก + รถที่ชั่งออกภายใน 24 ชม. (ใช้กับปุ่ม "ดึงจากตาชั่ง")</summary>
    public static Task<List<ScaleRow>> CandidatesAsync(string mdbPath) => Task.Run(() =>
    {
        var rows = Query(mdbPath, $"SELECT TOP 200 {Columns} FROM WDATA ORDER BY TICKET1 DESC")[0];
        var since = DateTime.Now.AddHours(-24);
        return rows.Where(r => !r.WeighedOut || (r.WeighOutAt ?? r.WeighInAt) >= since).ToList();
    });

    /// <summary>
    /// ทุกแถวที่ชั่งเข้าภายใน days วันย้อนหลัง (ทุก STAT) — ใช้หางานคงค้าง (รถที่ไม่มีบันทึก)
    /// และสรุปตัดยอดรายวัน ซึ่งนับตามวันชั่งเข้า (DAYIN)
    /// </summary>
    public static Task<List<ScaleRow>> RecentByDayInAsync(string mdbPath, int days) => Task.Run(() =>
        Query(mdbPath, $"SELECT {Columns} FROM WDATA WHERE DAYIN>=DateAdd('d',-{Math.Max(0, days)},Date()) ORDER BY TICKET1")[0]);

    public static Task<Dictionary<string, ScaleRow>> ByTicket1Async(string mdbPath, IReadOnlyCollection<string> ticket1s) => Task.Run(() =>
    {
        if (ticket1s.Count == 0) return new Dictionary<string, ScaleRow>();
        var list = string.Join(",", ticket1s.Select(t => "'" + t.Replace("'", "''") + "'"));
        return Query(mdbPath, $"SELECT {Columns} FROM WDATA WHERE TICKET1 IN ({list})")[0]
            .GroupBy(r => r.Ticket1).ToDictionary(g => g.Key, g => g.First());
    });

    /// <summary>
    /// ตรวจจับรถบนตาชั่ง (สำเนาไฟล์เดียว 2 query):
    ///   ขาเข้า = STAT '1' ยังไม่มี TICKET2 และชั่งเข้าภายใน inWindowHours ชม. (ค่าเริ่มต้น 24)
    ///            → เรียง TICKET1 มาก→น้อย (ตัวแรก = max TICKET1)
    ///            Jet กรองหยาบด้วย DAYIN >= (วันนี้ − N วัน) เพราะ DAYIN เก็บแค่วันที่
    ///            แล้วกรองละเอียดใน C# ด้วยเวลาจริง DAYIN+TMIN >= ตอนนี้ − N ชม.
    ///            (เดิม DAYIN = วันนี้ ทำให้รถที่ชั่งเข้าก่อนเที่ยงคืนไม่ถูกตรวจจับ)
    ///   ขาออก = STAT '2' และ DAYOUT = วันนี้ → เรียง TICKET2 มาก→น้อย (ตัวแรก = max TICKET2)
    ///   ใช้ DAYOUT ไม่ใช้ DAYIN เพราะรถบางคันชั่งเข้าคืนก่อนแล้วออกหลังเที่ยงคืน
    /// </summary>
    public static Task<DetectResult> DetectAsync(string mdbPath, int inWindowHours = 24) => Task.Run(() =>
    {
        int hours = Math.Max(1, inWindowHours);
        int days = (int)Math.Ceiling(hours / 24.0);
        var res = Query(mdbPath,
            $"SELECT {Columns} FROM WDATA WHERE STAT='1' AND (TICKET2 IS NULL OR TICKET2='') AND DAYIN>=DateAdd('d',-{days},Date()) ORDER BY TICKET1 DESC",
            $"SELECT {Columns} FROM WDATA WHERE STAT='2' AND DAYOUT=Date() ORDER BY TICKET2 DESC");
        var since = DateTime.Now.AddHours(-hours);
        var ins = res[0].Where(r => r.WeighInAt is { } t && t >= since).ToList();
        return new DetectResult(ins, res[1]);
    });

    static List<List<ScaleRow>> Query(string mdbPath, params string[] sqls)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "TruckSidecar");
        Directory.CreateDirectory(tmpDir);
        var copy = Path.Combine(tmpDir, $"wdata_{Guid.NewGuid():N}.mdb");
        try
        {
            using (var src = new FileStream(mdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(copy))
                src.CopyTo(dst);

            using var conn = new OleDbConnection($"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={copy};Mode=Read;");
            conn.Open();
            var results = new List<List<ScaleRow>>();
            foreach (var sql in sqls)
            {
                using var cmd = new OleDbCommand(sql, conn);
                using var r = cmd.ExecuteReader()!;
                var rows = new List<ScaleRow>();
                while (r.Read())
                {
                    string S(string c) => r[c] is string s ? s.Trim() : "";
                    double D(string c) => r[c] is double d ? d : 0;
                    rows.Add(new ScaleRow(
                        S("STAT"), S("TRUCK"), S("TICKET1"), At(r["DAYIN"], S("TMIN")), D("W1"),
                        S("TICKET2"), At(r["DAYOUT"], S("TMOUT")), D("W2"), S("REMARK1"), S("REMARK2"), S("PRODUCT")));
                }
                results.Add(rows);
            }
            return results;
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            try { File.Delete(copy); } catch { /* ลบรอบหน้า */ }
        }
    }

    /// <summary>DAYIN/DAYOUT เก็บแค่วันที่ ส่วนเวลาอยู่ใน TMIN/TMOUT เป็นข้อความ "HH:mm:ss"</summary>
    static DateTime? At(object day, string time)
    {
        if (day is not DateTime d) return null;
        return TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var t) ? d.Date + t : d.Date;
    }
}
