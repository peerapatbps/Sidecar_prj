using System.Globalization;

namespace TruckSidecar;

/// <summary>หนึ่งบรรทัดในรายการคำนวณ (ตัน มีเครื่องหมาย)</summary>
public sealed record DayCutLine(string Text, double Tonnes);

/// <summary>ยอดตัดของหนึ่ง configID (คู่ถัง) ในวันหนึ่ง</summary>
public sealed record DayCutConfig(
    int ConfigId, string Name, string Chemical,
    double Raw,          // ผลรวมดิบจากทุกเที่ยว (อาจติดลบเพราะล้างถัง)
    double Auto,         // หลังย้ายยอดติดลบอัตโนมัติ (≥ 0)
    double? Override, string? OverrideNote, string? OverrideBy,
    List<DayCutLine> Lines, bool NeedsCheck)
{
    /// <summary>ยอดที่จะส่ง Aquadat</summary>
    public double Final => Override ?? Auto;
}

public enum DayCutStatus { Pending, Confirmed, ChangedAfterConfirm }

/// <summary>ผลตัดยอดของ 1 วัน (วันชั่งเข้า)</summary>
public sealed record DayCutResult(
    DateTime Day, List<DayCutConfig> Configs, List<PendingItem> Pending,
    DayCutStatus Status, string? ConfirmedAt, string? ConfirmedBy)
{
    public string Key => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    /// <summary>ลายเซ็นยอดที่จะส่ง — ยอดเปลี่ยนหลังยืนยัน = ต้องยืนยันใหม่</summary>
    public string Sig => DayCut.SigOf(Configs);
    public bool NeedsCheck => Configs.Any(c => c.NeedsCheck && c.Override == null);
}

/// <summary>
/// ตัดยอดรายวัน — จบเป็นวันๆ ไม่ยกยอดข้ามวัน
///  1) ยอดดิบต่อคู่ถัง = ผลรวมแบบมีเครื่องหมายของทุกเที่ยว (รับ − นำออก/ล้างถัง)
///  2) Aquadat ไม่รับค่าติดลบ → คู่ถังที่ติดลบย้ายส่วนติดลบไปหักคู่ถังอื่นของสารเดียวกันในวันนั้น
///     (คู่ที่ยอดมากที่สุดก่อน) ถ้ายังติดลบอยู่ = 0 และตั้งธง "ต้องตรวจ" ให้ผู้ใช้แก้ยอด
///  3) ค่าที่ผู้ใช้แก้เอง (override + หมายเหตุ) มีผลเหนือยอดอัตโนมัติ
/// </summary>
public static class DayCut
{
    public static string SigOf(IEnumerable<DayCutConfig> configs) =>
        string.Join(";", configs.OrderBy(c => c.ConfigId).Select(c => $"{c.ConfigId}={c.Final.ToString("0.###", CultureInfo.InvariantCulture)}"));

    /// <summary>ชื่อคู่ถังของ configID เช่น "PACL A+B"</summary>
    public static string GroupName(IEnumerable<TankConfig> tanks, int configId)
    {
        var g = tanks.Where(t => t.IncomeConfigId == configId).ToList();
        return g.Count == 0 ? $"#{configId}" : $"{g[0].Name.Split(' ')[0]} {string.Join("+", g.Select(t => t.Name.Split(' ').Last()))}";
    }

    public static DayCutResult Compute(SidecarController c, DateTime day)
    {
        var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tanks = c.Cfg.Tanks;
        var (raw, unsynced, synced) = c.Outbox.DayRaw(key);
        var trips = c.Outbox.DayTrips(key);
        var overrides = c.Outbox.Overrides(key);
        var conf = c.Outbox.Confirmation(key);

        // คู่ถังที่ต้องอยู่ในการตัดยอด: ทุกคู่ของสารที่มีกิจกรรมวันนั้น + คู่ที่ถูกแก้ + คู่ที่เคยส่งไว้ (จะได้ส่ง 0 ทับได้)
        var sentBefore = conf?.Sig.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Split('=')[0], out var id) ? id : 0).Where(id => id != 0) ?? Enumerable.Empty<int>();
        var activeIds = raw.Keys.Concat(overrides.Keys).Concat(sentBefore).ToHashSet();
        var chems = tanks.Where(t => activeIds.Contains(t.IncomeConfigId)).Select(t => t.Chemical).Distinct().ToList();
        var ids = tanks.Where(t => chems.Contains(t.Chemical)).Select(t => t.IncomeConfigId).Distinct().ToList();

        var lines = ids.ToDictionary(id => id, _ => new List<DayCutLine>());
        foreach (var t in trips)
            foreach (var g in t.Allocations.GroupBy(a => a.ConfigId).Where(g => lines.ContainsKey(g.Key)))
            {
                double tn = Math.Round(g.Sum(a => a.Tonnes), 3);
                string kind = tn < 0 ? $"ล้างถัง {string.Join("+", g.Select(a => a.Tank.Split(' ').Last()))}" : "รับ";
                lines[g.Key].Add(new DayCutLine(
                    $"#{t.Track.TrimStart('0')} {t.Plate} · {kind}" + (g.Any(a => a.Estimated) ? " · ประมาณการ" : ""), tn));
            }

        // ย้ายยอดติดลบไปหักคู่ถังอื่นของสารเดียวกัน (ภายในวันเดียวกัน)
        var auto = ids.ToDictionary(id => id, id => raw.GetValueOrDefault(id));
        var needs = new HashSet<int>();
        foreach (var chem in chems)
        {
            var group = tanks.Where(t => t.Chemical == chem).Select(t => t.IncomeConfigId).Distinct().ToList();
            foreach (var neg in group.Where(id => auto[id] < 0).ToList())
            {
                double deficit = -auto[neg];
                foreach (var pos in group.Where(id => id != neg && auto[id] > 0).OrderByDescending(id => auto[id]).ToList())
                {
                    if (deficit <= 0) break;
                    double take = Math.Round(Math.Min(deficit, auto[pos]), 3);
                    auto[pos] = Math.Round(auto[pos] - take, 3);
                    deficit = Math.Round(deficit - take, 3);
                    lines[pos].Add(new DayCutLine($"หักยอดติดลบจาก {GroupName(tanks, neg)} (ล้างถัง)", -take));
                    lines[neg].Add(new DayCutLine($"ย้ายยอดติดลบไปหักที่ {GroupName(tanks, pos)}", take));
                }
                auto[neg] = 0;
                if (deficit > 0)
                {
                    needs.Add(neg);
                    lines[neg].Add(new DayCutLine($"ยังติดลบ {deficit:0.###} t ไม่มียอดรับให้หัก → ส่ง 0 (ตรวจ/แก้ยอด)", deficit));
                }
            }
        }

        var configs = ids.Select(id =>
        {
            var ov = overrides.TryGetValue(id, out var o) ? o : ((double, string, string, string)?)null;
            return new DayCutConfig(id, GroupName(tanks, id), tanks.First(t => t.IncomeConfigId == id).Chemical,
                raw.GetValueOrDefault(id), Math.Round(auto[id], 3), ov?.Item1, ov?.Item2, ov?.Item3, lines[id], needs.Contains(id));
        }).OrderBy(x => tanks.ToList().FindIndex(t => t.IncomeConfigId == x.ConfigId)).ToList();

        var pending = c.Pending().Where(p => p.Day == day.Date && p.Record?.Ticket2.Length != 0).ToList();
        string sig = SigOf(configs);
        var status =
            !unsynced && (conf == null || conf.Value.Sig == sig) && (conf != null || synced) ? DayCutStatus.Confirmed
            : conf != null || synced ? DayCutStatus.ChangedAfterConfirm
            : DayCutStatus.Pending;
        return new DayCutResult(day.Date, configs, pending, status, conf?.At, conf?.By);
    }
}
