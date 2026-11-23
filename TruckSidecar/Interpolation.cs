namespace TruckSidecar;

/// <summary>
/// ปริมาณสารเคมี (ตัน) ที่ปันส่วนเข้าถังหนึ่ง จากการชั่ง 1 เที่ยว — Tonnes มีเครื่องหมาย (ลบ = นำออก/ล้างถัง)
/// Estimated = ยังไม่มีระดับถังครบ จึงปันเท่ากันตามถังที่เลือก (จะถูกปันใหม่เมื่อกรอกระดับครบ)
/// </summary>
public sealed record TankAllocation(string Tank, int ConfigId, double FillVolumeM3, double Tonnes, bool Estimated = false);

public static class Interpolation
{
    /// <summary>
    /// น้ำหนักสุทธิ (ตัน) = (W1 − W2) / 1000 แบบมีเครื่องหมาย: บวก = รับเข้า, ลบ = นำออก (ล้างถัง)
    /// ปันส่วนตามปริมาตรที่เปลี่ยนในแต่ละถัง (รับ: หลัง − ก่อน, ล้างถัง: ก่อน − หลัง)
    /// ถ้าระดับถังยังไม่ครบ (ส่งยอดได้ทันทีที่ชั่งเข้า-ออกครบ) → ปันเท่ากันตามถังที่เลือก และติดธง Estimated
    /// คืนค่าเฉพาะถังที่รู้ configId (ปัดทศนิยม 3 ตำแหน่ง); ยังไม่ได้ชั่งออก / ไม่ได้เลือกถัง → ว่าง
    /// </summary>
    public static List<TankAllocation> Allocate(UnloadRecord r, IReadOnlyList<TankConfig> tanks)
    {
        if (r.NetKg is not { } netKg || netKg == 0) return new();
        double netTonnes = netKg / 1000.0;

        var sel = r.Fills
            .Select(f => (f.Tank, Cfg: tanks.FirstOrDefault(t => t.Name == f.Tank), Fill: f))
            .Where(x => x.Cfg != null)
            .ToList();
        if (sel.Count == 0) return new();

        double Vol(FillEntry f) => r.IsWash
            ? (f.LevelBefore ?? 0) - (f.LevelAfter ?? 0)
            : (f.LevelAfter ?? 0) - (f.LevelBefore ?? 0);

        bool levelsComplete = sel.All(x => x.Fill.LevelBefore != null && x.Fill.LevelAfter != null);
        var vols = sel.Select(x => (x.Tank, x.Cfg, Vol: Vol(x.Fill))).Where(x => x.Vol > 0).ToList();
        double totalVol = vols.Sum(x => x.Vol);

        if (levelsComplete && totalVol > 0)
            return vols.Select(x => new TankAllocation(
                x.Tank, x.Cfg!.IncomeConfigId, Math.Round(x.Vol, 3),
                Math.Round(netTonnes * x.Vol / totalVol, 3))).ToList();

        // ระดับยังไม่ครบ (หรือปริมาตรไม่บวก) → ประมาณการ: ปันเท่ากันทุกถังที่เลือก
        return sel.Select(x => new TankAllocation(
            x.Tank, x.Cfg!.IncomeConfigId, 0, Math.Round(netTonnes / sel.Count, 3), Estimated: true)).ToList();
    }

    /// <summary>รวมการปันส่วนตาม configId (เช่น ถัง A+B ที่ใช้ config เดียวกัน) เพื่อส่งเข้า Aquadat หนึ่งค่า — รวมแบบมีเครื่องหมาย</summary>
    public static Dictionary<int, double> ByConfig(IEnumerable<TankAllocation> allocs) =>
        allocs.GroupBy(a => a.ConfigId)
              .ToDictionary(g => g.Key, g => Math.Round(g.Sum(a => a.Tonnes), 3));
}
