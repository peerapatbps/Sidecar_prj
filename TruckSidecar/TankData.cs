using System.Globalization;
using System.Text.Json;

namespace TruckSidecar;

public sealed record HistPoint(string Tank, DateTime Ts, double Level, string? Status);

public sealed record Prediction(
    double BaseLevel, DateTime BaseTs, string BaseSource,
    double? RatePerHour, double Level, DateTime At, string? RateSource = null);

/// <summary>
/// ผลการดึง API หนึ่งครั้ง: ระดับถังอาจยังไม่เข้า (เช่นถังที่ ON ในรอบล่าสุด) แต่สถานะมาก่อนได้
/// จึงแยกสถานะออกจากจุดระดับ
/// </summary>
public sealed record ChemFetch(List<HistPoint> Levels, Dictionary<string, string?> Statuses, DateTime? SnapshotTs,
    DoseRates? Dose = null);

/// <summary>
/// อัตราจ่ายสารจริง (m³/ชม.) ต่อสารเคมีต่อเฟส คำนวณแบบ BellBeast CHEMview.js: L/h = flow × dose × factor
/// เฟสที่ไม่มีค่า flow จะไม่อยู่ใน dict (ไม่ทราบ) ส่วน dose ว่าง = ไม่จ่าย (0)
/// </summary>
public sealed record DoseRates(Dictionary<string, Dictionary<int, double>> ByChemical, DateTime Ts)
{
    /// <summary>ผลรวมอัตราของเฟสในกลุ่มถังร่วม — null ถ้าไม่มีเฟสใดในกลุ่มที่ทราบค่า</summary>
    public double? Group(string chemical, IEnumerable<int> phases)
    {
        if (!ByChemical.TryGetValue(chemical, out var byPhase)) return null;
        double sum = 0; bool any = false;
        foreach (var ph in phases)
            if (byPhase.TryGetValue(ph, out var r)) { sum += r; any = true; }
        return any ? sum : null;
    }
}

public static class ChemApi
{
    static DateTime ParseTs(JsonElement e) =>
        DateTime.ParseExact(e.GetProperty("ts").GetString()!, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);

    /// <summary>ดึง /api/chem/summary แล้วแปลง Aq[id] เป็นระดับ/สถานะของแต่ละถังตาม config</summary>
    public static async Task<ChemFetch> FetchAsync(HttpClient http, AppConfig cfg)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(cfg.ApiUrl));
        var root = doc.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            throw new InvalidDataException("API ตอบ ok=false");
        var aq = root.GetProperty("Aq");

        var points = new List<HistPoint>();
        var statuses = new Dictionary<string, string?>();
        DateTime? snapshot = null;
        foreach (var t in cfg.Tanks)
        {
            string? status = null;
            if (aq.TryGetProperty(t.StatusId.ToString(), out var st))
            {
                if (st.GetProperty("value_text").ValueKind == JsonValueKind.String)
                    status = st.GetProperty("value_text").GetString();
                statuses[t.Name] = status;
                snapshot = Max(snapshot, ParseTs(st));
            }

            if (aq.TryGetProperty(t.LevelId.ToString(), out var lv) &&
                lv.GetProperty("value").ValueKind == JsonValueKind.Number)
            {
                var ts = ParseTs(lv);
                points.Add(new HistPoint(t.Name, ts, lv.GetProperty("value").GetDouble(), status));
                snapshot = Max(snapshot, ts);
            }
        }
        return new ChemFetch(points, statuses, snapshot, ReadDoseRates(aq, cfg));
    }

    static DoseRates? ReadDoseRates(JsonElement aq, AppConfig cfg)
    {
        if (cfg.PhaseFlow.Count == 0 || cfg.Dosing.Count == 0) return null;
        var flows = new Dictionary<int, double>();   // เฟส → m³/s
        DateTime? ts = null;
        foreach (var f in cfg.PhaseFlow)
            if (Number(aq, f.Id) is { } v && f.Divisor > 0)
            {
                flows[f.Phase] = v.Value / f.Divisor;
                ts = Max(ts, v.Ts);
            }
        if (ts == null) return null;

        var byChem = new Dictionary<string, Dictionary<int, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (chem, d) in cfg.Dosing)
        {
            var byPhase = byChem[chem] = new Dictionary<int, double>();
            foreach (var (phase, doseId) in d.DoseIds)
            {
                if (!flows.TryGetValue(phase, out var flow)) continue;
                double dose = Number(aq, doseId)?.Value ?? 0;   // dose ว่าง/ไม่มี = เฟสนี้ไม่จ่าย
                byPhase[phase] = Math.Max(0, flow * dose * d.Factor) / 1000;   // L/h → m³/h
            }
        }
        return new DoseRates(byChem, ts.Value);
    }

    static (double Value, DateTime Ts)? Number(JsonElement aq, int id) =>
        aq.TryGetProperty(id.ToString(), out var e) && e.GetProperty("value").ValueKind == JsonValueKind.Number
            ? (e.GetProperty("value").GetDouble(), ParseTs(e)) : null;

    static DateTime? Max(DateTime? a, DateTime b) => a is { } x && x > b ? x : b;
}

/// <summary>ประวัติค่าราย 2 ชม. จาก API เก็บไว้ในเครื่อง ใช้คำนวณอัตราการใช้และแสดงค่าล่าสุดเมื่อ API ล่ม</summary>
public sealed class ApiHistory
{
    readonly string _path;
    readonly Dictionary<string, List<HistPoint>> _byTank = new();

    public ApiHistory(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "api_history.jsonl");
        if (!File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = JsonSerializer.Deserialize<HistPoint>(line);
            if (p != null) Add(p);
        }
    }

    public IReadOnlyList<HistPoint> Get(string tank) =>
        _byTank.TryGetValue(tank, out var l) ? l : Array.Empty<HistPoint>();

    /// <summary>เพิ่มเฉพาะจุดที่ ts ใหม่กว่าจุดล่าสุดของถังนั้น</summary>
    public void Append(IEnumerable<HistPoint> points)
    {
        var fresh = points.Where(p => Get(p.Tank) is var l && (l.Count == 0 || p.Ts > l[^1].Ts)).ToList();
        if (fresh.Count == 0) return;
        foreach (var p in fresh) Add(p);
        File.AppendAllLines(_path, fresh.Select(p => JsonSerializer.Serialize(p)));
    }

    void Add(HistPoint p)
    {
        if (!_byTank.TryGetValue(p.Tank, out var l)) _byTank[p.Tank] = l = new List<HistPoint>();
        l.Add(p);
    }
}

public static class Predictor
{
    /// <summary>
    /// คาดการณ์ระดับถัง ณ ตอนนี้
    /// ฐาน = ค่าที่ใหม่ที่สุดระหว่าง API ล่าสุด กับ "ระดับหลังเติม" ที่พนักงานบันทึก
    /// อัตราการใช้ (ถัง ON) ตามลำดับ:
    ///   1) measured = อัตราจ่ายจริง (flow × dose) ที่แบ่งให้ถังนี้แล้ว ถ้ามีและ > 0
    ///   2) เฉลี่ยถ่วงเวลาจากทุกช่วงที่ระดับลดลงขณะถัง ON ภายใน windowHours (ช่วงที่ระดับเพิ่มคือมีการเติม จึงข้าม)
    ///   3) defaultRate จาก config
    /// ถังที่ไม่ได้ ON ถือว่าไม่ถูกใช้
    /// </summary>
    public static Prediction? Predict(TankConfig tank, IReadOnlyList<HistPoint> hist,
        (DateTime At, double Level)? operatorReading, string? statusNow, DateTime now,
        double? defaultRatePerHour = null, int windowHours = 24,
        (double Rate, string Source)? measured = null)
    {
        var last = hist.Count > 0 ? hist[^1] : null;
        if (last == null && operatorReading == null) return null;

        double baseLevel; DateTime baseTs; string source;
        if (operatorReading is { } op && (last == null || op.At > last.Ts))
            (baseLevel, baseTs, source) = (op.Level, op.At, "พนักงานบันทึกหลังเติม");
        else
            (baseLevel, baseTs, source) = (last!.Level, last.Ts, "API");

        double? rate = null;
        string? rateSource = null;
        if ((statusNow ?? last?.Status) is { } status)
        {
            if (!status.Equals("ON", StringComparison.OrdinalIgnoreCase)) { rate = 0; rateSource = "ถังไม่ได้ ON"; }
            else if (measured is { Rate: > 0 } m) { rate = -m.Rate; rateSource = m.Source; }
            else
            {
                double drop = 0, hoursSum = 0;
                int segs = 0;
                for (int i = hist.Count - 1; i >= 1; i--)
                {
                    var a = hist[i - 1];
                    var b = hist[i];
                    if ((now - b.Ts).TotalHours > windowHours) break;
                    double hours = (b.Ts - a.Ts).TotalHours;
                    if (hours <= 0 || hours > 6) continue;
                    if (!IsOn(a.Status) && !IsOn(b.Status)) continue;   // ช่วงที่ถังพัก = ไม่ได้ใช้จริง
                    if (b.Level >= a.Level) continue;                   // เติม/ค่าไม่ขยับ → ข้าม
                    drop += a.Level - b.Level;
                    hoursSum += hours;
                    segs++;
                }
                if (hoursSum > 0) { rate = -drop / hoursSum; rateSource = $"เฉลี่ยจาก API {segs} ช่วง ({hoursSum:0.#} ชม.)"; }
                else if (defaultRatePerHour is { } d && d > 0) { rate = -d; rateSource = "ค่าเริ่มต้นใน config (ประวัติ API ยังไม่พอ)"; }
            }
        }

        double elapsed = Math.Max(0, (now - baseTs).TotalHours);
        // เพดานเป็นพิกัดถัง เว้นแต่ค่าฐานเกินพิกัดอยู่แล้ว (เช่น PACL B 25.2/25) จะไม่ตัดค่านั้นทิ้ง
        double level = Math.Clamp(baseLevel + (rate ?? 0) * elapsed, 0, Math.Max(tank.Capacity, baseLevel));
        return new Prediction(baseLevel, baseTs, source, rate, level, now, rateSource);
    }

    static bool IsOn(string? status) => string.Equals(status, "ON", StringComparison.OrdinalIgnoreCase);
}
