using System.Globalization;
using System.Text.Json;

namespace TruckSidecar;

/// <summary>รถที่ตรวจพบในตาชั่งแต่ยังไม่มีบันทึกใน sidecar</summary>
/// <param name="IsPrimary">= max(TICKET1) ของรถที่ชั่งเข้าวันนี้ (กฎข้อ 2) → เติมฟอร์มอัตโนมัติ</param>
/// <param name="MissedWeighIn">ชั่งออกไปแล้วแต่ยังไม่มีบันทึก (หลุดตอนชั่งเข้า)</param>
public sealed record Arrival(ScaleRow Row, string Chemical, bool IsPrimary, bool MissedWeighIn);

/// <summary>
/// งานคงค้าง 1 รายการ (Day = วันชั่งเข้า = วันตัดยอด)
/// Record = null → ชั่งออกแล้วแต่ไม่มีบันทึก (ยังไม่นับในยอด); Counted = ลง outbox แล้ว (อาจเป็นประมาณการ)
/// </summary>
public sealed record PendingItem(DateTime Day, string Chemical, string Ticket1, string Plate, string Reason,
                                 bool Counted, ScaleRow? Row, UnloadRecord? Record);

/// <summary>ผล crosscheck ก่อนบันทึก (กฎข้อ 5)</summary>
public sealed class CheckResult
{
    public List<string> Blocks { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>อ่าน data.mdb ไม่ได้ขณะตรวจ — ให้พนักงานเลือกว่าจะบันทึกต่อหรือไม่</summary>
    public string? MdbError { get; set; }
}

/// <summary>
/// ตรรกะทั้งหมดที่ไม่ใช่ UI: ตรวจจับตาชั่ง, API ถัง, บันทึก + crosscheck, Aquadat
/// UI (MainWindow/ReceivePanel) เรียกผ่านคลาสนี้ และฟัง event เพื่อวาดใหม่
/// </summary>
public sealed class SidecarController
{
    public AppConfig Cfg { get; }
    public string DataDir { get; }
    public RecordStore Store { get; }
    public ApiHistory History { get; }
    public AquadatOutbox Outbox { get; }
    public DismissedSet Dismissed { get; }
    public ScaleWatcher Watcher { get; }
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public Dictionary<string, string?> Statuses { get; private set; } = new();
    public DateTime? SnapshotTs { get; private set; }
    /// <summary>อัตราจ่ายสารจริงจาก API รอบล่าสุดที่อ่านได้ (คงไว้ตอน API ล่ม จนเก่าเกิน MaxDoseAgeHours)</summary>
    public DoseRates? Dose { get; private set; }
    const double MaxDoseAgeHours = 6;
    public DateTime? LastFetchOk { get; private set; }
    public string? LastFetchError { get; private set; }

    public List<Arrival> Arrivals { get; private set; } = new();
    /// <summary>ทุกแถวที่ชั่งเข้าภายใน PendingLookbackDays วัน (รีเฟรชทุก ForceCheckSeconds) — null = ยังไม่เคยอ่านได้</summary>
    public List<ScaleRow>? RecentScale { get; private set; }
    DateTime _recentAt = DateTime.MinValue;
    readonly HashSet<string> _announced = new();
    readonly HashSet<string> _conflicts = new();
    bool _scanning, _pushing;

    /// <summary>token ของ aquadat เดิม (v1) — ใช้เมื่อ config aquadatApi = 1</summary>
    public string AqToken { get; private set; } = "";
    /// <summary>เซสชัน aquadat2 (cookie + JWT + refresh token) — ใช้เมื่อ config aquadatApi = 2</summary>
    Aquadat2Session? _aq2;
    public bool AqLoggedIn => Cfg.UseAquadat2 ? _aq2 != null : !string.IsNullOrWhiteSpace(AqToken);
    /// <summary>ชื่อระบบที่แสดงบน UI</summary>
    public string AqSystemName => Cfg.UseAquadat2 ? "Aquadat2" : "Aquadat";
    public bool Pushing => _pushing;

    /// <summary>รถคันล่าสุด (max TICKET1) ปรากฏครั้งแรก → UI ตัดสินใจเติมฟอร์มหรือแสดงเป็นชิป</summary>
    public event Action<Arrival>? NewPrimaryArrival;
    /// <summary>record ที่มีอยู่ได้ผลชั่งออก (TICKET2/W2) จาก data.mdb</summary>
    public event Action<UnloadRecord>? RecordWeighedOut;
    /// <summary>ตาชั่งขัดกับบันทึก (TICKET2 ซ้ำ / ทะเบียนไม่ตรงตอนชั่งออก) — แจ้งเตือนสีแดง</summary>
    public event Action<string>? ScaleConflict;
    /// <summary>ข้อมูลใดๆ เปลี่ยน → วาดใหม่</summary>
    public event Action? Changed;

    public SidecarController(AppConfig cfg, string baseDir)
    {
        Cfg = cfg;
        DataDir = Path.IsPathRooted(cfg.DataDir) ? cfg.DataDir : Path.Combine(baseDir, cfg.DataDir);
        Store = new RecordStore(DataDir);
        History = new ApiHistory(DataDir);
        Outbox = new AquadatOutbox(DataDir);
        Dismissed = new DismissedSet(DataDir);
        Watcher = new ScaleWatcher(cfg.MdbPath, cfg.ForceCheckSeconds, cfg.InWindowHours);
    }

    public IReadOnlyList<TankConfig> TanksOf(string chemical) => Cfg.Tanks.Where(t => t.Chemical == chemical).ToList();
    public IEnumerable<string> Chemicals => Cfg.Tanks.Select(t => t.Chemical).Distinct();

    // ───────────────────────── ตรวจจับตาชั่ง (กฎข้อ 2, 4) ─────────────────────────

    public async Task ScanScaleAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            var det = await Watcher.CheckAsync();
            if (det != null)
            {
                Process(det);
                await CatchUpOldWaitingAsync(det);
            }
            await RefreshRecentAsync(det != null);
        }
        finally
        {
            _scanning = false;
            Changed?.Invoke();
        }
    }

    void Process(DetectResult det)
    {
        // ขาออก (กฎข้อ 4): เติม TICKET2/W2 ให้ record ที่ผูก TICKET1 ไว้แล้ว
        foreach (var row in det.Outs)
            if (Store.FindByTrackId(row.Ticket1) is { } rec && rec.Ticket2.Length == 0)
                ApplyWeighOut(rec, row);

        // ขาเข้า (กฎข้อ 2): เฉพาะสารเคมีที่รู้จัก, ยังไม่มีบันทึก, ไม่ถูกกด "ไม่ใช่รถสารเคมี"
        var list = new List<Arrival>();
        var ins = det.Ins.Where(r => Cfg.ChemicalOf(r.Product) != null).ToList();
        var primary = ins.FirstOrDefault()?.Ticket1;          // Ins เรียง TICKET1 มาก→น้อย
        foreach (var r in ins)
            if (Store.FindByTrackId(r.Ticket1) == null && !Dismissed.Contains(r.Ticket1))
                list.Add(new Arrival(r, Cfg.ChemicalOf(r.Product)!, r.Ticket1 == primary, false));
        foreach (var r in det.Outs)
            if (Cfg.ChemicalOf(r.Product) is { } chem && Store.FindByTrackId(r.Ticket1) == null && !Dismissed.Contains(r.Ticket1))
                list.Add(new Arrival(r, chem, false, true));
        Arrivals = list;

        if (list.FirstOrDefault(a => a.IsPrimary) is { } p && _announced.Add(p.Row.Ticket1))
            NewPrimaryArrival?.Invoke(p);
    }

    /// <summary>
    /// ตั๋วค้างจากวันก่อน (sidecar ปิดอยู่ตอนรถชั่งออก ฯลฯ) ไม่อยู่ในผล query "วันนี้"
    /// → ตามด้วย TICKET1 ตรงๆ แบบเดียวกับ v1
    /// </summary>
    async Task CatchUpOldWaitingAsync(DetectResult det)
    {
        var seen = det.Ins.Concat(det.Outs).Select(r => r.Ticket1).ToHashSet();
        var waiting = Store.All.Where(r => r.TrackId.Length > 0 && r.Ticket2.Length == 0 && !seen.Contains(r.TrackId)).ToList();
        if (waiting.Count == 0) return;
        try
        {
            var rows = await ScaleDb.ByTicket1Async(Cfg.MdbPath, waiting.Select(r => r.TrackId).ToList());
            foreach (var rec in waiting)
                if (rows.TryGetValue(rec.TrackId, out var row) && row.WeighedOut) ApplyWeighOut(rec, row);
        }
        catch { /* ลองใหม่รอบหน้า */ }
    }

    /// <summary>
    /// อ่านรถย้อนหลัง PendingLookbackDays วัน (ตามวันชั่งเข้า) — ใช้หางานคงค้างที่ข้ามวันไปแล้ว
    /// และสรุปตัดยอด; ไม่ต้องอ่านทุก 3 วิ: อ่านเมื่อ data.mdb เปลี่ยน หรือทุก ForceCheckSeconds
    /// </summary>
    async Task RefreshRecentAsync(bool changed)
    {
        if (!Watcher.Enabled) return;
        if (!changed && RecentScale != null && (DateTime.Now - _recentAt).TotalSeconds < Math.Max(5, Cfg.ForceCheckSeconds)) return;
        try
        {
            RecentScale = await ScaleDb.RecentByDayInAsync(Cfg.MdbPath, Cfg.PendingLookbackDays);
            _recentAt = DateTime.Now;
        }
        catch { /* ลองใหม่รอบหน้า (ใช้ชุดเดิมไปก่อน) */ }
    }

    void ApplyWeighOut(UnloadRecord rec, ScaleRow row)
    {
        // กันผูก TICKET2 เดียวกันซ้ำ 2 record (กฎข้อ 5)
        if (Store.FindByTicket2(row.Ticket2) is { } dup && dup.Id != rec.Id)
        {
            Conflict($"t2|{rec.TrackId}|{row.Ticket2}",
                new { block = "ticket2-duplicate", ticket1 = rec.TrackId, ticket2 = row.Ticket2, other = dup.TrackId },
                $"ไม่รับผลชั่งออกของ {rec.Plate} #{Short(rec.TrackId)}\nตั๋วออก {row.Ticket2Short} ผูกกับ #{Short(dup.TrackId)} ({dup.Plate}) อยู่แล้ว — แจ้งผู้ดูแลตรวจ data.mdb");
            return;
        }
        if (row.Truck != rec.Plate)
        {
            Conflict($"plate|{rec.TrackId}|{row.Truck}",
                new { block = "weighout-plate-mismatch", ticket1 = rec.TrackId, record = rec.Plate, mdb = row.Truck },
                $"ไม่รับผลชั่งออกของ #{Short(rec.TrackId)}\nทะเบียนในบันทึก {rec.Plate} ≠ ตาชั่ง {row.Truck} — แจ้งผู้ดูแลตรวจ data.mdb");
            return;
        }
        rec.ApplyScale(row);
        Store.Save(rec, "scale");
        EnqueueAquadat(rec);
        if (KindMismatch(rec) is { } km)
            Conflict($"kind|{rec.TrackId}", new { warn = "kind-mismatch", ticket1 = rec.TrackId, kind = rec.Kind, netKg = rec.NetKg }, km);
        RecordWeighedOut?.Invoke(rec);
    }

    /// <summary>
    /// ข้อมูลตาชั่งขัดกับบันทึก (ตรวจพบตอนสแกนอัตโนมัติ) — จด audit + แจ้งพนักงาน ครั้งเดียวต่อกรณี
    /// (ไม่งั้น query บังคับทุก 30 วิ จะจด audit ซ้ำไปเรื่อยๆ)
    /// </summary>
    void Conflict(string key, object detail, string message)
    {
        if (!_conflicts.Add(key)) return;
        Store.Audit("crosscheck", detail);
        ScaleConflict?.Invoke(message);
    }

    public void Dismiss(ScaleRow row)
    {
        Dismissed.Add(row.Ticket1);
        Store.Audit("dismiss", new { ticket1 = row.Ticket1, plate = row.Truck, product = row.Product });
        Arrivals = Arrivals.Where(a => a.Row.Ticket1 != row.Ticket1).ToList();
        Changed?.Invoke();
    }

    // ───────────────────────── บันทึก + crosscheck (กฎข้อ 5) ─────────────────────────

    /// <summary>
    /// ตรวจก่อนบันทึก: อ่านแถว TICKET1 จาก data.mdb ใหม่ แล้วเทียบทะเบียน/วันชั่งเข้า/สารเคมี/TICKET2
    /// ถ้าแถวชั่งออกแล้วจะเติม TICKET2/W2 ให้ r ก่อนบันทึก
    /// </summary>
    public async Task<CheckResult> CrossCheckAsync(UnloadRecord r)
    {
        var res = new CheckResult();
        if (r.TrackId.Length == 0)
        {
            res.Blocks.Add("ยังไม่ได้เลือกรถ — รอระบบตรวจพบรถบนตาชั่ง หรือกด \"ดึงจากตาชั่ง\"");
            return res;
        }
        if (Store.FindByTrackId(r.TrackId) is { } dup && dup.Id != r.Id)
            res.Blocks.Add($"Track ID {Short(r.TrackId)} ถูกบันทึกแล้ว (ทะเบียน {dup.Plate}) — เปิดจากรายการตั๋วค้างเพื่อแก้ไข");

        if (!string.IsNullOrEmpty(Cfg.MdbPath))
        {
            try
            {
                var rows = await ScaleDb.ByTicket1Async(Cfg.MdbPath, new[] { r.TrackId });
                if (!rows.TryGetValue(r.TrackId, out var row))
                    res.Blocks.Add($"ไม่พบ Track ID {Short(r.TrackId)} ใน data.mdb (อาจถูกยกเลิกในโปรแกรมชั่ง)");
                else
                {
                    if (row.Truck != r.Plate)
                        res.Blocks.Add($"ทะเบียนไม่ตรงกับตาชั่ง: บันทึก {r.Plate} แต่ data.mdb เป็น {row.Truck}");
                    if (r.WeighInAt is { } a && row.WeighInAt is { } b && a.Date != b.Date)
                        res.Blocks.Add($"วันชั่งเข้าไม่ตรงกับตาชั่ง ({a:dd/MM} ≠ {b:dd/MM})");
                    if (row.WeighedOut)
                    {
                        if (Store.FindByTicket2(row.Ticket2) is { } d2 && d2.Id != r.Id)
                            res.Blocks.Add($"ตั๋วชั่งออก {row.Ticket2Short} ผูกกับ Track ID {Short(d2.TrackId)} อยู่แล้ว");
                        else if (res.Blocks.Count == 0)
                            r.ApplyScale(row);    // ชั่งออกไปแล้วระหว่างกรอก → เก็บผลชั่งออกด้วย
                    }
                    if (Cfg.ChemicalOf(row.Product) is { } mdbChem && r.Chemical.Length > 0 && mdbChem != r.Chemical)
                        res.Warnings.Add($"สินค้าในตาชั่งเป็น {mdbChem} แต่บันทึกเป็น {r.Chemical}");
                }
            }
            catch (Exception ex) { res.MdbError = ex.Message; }
        }

        if (r.Chemical.Length > 0)
            foreach (var f in r.Fills)
                if (Cfg.Tanks.FirstOrDefault(t => t.Name == f.Tank) is { } t && t.Chemical != r.Chemical)
                    res.Blocks.Add($"รถ {r.Chemical} เลือกถัง {f.Tank} ({t.Chemical}) ไม่ได้");

        // ค่าผิดปกติ (เตือน ยืนยันแล้วบันทึกได้)
        if (KindMismatch(r) is { } km) res.Warnings.Add(km);
        if (!r.IsWash)
        {
            if (r.Sg is { } sg && (sg < 1.0 || sg > 1.6)) res.Warnings.Add($"ถ.พ. {sg} อยู่นอกช่วงปกติ (1.0–1.6)");
            if (r.TempC is { } tc && (tc < 5 || tc > 60)) res.Warnings.Add($"อุณหภูมิ {tc} °C ผิดปกติ");
        }
        foreach (var f in r.Fills)
        {
            var cap = Cfg.Tanks.FirstOrDefault(t => t.Name == f.Tank)?.Capacity ?? double.MaxValue;
            if (f.LevelAfter > cap || f.LevelBefore > cap) res.Warnings.Add($"{f.Tank}: ระดับเกินพิกัดถัง {cap} m³");
            if (f.LevelBefore is { } b && f.LevelAfter is { } a)
            {
                if (!r.IsWash && a <= b) res.Warnings.Add($"{f.Tank}: ระดับหลังเติม ({a}) ไม่มากกว่าก่อนเติม ({b})");
                if (r.IsWash && a >= b) res.Warnings.Add($"{f.Tank}: ล้างถังแต่ระดับหลัง ({a}) ไม่น้อยกว่าก่อน ({b})");
            }
        }

        if (res.Blocks.Count > 0 || res.MdbError != null || res.Warnings.Count > 0)
            Store.Audit("crosscheck", new
            {
                ticket1 = r.TrackId, plate = r.Plate, blocks = res.Blocks, warnings = res.Warnings, mdbError = res.MdbError,
            });
        return res;
    }

    /// <summary>ทิศของน้ำหนักไม่ตรงกับประเภทเที่ยว (ชั่งออกแล้วเท่านั้น) — null = ตรง/ยังไม่รู้</summary>
    public static string? KindMismatch(UnloadRecord r) => r.NetKg switch
    {
        < 0 when !r.IsWash => $"#{Short(r.TrackId)} ชั่งออกหนักกว่าชั่งเข้า ({r.W1:#,0} → {r.W2:#,0} กก.)\nเป็นเที่ยวล้างถัง (นำออก) หรือไม่? — เลือกประเภท \"ล้างถัง\" ในฟอร์ม",
        > 0 when r.IsWash => $"#{Short(r.TrackId)} เลือกเป็นล้างถัง แต่ชั่งออกเบากว่าชั่งเข้า ({r.W1:#,0} → {r.W2:#,0} กก.) — ตรวจประเภทเที่ยว",
        _ => null,
    };

    public void Commit(UnloadRecord r)
    {
        Store.Save(r);
        EnqueueAquadat(r);
        Arrivals = Arrivals.Where(a => a.Row.Ticket1 != r.TrackId).ToList();
        Changed?.Invoke();
        _ = SyncAsync();
        // ไม่ส่ง Aquadat อัตโนมัติ — ส่งเมื่อผู้ใช้ยืนยันตัดยอดของวันนั้นเท่านั้น
    }

    static string Short(string ticket) => ticket.TrimStart('0');

    // ───────────────────────── API ถัง ─────────────────────────

    public async Task FetchApiAsync()
    {
        try
        {
            var fetch = await ChemApi.FetchAsync(_http, Cfg);
            History.Append(fetch.Levels);
            Statuses = fetch.Statuses;
            SnapshotTs = fetch.SnapshotTs;
            if (fetch.Dose != null) Dose = fetch.Dose;
            LastFetchOk = DateTime.Now;
            LastFetchError = null;
        }
        catch (Exception ex)
        {
            LastFetchError = ex.Message;
        }
        Changed?.Invoke();
        await SyncAsync();
    }

    /// <param name="Active">เที่ยวที่เลือกถังนี้แล้วแต่ยังไม่จบ (ยังไม่ชั่งออก หรือยังไม่บันทึกระดับหลังเติม) — ใช้ไฮไลต์ถังบนกราฟ</param>
    public sealed record TankView(TankConfig Tank, double? ApiLevel, string? Status, DateTime? StaleTs, Prediction? Pred, HistPoint? Last,
                                  IReadOnlyList<UnloadRecord> Active);

    /// <summary>
    /// ถังที่ "ถูกเลือก" = มีเที่ยวผูกถังนี้แล้ว แต่ยังไม่ครบทั้ง ชั่งออก (W2) และ ระดับหลังเติมของถังนี้
    /// ครบทั้งสองอย่างเมื่อไร ถังกลับเป็นสีปกติ — ดูย้อนหลังแค่ PendingLookbackDays กัน record ค้างเก่าไฮไลต์ไม่จบ
    /// </summary>
    public Dictionary<string, List<UnloadRecord>> ActiveFills()
    {
        var since = DateTime.Today.AddDays(-Math.Max(1, Cfg.PendingLookbackDays));
        var d = new Dictionary<string, List<UnloadRecord>>();
        foreach (var r in Store.All)
        {
            if ((r.WeighInAt ?? r.CreatedAt) < since) continue;
            foreach (var f in r.Fills)
            {
                if (f.Tank.Length == 0 || (r.StepWeighOut && f.LevelAfter != null)) continue;
                if (!d.TryGetValue(f.Tank, out var l)) d[f.Tank] = l = new();
                if (!l.Contains(r)) l.Add(r);
            }
        }
        foreach (var l in d.Values) l.Sort((a, b) => (a.WeighInAt ?? a.CreatedAt).CompareTo(b.WeighInAt ?? b.CreatedAt));
        return d;
    }

    public List<TankView> TankViews(string chemical)
    {
        var now = DateTime.Now;
        var readings = Store.LatestAfterReadings();
        var active = ActiveFills();
        var list = new List<TankView>();
        string? StatusOf(TankConfig t) =>
            Statuses.TryGetValue(t.Name, out var s) ? s : History.Get(t.Name) is { Count: > 0 } h ? h[^1].Status : null;
        // อัตราเริ่มต้นเป็นยอดใช้ทั้งระบบของสารนั้น → แบ่งเท่าๆ กันตามจำนวนถังที่ ON อยู่
        bool IsOn(TankConfig t) => string.Equals(StatusOf(t), "ON", StringComparison.OrdinalIgnoreCase);
        int onCount = Math.Max(1, TanksOf(chemical).Count(IsOn));
        var dose = Dose is { } d0 && (now - d0.Ts).TotalHours <= MaxDoseAgeHours ? d0 : null;
        foreach (var t in TanksOf(chemical))
        {
            // อัตราจ่ายจริงของกลุ่มถังร่วม (เฟสเดียวกัน) → แบ่งเท่าๆ กันตามจำนวนถังที่ ON ในกลุ่ม
            (double, string)? measured = null;
            if (dose != null && t.Phases.Count > 0 && dose.Group(t.Chemical, t.Phases) is { } groupRate)
            {
                int onInGroup = Math.Max(1, TanksOf(chemical).Count(o => o.Phases.SequenceEqual(t.Phases) && IsOn(o)));
                measured = (groupRate / onInGroup,
                    $"อัตราจ่ายจริง Ph{string.Join("+", t.Phases)} {groupRate:0.00} m³/ชม. ÷ {onInGroup} ถัง (flow×dose {dose.Ts:HH:mm})");
            }
            var hist = History.Get(t.Name);
            var last = hist.Count > 0 ? hist[^1] : null;
            (DateTime, double)? op = readings.TryGetValue(t.Name, out var rd) ? rd : null;
            var status = StatusOf(t);
            // ระดับรอบล่าสุดยังไม่เข้า → แสดงค่ารอบก่อนพร้อมเวลากำกับ
            DateTime? stale = last != null && SnapshotTs is { } snap && last.Ts < snap ? last.Ts : null;
            double? defRate = Cfg.DefaultUsagePerHour.TryGetValue(t.Chemical, out var dr) ? dr / onCount : null;
            list.Add(new TankView(t, last?.Level, status, stale,
                Predictor.Predict(t, hist, op, status, now, defRate, Cfg.RateWindowHours, measured), last,
                active.TryGetValue(t.Name, out var act) ? act : Array.Empty<UnloadRecord>()));
        }
        return list;
    }

    public DateTime LatestApiTs =>
        SnapshotTs ?? Cfg.Tanks.Select(t => History.Get(t.Name)).Where(h => h.Count > 0)
                               .Select(h => h[^1].Ts).DefaultIfEmpty().Max();

    // ───────────────────────── Central DB (dormant hook) ─────────────────────────

    async Task SyncAsync()
    {
        try { await Store.SyncAsync(_http, Cfg.CentralApiUrl); }
        catch { /* ลองใหม่รอบหน้า */ }
    }

    // ───────────────────────── Aquadat (interpolate → outbox → ส่งด้วย token) ─────────────────────────

    /// <summary>
    /// คำนวณปริมาณ (ตัน, มีเครื่องหมาย) แบบ interpolate แล้วลง outbox — ทันทีที่ชั่งเข้า-ออกครบและเลือกถังแล้ว
    /// (ระดับถังยังไม่ครบ = ปันเท่ากัน/ประมาณการ แล้วปันใหม่เองเมื่อกรอกครบ; Upsert ตั้งค้างส่งเพื่อส่งทับ)
    /// วันตัดยอด = วันชั่งเข้า; เที่ยวที่เคยลง outbox แล้วใช้วันเดิมเสมอ (กันยอดซ้ำข้ามวัน)
    /// </summary>
    void EnqueueAquadat(UnloadRecord r)
    {
        var existing = Outbox.DateOf(r.TrackId);
        var allocs = r.Countable ? Interpolation.Allocate(r, Cfg.Tanks) : new();
        if (allocs.Count == 0)
        {
            // เคยลงยอดไว้ แต่ตอนนี้นับไม่ได้แล้ว (เช่น เอาถังออกหมด) → ถอนยอด: ที่ยังไม่ส่งลบ, ที่ส่งแล้วตั้ง 0 ให้ส่งทับ
            if (existing != null) Outbox.Upsert(r.TrackId, existing, Hhmm(r), Array.Empty<(int, double)>(), Detail(r, allocs));
            return;
        }
        var date = existing ?? (r.WeighInAt ?? r.WeighOutAt ?? DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Outbox.Upsert(r.TrackId, date, Hhmm(r), Interpolation.ByConfig(allocs).Select(kv => (kv.Key, kv.Value)), Detail(r, allocs));

        static string Hhmm(UnloadRecord r) => (r.WeighOutAt ?? r.WeighInAt ?? DateTime.Now).ToString("HH:mm", CultureInfo.InvariantCulture);
        static string Detail(UnloadRecord r, List<TankAllocation> allocs) => JsonSerializer.Serialize(new
        {
            r.Plate, track = r.TrackId, kind = r.Kind, netKg = r.NetKg, estimated = allocs.Any(a => a.Estimated), allocations = allocs,
        });
    }

    // ───────────────────────── งานคงค้าง / ตัดยอดรายวัน (นับตามวันชั่งเข้า) ─────────────────────────

    static DateTime DayOf(ScaleRow s) => (s.WeighInAt ?? s.WeighOutAt ?? DateTime.Today).Date;
    static DateTime DayOf(UnloadRecord r) => (r.WeighInAt ?? r.WeighOutAt ?? r.CreatedAt).Date;

    static string Reason(UnloadRecord r) =>
        r.Ticket2.Length == 0 ? "ยังไม่ชั่งออก"
        : $"ขาด {string.Join(", ", r.Missing)}" + (r.Countable ? " (ยอดเป็นประมาณการ)" : " (ยังไม่นับในยอด)");

    /// <summary>
    /// งานคงค้างของสารเคมี (chem = null → ทุกสาร) เรียงตามวันชั่งเข้า:
    /// รถที่ชั่งออกแล้วแต่ไม่มีบันทึก (ย้อนหลัง PendingLookbackDays วัน) + record ที่ยังไม่ครบ
    /// excludeTickets = ตั๋วที่ UI แสดงอยู่แล้ว (ชิปรถวันนี้จาก Arrivals) กันแสดงซ้ำ
    /// </summary>
    public List<PendingItem> Pending(string? chem = null, ISet<string>? excludeTickets = null)
    {
        var list = new List<PendingItem>();
        foreach (var s in RecentScale ?? new())
        {
            if (!s.WeighedOut || Cfg.ChemicalOf(s.Product) is not { } c || (chem != null && c != chem)) continue;
            if (Store.FindByTrackId(s.Ticket1) != null || Dismissed.Contains(s.Ticket1)) continue;
            if (excludeTickets?.Contains(s.Ticket1) == true) continue;
            list.Add(new PendingItem(DayOf(s), c, s.Ticket1, s.Truck, "ชั่งออกแล้วแต่ยังไม่มีบันทึก (ยังไม่นับในยอด)", false, s, null));
        }
        foreach (var r in Store.All.Where(r => !r.IsComplete))
        {
            var c = r.ChemicalOr(Cfg.Tanks);
            if (chem != null && c != chem && c.Length > 0) continue;
            list.Add(new PendingItem(DayOf(r), c, r.TrackId, r.Plate, Reason(r), r.Countable, null, r));
        }
        return list.OrderBy(p => p.Day).ThenBy(p => p.Ticket1).ToList();
    }

    /// <summary>ข้อความเตือนงานคงค้างของวันเดียว ก่อนยืนยันตัดยอด (ว่าง = ไม่มี)</summary>
    public static string PendingText(DayCutResult cut)
    {
        if (cut.Pending.Count == 0) return "";
        var sb = new System.Text.StringBuilder($"มีงานคงค้างของวันที่ {cut.Day:dd/MM} คือ\n");
        foreach (var p in cut.Pending) sb.AppendLine($" • #{Short(p.Ticket1)} {p.Plate} ({p.Chemical}) — {p.Reason}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>แก้ยอดตัดของคู่ถังเอง (≥ 0 + หมายเหตุบังคับ) — วันนั้นต้องยืนยันใหม่</summary>
    public void SetOverride(DateTime day, int configId, double value, string note)
    {
        var cut = CutOf(day);
        var before = cut.Configs.FirstOrDefault(x => x.ConfigId == configId);
        Outbox.SetOverride(cut.Key, configId, value, note);
        Store.Audit("daycut-override", new { day = cut.Key, configId, value, note, auto = before?.Auto, previous = before?.Override });
        Changed?.Invoke();
    }

    /// <summary>ยกเลิกค่าที่แก้เอง กลับไปใช้ยอดคำนวณ — วันนั้นต้องยืนยันใหม่</summary>
    public void ClearOverride(DateTime day, int configId)
    {
        var cut = CutOf(day);
        var before = cut.Configs.FirstOrDefault(x => x.ConfigId == configId);
        Outbox.ClearOverride(cut.Key, configId);
        Store.Audit("daycut-override-clear", new { day = cut.Key, configId, previous = before?.Override, note = before?.OverrideNote, auto = before?.Auto });
        Changed?.Invoke();
    }

    /// <summary>ผลตัดยอดของวัน (วันชั่งเข้า)</summary>
    public DayCutResult CutOf(DateTime day) => DayCut.Compute(this, day.Date);

    /// <summary>
    /// วันที่ต้องแสดงในหน้าตัดยอด (ย้อนหลัง PendingLookbackDays วัน, ใหม่ → เก่า):
    /// วันที่มียอด/แก้ยอด/ยืนยัน + วันที่มีงานคงค้าง (เช่น รถที่ไม่มีบันทึก ซึ่งยังไม่มียอดเลย)
    /// </summary>
    public List<DayCutResult> DayCuts()
    {
        var days = new HashSet<DateTime>();
        try
        {
            foreach (var d in Outbox.ActiveDates(Cfg.PendingLookbackDays))
                if (DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) days.Add(dt.Date);
        }
        catch { /* outbox อ่านไม่ได้ชั่วคราว */ }
        var oldest = DateTime.Today.AddDays(-Cfg.PendingLookbackDays);
        foreach (var p in Pending()) if (p.Day >= oldest && p.Record?.Ticket2.Length != 0) days.Add(p.Day);
        return days.OrderByDescending(d => d).Select(CutOf).ToList();
    }

    /// <summary>
    /// สรุปตัดยอดของวัน (วันชั่งเข้า) ต่อสารเคมี: จำนวนคัน + ยอดตันต่อกลุ่มถังจาก outbox + สถานะรายตั๋ว
    /// คืน (ข้อความ, มีงานค้างไหม) — ต้องมี RecentScale แล้ว
    /// </summary>
    public (string Text, bool HasPending) DaySummary(DateTime day)
    {
        var sb = new System.Text.StringBuilder();
        bool pending = false;
        DayCutResult? cut;
        try { cut = CutOf(day); } catch { cut = null; }

        foreach (var chem in Chemicals)
        {
            var rows = (RecentScale ?? new()).Where(s => DayOf(s) == day.Date && Cfg.ChemicalOf(s.Product) == chem && !Dismissed.Contains(s.Ticket1)).ToList();
            var recs = Store.All.Where(r => DayOf(r) == day.Date && r.ChemicalOr(Cfg.Tanks) == chem).ToList();
            var tickets = rows.Select(s => s.Ticket1).Union(recs.Select(r => r.TrackId)).ToList();
            if (tickets.Count == 0) continue;

            int done = 0; var notDone = new List<string>(); var noRec = new List<string>(); int wash = 0;
            foreach (var t in tickets)
            {
                var rec = recs.FirstOrDefault(r => r.TrackId == t) ?? Store.FindByTrackId(t);
                var row = rows.FirstOrDefault(s => s.Ticket1 == t);
                if (rec?.IsWash == true) wash++;
                if (rec == null) noRec.Add($"#{Short(t)} {row?.Truck}" + (row?.WeighedOut == true ? $" ชั่งออก {row.WeighOutAt:HH:mm}" : " ยังไม่ชั่งออก"));
                else if (rec.IsComplete) done++;
                else notDone.Add($"#{Short(t)} {rec.Plate} {Reason(rec)}");
            }
            int trucks = tickets.Count - wash;
            sb.AppendLine($"{chem}: รถรับ {trucks} คัน" + (wash > 0 ? $" · ล้างถัง {wash} เที่ยว" : ""));
            var parts = (cut?.Configs ?? new()).Where(x => x.Chemical == chem)
                .Select(x => $"{x.Name} {x.Final:0.###} t" + (x.Override != null ? " (แก้ด้วยมือ)" : x.NeedsCheck ? " ⚠ ต้องตรวจ" : "")).ToList();
            sb.AppendLine("   ยอดที่จะส่ง: " + (parts.Count > 0 ? string.Join(" · ", parts) : "ยังไม่มียอด"));
            pending |= cut?.Configs.Any(x => x.Chemical == chem && x.NeedsCheck && x.Override == null) == true;
            sb.AppendLine($"   บันทึกครบ {done} · ยังไม่ครบ {notDone.Count} · ไม่มีบันทึก {noRec.Count}");
            foreach (var s in notDone) sb.AppendLine($"   ⚠ {s}");
            foreach (var s in noRec) sb.AppendLine($"   ⚠ {s} — ยังไม่มีบันทึก (ไม่นับในยอด)");
            pending |= notDone.Count > 0 || noRec.Count > 0;
        }
        if (sb.Length == 0) return ("", false);
        return ($"ตัดยอดวันที่ {day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} (นับตามวันชั่งเข้า)\n\n{sb.ToString().TrimEnd()}", pending);
    }

    /// <summary>
    /// ยืนยันตัดยอดของวันแล้วส่ง Aquadat (time 24:00) ด้วย token ที่ได้จากการ login ในหน้าจอ
    /// ส่งยอด final ของทุกคู่ถังในการตัดยอดวันนั้น (รวม 0 เพื่อทับค่าที่เคยส่ง) — ไม่มีการส่งอัตโนมัติ
    /// </summary>
    public async Task<(int Sent, string? Error)> ConfirmDayAsync(DateTime day)
    {
        if (_pushing) return (0, "กำลังส่งอยู่");
        if (!AqLoggedIn) return (0, "ยังไม่ได้เข้าสู่ระบบ Aquadat");
        var cut = CutOf(day);
        if (cut.Configs.Count == 0) return (0, "ไม่มียอดของวันนี้");
        var rows = cut.Configs.Select(x => new OutboxRow
        {
            Date = cut.Key, ConfigId = x.ConfigId, Value = Math.Round(x.Final, 3), Time = "24:00", TransType = "", FlagValue = "0",
        }).ToList();
        _pushing = true;
        try
        {
            var body = Cfg.UseAquadat2
                ? await Aquadat2Client.PushAsync(_aq2!, rows)
                : await AquadatClient.PushAsync(_http, Cfg.AquadatUrl, AqToken, rows);
            Outbox.SaveConfirm(cut.Key, cut.Sig, body);
            Store.Audit("daycut-confirm", new
            {
                day = cut.Key, sent = rows.Select(r => new { r.ConfigId, r.Value }),
                overrides = cut.Configs.Where(x => x.Override != null).Select(x => new { x.ConfigId, x.Override, x.OverrideNote, auto = x.Auto }),
                pending = cut.Pending.Select(p => new { p.Ticket1, p.Reason }),
            });
            return (rows.Count, null);
        }
        catch (Aquadat2AuthException ex)
        {
            AqLogout();                        // refresh ไม่ได้แล้ว → ให้ login ใหม่
            return (0, ex.Message);
        }
        catch (Exception ex)
        {
            // token อาจหมดอายุ → ให้ login ใหม่
            if (!Cfg.UseAquadat2 && (ex.Message.Contains("401") || ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase))) AqToken = "";
            return (0, ex.Message);
        }
        finally
        {
            _pushing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>login แบบผู้ใช้กรอกเอง (POST ครั้งเดียว) — token อยู่ในหน่วยความจำเท่านั้น</summary>
    public async Task<(bool Ok, string Message)> AqLoginAsync(string user, string password)
    {
        if (Cfg.UseAquadat2)
        {
            AqLogout();
            var (session, msg) = await Aquadat2Login.LoginAsync(Cfg.Aquadat2BaseUrl, Cfg.Aquadat2Domain, user, password);
            _aq2 = session;
            Changed?.Invoke();
            return (session != null, session != null ? $"เข้าสู่ระบบแล้ว (บัญชี AD {session.User})" : msg);
        }
        var res = await AquadatLogin.LoginAsync(Cfg.AquadatEnrollUrl, user, password);
        AqToken = res.Ok ? res.Token : "";
        Changed?.Invoke();
        return (res.Ok, res.Ok ? $"เข้าสู่ระบบแล้ว ({user})" : res.Message);
    }

    /// <summary>ทิ้ง token/เซสชันในหน่วยความจำ (ไม่มีอะไรบันทึกลงดิสก์)</summary>
    public void AqLogout()
    {
        AqToken = "";
        var s = _aq2;
        _aq2 = null;
        s?.Dispose();
    }
}
