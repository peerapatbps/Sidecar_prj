using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TruckSidecar.Ui;

/// <summary>
/// ส่วนล่างของแท็บ ALUM/PACL: รายการรถรอบันทึก + ฟอร์ม "บันทึกการรับ" (เหมือนกันทั้งสองแท็บ ต่างกันแค่ชุดถัง)
/// ทะเบียน/Track ID มาจากตาชั่งเท่านั้น (ตรวจจับอัตโนมัติ หรือปุ่ม "ดึงจากตาชั่ง")
/// </summary>
internal sealed class ReceivePanel
{
    readonly SidecarController _c;
    readonly Func<Window> _owner;
    public string Chemical { get; }
    readonly IReadOnlyList<TankConfig> _tanks;

    public UIElement QueueCard { get; }
    public UIElement FormCard { get; }

    UnloadRecord? _editing;
    ScaleRow? _picked;
    string _pickedSource = "";
    bool _dirty, _loading, _saving;
    /// <summary>ช่องที่จะวาง cursor เมื่อหน้าต่างถูก activate (ตั้งตอนรถเข้า/ออกขณะพนักงานอยู่ในโปรแกรมชั่ง)</summary>
    TextBox? _focusTarget;

    readonly StackPanel _queue = new();
    readonly TextBlock _queueTitle = Kit.T("รถรอบันทึก", 15, FontWeights.Bold);
    readonly TextBlock _formTitle = Kit.T("บันทึกการรับ", 15, FontWeights.Bold);
    readonly TextBlock _formSub = Kit.T("", 11.5, null, "Muted");
    readonly StackPanel _steps = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
    readonly TextBox _plate = Kit.Field(true), _track = Kit.Field(true), _sg = Kit.Field(), _temp = Kit.Field(),
                     _book = Kit.Field(), _billNo = Kit.Field();
    readonly TextBlock _scaleInfo = Kit.T("", 12, null, "Ink2", new Thickness(2, 0, 0, 10));
    readonly WrapPanel _pills = new();
    readonly Dictionary<string, ToggleButton> _pillByTank = new();
    readonly StackPanel _levelRows = new();
    readonly Grid _levelHead;
    readonly Dictionary<string, (TextBox Before, TextBox After, TextBlock Vol)> _rows = new();
    readonly TextBlock _missing = Kit.T("", 12, FontWeights.SemiBold, "Orange", new Thickness(2, 6, 0, 10));
    readonly Button _btnSave = new() { Content = "บันทึก", Style = Kit.S("PrimaryButton") };

    // ประเภทเที่ยว: รับสาร / ล้างถัง (นำออก) — ล้างถังไม่ต้องกรอก ถ.พ./อุณหภูมิ/ใบนำส่ง
    string _kind = UnloadRecord.KindReceive;
    readonly RadioButton _kindReceive = new() { Content = "รับสาร", GroupName = "", IsChecked = true };
    readonly RadioButton _kindWash = new() { Content = "ล้างถัง (นำออก)" };
    UIElement _docsRow1 = null!, _docsRow2 = null!;
    readonly TextBlock _tankLabel = Kit.T("", 11.5, FontWeights.SemiBold, "Muted", new Thickness(4, 2, 0, 5));
    readonly TextBlock _hBefore = Kit.T("", 11.5, FontWeights.SemiBold, "Muted"), _hAfter = Kit.T("", 11.5, FontWeights.SemiBold, "Muted"),
                       _hVol = Kit.T("", 11.5, FontWeights.SemiBold, "Muted");
    readonly Border _kindHint = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBlock _kindHintText = Kit.T("", 12, FontWeights.SemiBold, "Orange");

    /// <summary>เปิด record ของสารเคมีอื่นที่แท็บของมัน (กันเปิด record เดียวกันซ้อน 2 ฟอร์ม)</summary>
    readonly Action<UnloadRecord> _openInOwnTab;

    public ReceivePanel(SidecarController c, string chemical, Func<Window> owner, Action<UnloadRecord> openInOwnTab)
    {
        _c = c;
        _owner = owner;
        _openInOwnTab = openInOwnTab;
        Chemical = chemical;
        _tanks = c.TanksOf(chemical);

        // ── การ์ดรายการรถรอบันทึก / ตั๋วค้าง ──
        var q = new StackPanel();
        q.Children.Add(new Grid { Children = { _queueTitle }, Margin = new Thickness(0, 0, 0, 6) });
        q.Children.Add(_queue);
        QueueCard = Kit.Card(q);

        // ── การ์ดฟอร์ม ──
        var f = new StackPanel();
        var pick = new Button { Content = "ดึงจากตาชั่ง", Style = Kit.S("GlassButton"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12 };
        pick.Click += async (_, _) => await PickFromScaleAsync();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Children = { _formTitle, _formSub } };
        head.Children.Add(titles);
        Grid.SetColumn(pick, 1);
        pick.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(pick);
        f.Children.Add(head);
        f.Children.Add(_steps);

        // ── ประเภทเที่ยว (segmented เหมือนแท็บ) ──
        var kindGroup = $"kind-{chemical}";
        foreach (var rb in new[] { _kindReceive, _kindWash })
        {
            rb.Style = Kit.S("Segment");
            rb.GroupName = kindGroup;
            System.Windows.Automation.AutomationProperties.SetName(rb, (string)rb.Content);
        }
        _kindReceive.Checked += (_, _) => SetKind(UnloadRecord.KindReceive, user: true);
        _kindWash.Checked += (_, _) => SetKind(UnloadRecord.KindWash, user: true);
        f.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x76, 0x76, 0x80)),
            Child = new UniformGrid { Rows = 1, Children = { _kindReceive, _kindWash } },
        });

        f.Children.Add(Kit.Two(Kit.Labeled("ทะเบียนรถ", _plate), Kit.Labeled("Track ID", _track)));
        f.Children.Add(_scaleInfo);

        // น้ำหนักออกมากกว่าเข้า → เสนอให้เป็นเที่ยวล้างถัง (ผู้ใช้ยืนยันเอง ไม่สลับให้อัตโนมัติ)
        var toWash = new Button { Content = "เป็นเที่ยวล้างถัง", Style = Kit.S("GlassButton"), Padding = new Thickness(10, 3, 10, 3), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        toWash.Click += (_, _) => _kindWash.IsChecked = true;
        _kindHint.Background = new SolidColorBrush(Kit.WithAlpha(Kit.B("OrangeDot"), 0x22));
        _kindHint.Child = new StackPanel { Children = { _kindHintText, toWash } };
        _kindHint.Visibility = Visibility.Collapsed;
        f.Children.Add(_kindHint);

        _docsRow1 = Kit.Two(Kit.Labeled("ถ.พ.", _sg), Kit.Labeled("อุณหภูมิ (°C)", _temp));
        _docsRow2 = Kit.Two(Kit.Labeled("ใบนำส่ง เล่มที่", _book), Kit.Labeled("ใบนำส่ง เลขที่", _billNo));
        f.Children.Add(_docsRow1);
        f.Children.Add(_docsRow2);

        f.Children.Add(_tankLabel);
        foreach (var t in _tanks)
        {
            var pill = new ToggleButton { Content = t.Name, Style = Kit.S("TankPill") };
            pill.Checked += (_, _) => { if (!_loading) { AddRow(t.Name, null, null); _dirty = true; UpdateMissing(); } };
            pill.Unchecked += (_, _) => { if (!_loading) { RemoveRow(t.Name); _dirty = true; UpdateMissing(); } };
            _pillByTank[t.Name] = pill;
            _pills.Children.Add(pill);
        }
        f.Children.Add(_pills);

        _levelHead = LevelGrid(Kit.T("ถัง", 11.5, FontWeights.SemiBold, "Muted"), _hBefore, _hAfter, _hVol);
        _levelHead.Margin = new Thickness(4, 4, 0, 4);
        f.Children.Add(_levelHead);
        f.Children.Add(_levelRows);
        f.Children.Add(_missing);

        var btnNew = new Button { Content = "เริ่มใหม่", Style = Kit.S("GlassButton") };
        btnNew.Click += (_, _) => { if (ConfirmDiscard()) NewRecord(); };
        _btnSave.Click += async (_, _) => await SaveAsync();
        f.Children.Add(Kit.Two(btnNew, _btnSave, 10));
        FormCard = Kit.Card(f);

        foreach (var box in new[] { _sg, _temp, _book, _billNo })
            box.TextChanged += (_, _) => { if (!_loading) _dirty = true; UpdateMissing(); };

        NewRecord();
    }

    /// <summary>เปลี่ยนประเภทเที่ยว: ซ่อน/แสดงช่องเอกสาร + เปลี่ยนป้ายตารางระดับ (user = ผู้ใช้กดเอง → นับว่าแก้ไข)</summary>
    void SetKind(string kind, bool user)
    {
        if (user && _loading) return;
        _kind = kind;
        bool wash = kind == UnloadRecord.KindWash;
        if ((_kindWash.IsChecked == true) != wash) { var l = _loading; _loading = true; (wash ? _kindWash : _kindReceive).IsChecked = true; _loading = l; }
        _docsRow1.Visibility = _docsRow2.Visibility = wash ? Visibility.Collapsed : Visibility.Visible;
        _tankLabel.Text = wash ? "ถังที่ล้าง (เลือกได้หลายถัง)" : "ถังที่ส่ง (เลือกได้หลายถัง)";
        _hBefore.Text = wash ? "ก่อนล้าง m³" : "ก่อนเติม m³";
        _hAfter.Text = wash ? "หลังล้าง m³" : "หลังเติม m³";
        _hVol.Text = wash ? "นำออก" : "เติมเข้า";
        foreach (var (_, (b, a, vol)) in _rows)
        {
            TryNum(b.Text, out var bv, out _);
            TryNum(a.Text, out var av, out _);
            vol.Text = Volume(bv, av);
            vol.Foreground = Kit.B(wash ? "Orange" : "Green");
        }
        if (user) _dirty = true;
        UpdateScaleInfo();
        UpdateMissing();
    }

    // ───────────────────────── สถานะให้ MainWindow ─────────────────────────

    /// <summary>ไม่มีอะไรที่พนักงานพิมพ์ค้างไว้ → เติมรถคันใหม่ทับได้</summary>
    public bool IsIdle => _editing == null && !_dirty;
    /// <summary>ฟอร์มว่างจริง (ไม่มีรถเข้าที่ระบบเติมรอไว้ด้วย) → เปิดรายการรถชั่งออกแทนได้</summary>
    public bool IsBlank => IsIdle && _picked == null;
    public string? EditingId => _editing?.Id;
    public int QueueCount { get; private set; }

    // ───────────────────────── โหลด/สร้าง ─────────────────────────

    public void NewRecord()
    {
        _loading = true;
        _focusTarget = null;
        _editing = null;
        _picked = null;
        _pickedSource = "";
        foreach (var t in new[] { _plate, _track, _sg, _temp, _book, _billNo }) t.Clear();
        foreach (var p in _pillByTank.Values) p.IsChecked = false;
        _levelRows.Children.Clear();
        _rows.Clear();
        _formTitle.Text = "บันทึกการรับ";
        _formSub.Text = "รอรถขึ้นตาชั่ง — ระบบจะเติมทะเบียนให้อัตโนมัติ";
        SetKind(UnloadRecord.KindReceive, user: false);
        _loading = false;
        _dirty = false;
        UpdateScaleInfo();
        UpdateMissing();
    }

    /// <summary>เติมรถจากตาชั่ง (ยังไม่บันทึก) — auto = ตรวจจับอัตโนมัติ (ไม่นับว่าแก้ไข จึงถูกแทนได้ถ้ารถใหม่มา)</summary>
    public void LoadArrival(ScaleRow row, bool auto)
    {
        NewRecord();
        _picked = row;
        _pickedSource = auto ? "auto" : "picker";
        _loading = true;
        _plate.Text = row.Truck;
        _track.Text = row.Ticket1Short;
        _loading = false;
        _dirty = !auto;
        _formTitle.Text = "บันทึกการรับ — รถเข้าใหม่";
        _formSub.Text = auto ? "ตรวจพบจากตาชั่งอัตโนมัติ · กรอก ถ.พ./อุณหภูมิ/ใบนำส่ง แล้วเลือกถัง" : "เลือกจากตาชั่ง";
        // โน้ตถังที่พนักงานชั่งจดไว้ใน REMARK (ถ้ามี) — ช่วยเตือน ไม่เลือกให้อัตโนมัติ
        var remark = string.Join(" ", new[] { row.Remark1, row.Remark2 }.Where(s => s.Length > 0));
        if (remark.Length > 0) _formSub.Text += $"\nหมายเหตุตาชั่ง: {remark}";
        UpdateScaleInfo();
        UpdateMissing();
        Kit.FadeIn(FormCard);
        if (!auto) FocusEntry();     // auto: MainWindow เรียกเองเมื่อสลับมาแท็บนี้ (ไม่แย่งแท็บที่พนักงานพิมพ์อยู่)
    }

    // ───────────────────────── เลื่อนหน้า / focus ─────────────────────────
    // รถหลายคัน → รายการ "รถรอบันทึก" ยาวขึ้น ดันฟอร์มลงไปไกล จึงเลื่อนให้เอง

    /// <summary>
    /// รถเข้า (ข้อมูลจากตาชั่ง) → เลื่อนหน้าแท็บลงล่างสุด แล้ววาง cursor ที่ ถ.พ.
    /// หน้าต่างไม่ active (พนักงานอยู่ในโปรแกรมชั่ง) → ไม่แย่ง keyboard แค่จำไว้ แล้ว focus เมื่อกลับมาที่ sidecar
    /// </summary>
    public void FocusEntry() => FocusLater(_sg);

    /// <summary>
    /// รถชั่งออก → วาง cursor ที่ระดับถังช่องแรกที่ยังว่าง (ปกติคือ ระดับหลังเติม)
    /// คืน false ถ้าฟอร์มไม่ได้เปิด record นี้ หรือกรอกระดับครบแล้ว
    /// </summary>
    public bool FocusLevels(UnloadRecord r)
    {
        if (_editing?.Id != r.Id) return false;
        var box = _rows.Values.SelectMany(x => new[] { x.Before, x.After }).FirstOrDefault(t => t.Text.Trim().Length == 0);
        if (box == null) return false;
        FocusLater(box);
        return true;
    }

    void FocusLater(TextBox box)
    {
        _focusTarget = box;
        AfterLayout(sv =>
        {
            if (box == _sg) sv.ScrollToEnd();
            else box.BringIntoView();
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(box), box);
            OnWindowActivated();
        });
    }

    /// <summary>หน้าต่าง sidecar ถูก activate → focus ช่องที่ค้างไว้จาก FocusEntry/FocusLevels</summary>
    public void OnWindowActivated()
    {
        // ใช้ foreground จริงของ Windows แทน Window.IsActive (ค้างเป็น true ได้ แล้ว cursor จะไปตกตอนหน้าต่างยังไม่ active)
        if (_focusTarget is { } t && t.IsVisible && Window.GetWindow(t) is { } w
            && Native.GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(w).Handle && t.Focus())
            _focusTarget = null;
    }

    void ScrollToTop() => AfterLayout(sv => sv.ScrollToTop());

    /// <summary>รอให้จัด layout ก่อน (แท็บเพิ่งสลับ/ฟอร์มเพิ่งเปลี่ยน) แล้วทำกับ ScrollViewer ของหน้าแท็บ</summary>
    void AfterLayout(Action<ScrollViewer> act) => FormCard.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        DependencyObject? d = FormCard;
        while (d != null && d is not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        if (d is ScrollViewer sv) act(sv);
    });

    public void OpenRecord(UnloadRecord r)
    {
        _loading = true;
        _focusTarget = null;
        _editing = r;
        _picked = null;
        _pickedSource = "";
        _plate.Text = r.Plate;
        _track.Text = r.TrackId.TrimStart('0');
        _sg.Text = r.Sg?.ToString(CultureInfo.InvariantCulture) ?? "";
        _temp.Text = r.TempC?.ToString(CultureInfo.InvariantCulture) ?? "";
        _book.Text = r.BillBook;
        _billNo.Text = r.BillNo;
        SetKind(r.Kind, user: false);
        foreach (var p in _pillByTank.Values) p.IsChecked = false;
        _levelRows.Children.Clear();
        _rows.Clear();
        foreach (var fill in r.Fills)
        {
            if (_pillByTank.TryGetValue(fill.Tank, out var p)) p.IsChecked = true;
            AddRow(fill.Tank, fill.LevelBefore, fill.LevelAfter);
        }
        _formTitle.Text = "บันทึกการรับ — แก้ไข";
        _formSub.Text = $"ฉบับที่ {r.Version} · บันทึกล่าสุด {r.UpdatedAt:dd/MM HH:mm}";
        _loading = false;
        _dirty = false;
        UpdateScaleInfo();
        UpdateMissing();
        Kit.FadeIn(FormCard);
    }

    /// <summary>เปิด record (ถามก่อนถ้ามีข้อมูลค้างพิมพ์) — ถ้าเปิดอยู่แล้วไม่ทำอะไร</summary>
    public void TryOpen(UnloadRecord r)
    {
        if (r.Id != _editing?.Id && ConfirmDiscard()) OpenRecord(r);
    }

    /// <summary>record ที่เปิดอยู่ได้ผลชั่งออก → อัปเดตเฉพาะข้อมูลตาชั่ง ไม่ทับสิ่งที่พนักงานกำลังพิมพ์</summary>
    public void OnWeighedOut(UnloadRecord r)
    {
        if (_editing?.Id != r.Id) return;
        _editing = r;
        UpdateScaleInfo();
        UpdateMissing();
    }

    // ───────────────────────── รายการรถรอบันทึก ─────────────────────────

    /// <summary>
    /// ฟอร์มที่ระบบเติมให้อัตโนมัติ (พนักงานยังไม่พิมพ์) แต่ตั๋วหายไปจากตาชั่งแล้ว (ถูกยกเลิกในโปรแกรมชั่ง)
    /// → ล้างฟอร์ม คืนชื่อรถเพื่อแจ้งเตือน; ถ้าพนักงานพิมพ์ไปแล้วจะไม่ล้าง (crosscheck ตอนบันทึกจะ block เอง)
    /// </summary>
    public string? DropIfCancelled()
    {
        if (_picked is not { } p || _dirty || _editing != null || _pickedSource != "auto") return null;
        if (_c.Arrivals.Any(a => a.Row.Ticket1 == p.Ticket1) || _c.Store.FindByTrackId(p.Ticket1) != null) return null;
        NewRecord();
        return $"{p.Truck} #{p.Ticket1Short}";
    }

    string _queueSig = "";

    public void RefreshQueue()
    {
        var arrivals = _c.Arrivals.Where(a => a.Chemical == Chemical).ToList();
        // งานคงค้าง (ตามวันชั่งเข้า): record ไม่ครบ + รถที่ชั่งออกแล้วแต่ไม่มีบันทึกย้อนหลังหลายวัน
        // (ชิปรถวันนี้มาจาก arrivals อยู่แล้ว จึงตัดตั๋วพวกนั้นออก — กันชิปหายเมื่อข้ามเที่ยงคืน)
        var pendingAll = _c.Pending(Chemical, arrivals.Select(a => a.Row.Ticket1).ToHashSet());
        var pending = pendingAll.Where(p => p.Record != null).ToList();
        var unrecorded = pendingAll.Where(p => p.Record == null && p.Row != null).ToList();
        // บันทึกครบของวันนี้ — เปิดแก้ไขได้ (กรอกผิดหลังบันทึก)
        var done = _c.Store.All
            .Where(r => r.IsComplete && r.ChemicalOr(_c.Cfg.Tanks) == Chemical && (r.WeighOutAt ?? r.CreatedAt).Date == DateTime.Today)
            .OrderByDescending(r => r.WeighOutAt ?? r.CreatedAt).ToList();
        HashSet<string> sent;
        try { sent = _c.Outbox.DailyTrips(0).Where(t => t.Synced).Select(t => t.Track).ToHashSet(); }
        catch { sent = new(); }

        // ฟอร์มที่กรอกค้างไว้ แต่รถชั่งออกในโปรแกรมชั่งแล้ว → ตามผลชั่งออก โดยไม่ล้างสิ่งที่พิมพ์
        if (_picked is { WeighedOut: false } open &&
            arrivals.FirstOrDefault(a => a.MissedWeighIn && a.Row.Ticket1 == open.Ticket1) is { } wo)
            FollowWeighOut(wo.Row);

        // สร้างชิปใหม่เฉพาะเมื่อเนื้อหาเปลี่ยน — RefreshAll ถูกเรียกทุก 3 วิ (ทุกรอบตรวจตาชั่ง)
        // ถ้าสร้างใหม่ทุกรอบ ปุ่มจะถูกแทนที่ระหว่างกดเมาส์ (กดลงปุ่มเก่า ปล่อยบนปุ่มใหม่) → คลิกหายแบบสุ่ม
        var sig = string.Join(";", arrivals.Select(a => $"a{a.Row.Ticket1}{a.MissedWeighIn}{a.Row.WeighOutAt:HHmm}"))
                + "|" + string.Join(";", pending.Select(p => $"p{p.Record!.Id}v{p.Record.Version}"))
                + "|" + string.Join(";", unrecorded.Select(p => $"u{p.Ticket1}"))
                + "|" + string.Join(";", done.Select(r => $"d{r.Id}v{r.Version}{sent.Contains(r.TrackId)}"))
                + "|" + _editing?.Id + "|" + _picked?.Ticket1;
        if (sig == _queueSig) return;
        _queueSig = sig;

        _queue.Children.Clear();
        int n = 0;
        foreach (var a in arrivals)
        {
            var r = a.Row;
            // รถที่ชั่งเข้าเมื่อวาน (ภายใน 24 ชม.) แสดงวันที่ด้วย ไม่ให้สับสนกับรถของวันนี้
            string inAt = r.WeighInAt is { } wi && wi.Date != DateTime.Today
                ? $"{wi:dd/MM HH:mm}" + (wi.Date == DateTime.Today.AddDays(-1) ? " (เมื่อวาน)" : "")
                : $"{r.WeighInAt:HH:mm}";
            string sub = a.MissedWeighIn ? $"ชั่งออกแล้ว {r.WeighOutAt:HH:mm} แต่ยังไม่มีบันทึก" : $"ชั่งเข้า {inAt} · {r.W1:#,0} กก. · รอบันทึก";
            _queue.Children.Add(QueueRow(a.MissedWeighIn ? "RedDot" : "Accent", $"{r.Truck}  #{r.Ticket1Short}", sub,
                () =>
                {
                    // คันที่เปิดอยู่แล้ว → ไม่โหลดใหม่ (LoadArrival ล้างฟอร์ม = ข้อมูลที่พิมพ์หาย) แค่ตามผลชั่งออก
                    if (_picked?.Ticket1 == r.Ticket1) { if (r.WeighedOut && !_picked.WeighedOut) FollowWeighOut(r); }
                    else if (ConfirmDiscard()) LoadArrival(r, false);
                },
                () =>
                {
                    if (GlassDialog.Confirm(_owner(), $"ซ่อนรถ {r.Truck} #{r.Ticket1Short}\nว่าไม่ใช่รถสารเคมี?", "ซ่อน"))
                    {
                        if (_picked?.Ticket1 == r.Ticket1) NewRecord();
                        _c.Dismiss(r);
                    }
                }, _picked?.Ticket1 == r.Ticket1));
            n++;
        }
        foreach (var p in unrecorded)
        {
            var r = p.Row!;
            _queue.Children.Add(QueueRow("RedDot", $"{r.Truck}  #{r.Ticket1Short}",
                $"{p.Day:dd/MM} · ชั่งออก {r.WeighOutAt:dd/MM HH:mm} แต่ยังไม่มีบันทึก (ยังไม่นับในยอด)",
                () => { if (_picked?.Ticket1 != r.Ticket1 && ConfirmDiscard()) LoadArrival(r, false); },
                () =>
                {
                    if (GlassDialog.Confirm(_owner(), $"ซ่อนรถ {r.Truck} #{r.Ticket1Short}\nว่าไม่ใช่รถสารเคมี?", "ซ่อน"))
                    {
                        if (_picked?.Ticket1 == r.Ticket1) NewRecord();
                        _c.Dismiss(r);
                    }
                }, _picked?.Ticket1 == r.Ticket1));
            n++;
        }
        foreach (var p in pending)
        {
            var r = p.Record!;
            var miss = r.Missing;
            _queue.Children.Add(QueueRow("OrangeDot", $"{r.Plate}  #{r.TrackId.TrimStart('0')}" + (r.IsWash ? "  · ล้างถัง" : ""),
                $"{p.Day:dd/MM} · ขาด {miss.Count}: {string.Join(", ", miss.Take(3))}{(miss.Count > 3 ? "…" : "")}" + (p.Counted ? " · ยอดประมาณการ" : ""),
                () => { if (r.Id != _editing?.Id && ConfirmDiscard()) OpenRecord(r); }, null, r.Id == _editing?.Id));
            n++;
        }
        QueueCount = n;
        _queueTitle.Text = n == 0 ? "รถรอบันทึก" : $"รถรอบันทึก ({n})";
        if (n == 0) _queue.Children.Add(Kit.T("ไม่มีรถค้าง ✓", 12, null, "Muted", new Thickness(2, 0, 0, 0)));

        // ไม่นับใน QueueCount — badge/ปุ่ม "ไปส่งยอด" นับเฉพาะรถที่ยังต้องบันทึก
        if (done.Count > 0)
            _queue.Children.Add(Kit.T($"บันทึกครบวันนี้ ({done.Count}) · แตะเพื่อแก้ไข", 12.5, FontWeights.SemiBold, "Muted", new Thickness(2, 10, 0, 6)));
        foreach (var r in done)
        {
            _queue.Children.Add(QueueRow("GreenDot", $"{r.Plate}  #{r.TrackId.TrimStart('0')}" + (r.IsWash ? "  · ล้างถัง" : ""),
                $"ชั่งออก {r.WeighOutAt:HH:mm} · {Kit.NetText(r.NetKg)} · {(sent.Contains(r.TrackId) ? "ส่ง Aquadat แล้ว" : "ค้างส่ง")}",
                () => { if (r.Id != _editing?.Id && ConfirmDiscard()) OpenRecord(r); }, null, r.Id == _editing?.Id));
        }
    }

    /// <summary>รถที่กรอกค้างไว้ชั่งออกแล้ว → อัปเดตข้อมูลตาชั่งในฟอร์ม ไม่แตะช่องที่พนักงานพิมพ์</summary>
    void FollowWeighOut(ScaleRow row)
    {
        _picked = row;
        _formSub.Text = $"รถชั่งออกแล้ว {row.WeighOutAt:HH:mm} — กรอกให้ครบแล้วกดบันทึกได้เลย";
        UpdateScaleInfo();
        UpdateMissing();
    }

    UIElement QueueRow(string dot, string title, string sub, Action open, Action? dismiss, bool active = false)
    {
        var g = new Grid { Margin = new Thickness(10, 6, 6, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var d = Kit.Dot(dot, 9);
        d.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(d);
        var text = new StackPanel { Children = { Kit.T(title, 13, FontWeights.SemiBold), Kit.T(sub, 11.5, null, "Muted") } };
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        var btn = new Button
        {
            Style = Kit.S("FlatButton"), Content = g, Margin = new Thickness(0, 0, 0, 5),
            Background = new SolidColorBrush(active ? Kit.WithAlpha(Kit.B("Accent"), 0x22) : Color.FromArgb(0x8C, 255, 255, 255)),
        };
        btn.Click += (_, _) => open();
        if (dismiss != null)
        {
            var x = new Button
            {
                Content = "✕", Style = Kit.S("FlatButton"), Width = 26, Height = 26, FontSize = 11,
                Foreground = Kit.B("Muted"), ToolTip = "ไม่ใช่รถสารเคมี (ซ่อน)", HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            x.Content = new TextBlock { Text = "✕", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Kit.B("Muted") };
            x.Click += (_, e) => { e.Handled = true; dismiss(); };
            Grid.SetColumn(x, 2);
            g.Children.Add(x);
        }
        return btn;
    }

    // ───────────────────────── แถวระดับถัง ─────────────────────────

    static Grid LevelGrid(UIElement a, UIElement b, UIElement c, UIElement d)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
        UIElement[] cells = { a, b, c, d };
        for (int i = 0; i < 4; i++)
        {
            if (cells[i] is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(i is 1 or 2 ? 3 : 0, 0, 3, 0); }
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    void AddRow(string tank, double? before, double? after)
    {
        if (_rows.ContainsKey(tank)) return;
        var b = Kit.Field();
        var a = Kit.Field();
        b.Text = before?.ToString(CultureInfo.InvariantCulture) ?? "";
        a.Text = after?.ToString(CultureInfo.InvariantCulture) ?? "";
        var vol = Kit.T(Volume(before, after), 13, FontWeights.SemiBold, "Green");
        void Changed()
        {
            TryNum(b.Text, out var bv, out _);
            TryNum(a.Text, out var av, out _);
            vol.Text = Volume(bv, av);
            if (!_loading) _dirty = true;
            UpdateMissing();
        }
        b.TextChanged += (_, _) => Changed();
        a.TextChanged += (_, _) => Changed();
        _rows[tank] = (b, a, vol);

        UIElement label = Kit.T(tank, 13, FontWeights.SemiBold);
        if (_tanks.All(t => t.Name != tank))
        {
            // ถังของสารอื่น (มาจาก record เก่า v1) — ไม่มี pill ให้กดออก จึงมีปุ่มลบในแถว
            var del = new Button
            {
                Style = Kit.S("FlatButton"), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"{tank} ไม่ใช่ถัง {Chemical} — กดเพื่อลบแถวนี้",
                Content = Kit.T($"✕ {tank}", 12.5, FontWeights.SemiBold, "Red"),
            };
            System.Windows.Automation.AutomationProperties.SetName(del, $"ลบ {tank}");
            del.Click += (_, _) => { RemoveRow(tank); _dirty = true; UpdateMissing(); };
            label = del;
        }
        var row = LevelGrid(label, b, a, vol);
        row.Tag = tank;
        row.Margin = new Thickness(4, 0, 0, 6);
        // เรียงตามลำดับถังใน config
        int order = _tanks.ToList().FindIndex(t => t.Name == tank);
        int at = _levelRows.Children.Cast<FrameworkElement>().Count(e => _tanks.ToList().FindIndex(t => t.Name == (string)e.Tag) < order);
        _levelRows.Children.Insert(at, row);
        _levelHead.Visibility = Visibility.Visible;
        if (!_loading) Kit.FadeIn(row);
    }

    void RemoveRow(string tank)
    {
        if (!_rows.Remove(tank)) return;
        var el = _levelRows.Children.Cast<FrameworkElement>().FirstOrDefault(e => (string)e.Tag == tank);
        if (el != null) _levelRows.Children.Remove(el);
    }

    // ───────────────────────── อ่านฟอร์ม / ตรวจ ─────────────────────────

    static bool TryNum(string s, out double? v, out bool bad)
    {
        v = null;
        bad = false;
        s = s.Trim().Replace(',', '.');
        if (s.Length == 0) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { v = d; return true; }
        bad = true;
        return false;
    }

    static string Volume(double? before, double? after) =>
        before is { } b && after is { } a ? (a - b).ToString("0.##") : "";

    /// <summary>อ่านค่าจากฟอร์มเป็น record (สำเนา ยังไม่บันทึก)</summary>
    UnloadRecord Collect(List<string> errors)
    {
        var r = _editing?.Clone() ?? new UnloadRecord();
        // ทะเบียน/Track ID/น้ำหนัก มาจากตาชั่งเท่านั้น ไม่อ่านจากช่องข้อความ
        if (_picked != null)
        {
            r.ApplyScale(_picked);
            r.Chemical = _c.Cfg.ChemicalOf(_picked.Product) ?? Chemical;
            r.Source = _pickedSource;
        }
        if (r.Chemical.Length == 0 && r.TrackId.Length > 0) r.Chemical = Chemical;
        r.Kind = _kind;
        if (r.IsWash)
        {
            // ล้างถังไม่มีเอกสารรับสาร — ไม่เก็บค่าที่อาจค้างอยู่ในช่องที่ซ่อนไว้
            r.BillBook = r.BillNo = "";
            r.Sg = r.TempC = null;
        }
        else
        {
            r.BillBook = _book.Text.Trim();
            r.BillNo = _billNo.Text.Trim();
            if (!TryNum(_sg.Text, out var sg, out _)) errors.Add("ถ.พ. ต้องเป็นตัวเลข");
            if (!TryNum(_temp.Text, out var temp, out _)) errors.Add("อุณหภูมิต้องเป็นตัวเลข");
            r.Sg = sg;
            r.TempC = temp;
        }

        var old = _editing?.Fills.ToDictionary(f => f.Tank) ?? new();
        r.Fills = new List<FillEntry>();
        // ถังของแท็บนี้ตามลำดับ config ก่อน แล้วตามด้วยถังสารอื่นที่ติดมากับ record เก่า (v1)
        // — ไม่ทิ้งเงียบๆ ให้ crosscheck block และพนักงานกด ✕ ลบเอง
        var order = _tanks.Select(t => t.Name).Where(_rows.ContainsKey)
            .Concat(_rows.Keys.Where(k => _tanks.All(t => t.Name != k)));
        foreach (var name in order)
        {
            var (bBox, aBox, _) = _rows[name];
            if (!TryNum(bBox.Text, out var before, out _)) errors.Add($"ระดับก่อนเติม {name} ต้องเป็นตัวเลข");
            if (!TryNum(aBox.Text, out var after, out _)) errors.Add($"ระดับหลังเติม {name} ต้องเป็นตัวเลข");
            DateTime? afterAt = null;
            if (after != null)
                afterAt = old.TryGetValue(name, out var o) && o.LevelAfter == after && o.AfterRecordedAt != null
                    ? o.AfterRecordedAt
                    : DateTime.Now;
            r.Fills.Add(new FillEntry { Tank = name, LevelBefore = before, LevelAfter = after, AfterRecordedAt = afterAt });
        }
        return r;
    }

    void UpdateMissing()
    {
        if (_loading) return;
        var errors = new List<string>();
        var r = Collect(errors);
        UpdateSteps(r);
        _levelHead.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (errors.Count > 0)
        {
            _missing.Text = string.Join(" · ", errors);
            _missing.Foreground = Kit.B("Red");
            return;
        }
        if (r.TrackId.Length == 0)
        {
            _missing.Text = "";
            return;
        }
        var missing = r.Missing;
        _missing.Text = missing.Count == 0 ? "ข้อมูลครบ ✓" : "ยังขาด: " + string.Join(", ", missing);
        _missing.Foreground = Kit.B(missing.Count == 0 ? "Green" : "Orange");
    }

    void UpdateSteps(UnloadRecord r)
    {
        _steps.Children.Clear();
        (string, bool)[] steps =
        {
            ("ชั่งเข้า", r.StepWeighIn), (r.IsWash ? "เลือกถัง" : "ใบนำส่ง", r.StepDocs), ("ระดับถัง", r.StepLevels),
            ("ชั่งออก", r.StepWeighOut), ("ครบ", r.IsComplete && r.StepWeighIn),
        };
        for (int i = 0; i < steps.Length; i++)
        {
            var (name, done) = steps[i];
            var circle = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                Background = done ? Kit.B("GreenDot") : new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = done ? "✓" : (i + 1).ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold,
                    Foreground = done ? Brushes.White : Kit.B("Muted"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            sp.Children.Add(circle);
            sp.Children.Add(Kit.T(name, 11.5, FontWeights.SemiBold, done ? "Green" : "Muted", new Thickness(4, 0, 0, 0)));
            _steps.Children.Add(sp);
        }
    }

    void UpdateScaleInfo()
    {
        var r = new UnloadRecord();
        if (_picked != null) r.ApplyScale(_picked);
        else if (_editing != null) r = _editing;
        if (r.TrackId.Length == 0)
        {
            _scaleInfo.Text = "";
            _scaleInfo.Visibility = Visibility.Collapsed;
            _kindHint.Visibility = Visibility.Collapsed;
            return;
        }
        _scaleInfo.Visibility = Visibility.Visible;
        var s = $"ชั่งเข้า {r.W1:#,0} กก. ({r.WeighInAt:dd/MM HH:mm})";
        s += r.Ticket2.Length == 0
            ? " · ยังไม่ชั่งออก"
            : $"\nชั่งออก {r.W2:#,0} กก. ({r.WeighOutAt:HH:mm}) · {Kit.NetText(r.NetKg)} · ตั๋วออก {r.Ticket2.TrimStart('0')}";
        _scaleInfo.Text = s;

        // น้ำหนักออก > เข้า แต่ยังเป็น "รับสาร" → เสนอให้เปลี่ยนเป็นล้างถัง (ผู้ใช้ยืนยันเอง)
        bool suggest = r.NetKg < 0 && _kind != UnloadRecord.KindWash;
        _kindHintText.Text = suggest ? $"ชั่งออกหนักกว่าชั่งเข้า ({r.W1:#,0} → {r.W2:#,0} กก.) — เป็นเที่ยวล้างถัง (นำออก) หรือไม่?" : "";
        _kindHint.Visibility = suggest ? Visibility.Visible : Visibility.Collapsed;
    }

    bool ConfirmDiscard() =>
        !_dirty || GlassDialog.Confirm(_owner(), "มีข้อมูลที่ยังไม่ได้บันทึก\nต้องการทิ้งหรือไม่?", "ทิ้งข้อมูล");

    // ───────────────────────── ปุ่ม ─────────────────────────

    async Task PickFromScaleAsync()
    {
        if (string.IsNullOrEmpty(_c.Cfg.MdbPath))
        {
            GlassDialog.Info(_owner(), "ยังไม่ได้ตั้งค่า mdbPath ใน config.json", GlassDialog.Kind.Error);
            return;
        }
        List<ScaleRow> rows;
        try { rows = await ScaleDb.CandidatesAsync(_c.Cfg.MdbPath); }
        catch (Exception ex)
        {
            GlassDialog.Info(_owner(), "อ่านข้อมูลตาชั่งไม่ได้: " + ex.Message, GlassDialog.Kind.Error);
            return;
        }

        var picker = new ScalePickerWindow(_owner(), rows, _c.Store.FindByTrackId, _c.Cfg.ChemicalOf);
        if (picker.ShowDialog() != true || picker.Selected is not { } sel) return;

        // ตั๋วนี้มีบันทึกอยู่แล้ว → เปิดรายการเดิม ไม่สร้างซ้ำ
        if (_c.Store.FindByTrackId(sel.Ticket1) is { } existing)
        {
            var owner = existing.ChemicalOr(_c.Cfg.Tanks);
            if (owner.Length > 0 && owner != Chemical) { _openInOwnTab(existing); return; }
            TryOpen(existing);
            return;
        }
        if (_c.Cfg.ChemicalOf(sel.Product) is { } chem && chem != Chemical)
        {
            GlassDialog.Info(_owner(), $"รถคันนี้เป็น {chem} — ไปบันทึกที่แท็บ {chem}", GlassDialog.Kind.Warn);
            return;
        }
        // บันทึกที่ผูก Track ID ไปแล้วห้ามย้ายไปผูกตั๋วอื่น
        if (_editing != null)
        {
            GlassDialog.Info(_owner(), $"รายการนี้ผูกกับ Track ID {_editing.TrackId.TrimStart('0')} แล้ว เปลี่ยนไม่ได้\nกด \"เริ่มใหม่\" ก่อน");
            return;
        }
        if (!ConfirmDiscard()) return;
        LoadArrival(sel, false);
    }

    async Task SaveAsync()
    {
        if (_saving) return;
        var errors = new List<string>();
        var r = Collect(errors);
        if (errors.Count > 0)
        {
            GlassDialog.Info(_owner(), string.Join("\n", errors), GlassDialog.Kind.Error);
            return;
        }

        _saving = true;
        _btnSave.IsEnabled = false;
        _btnSave.Content = "กำลังตรวจกับตาชั่ง…";
        CheckResult chk;
        try { chk = await _c.CrossCheckAsync(r); }
        finally
        {
            _saving = false;
            _btnSave.IsEnabled = true;
            _btnSave.Content = "บันทึก";
        }

        if (chk.Blocks.Count > 0)
        {
            GlassDialog.Info(_owner(), "บันทึกไม่ได้\n\n" + string.Join("\n", chk.Blocks), GlassDialog.Kind.Error);
            return;
        }
        if (chk.MdbError != null &&
            !GlassDialog.Confirm(_owner(), $"อ่าน data.mdb เพื่อตรวจสอบไม่ได้\n({chk.MdbError})\n\nบันทึกต่อโดยไม่ตรวจกับตาชั่ง?", "บันทึกต่อ"))
            return;
        if (chk.Warnings.Count > 0 &&
            !GlassDialog.Confirm(_owner(), string.Join("\n", chk.Warnings) + "\n\nยืนยันบันทึก?", "บันทึก"))
            return;
        if (_editing is { IsComplete: true } && _c.Outbox.IsSynced(r.TrackId) &&
            !GlassDialog.Confirm(_owner(), $"Track ID {r.TrackId.TrimStart('0')} ส่ง Aquadat ไปแล้ว\nบันทึกแก้ไขแล้วระบบจะคำนวณยอดวันนั้นใหม่และส่งทับ\n\nยืนยันบันทึกแก้ไข?", "บันทึกแก้ไข"))
            return;

        _c.Commit(r);
        // กดบันทึก = ขั้นตอนนั้นเสร็จ → ล้างฟอร์มเสมอ (รายการที่ยังไม่ครบอยู่ใน "รถรอบันทึก" แตะเพื่อทำต่อ)
        // ฟอร์มว่าง = พร้อมรับรถคันถัดไปอัตโนมัติ และไม่ค้างที่ record ที่ครบเองตอนชั่งออก
        NewRecord();
        ScrollToTop();
        var miss = r.Missing;
        _missing.Text = miss.Count == 0
            ? $"บันทึก Track ID {r.TrackId.TrimStart('0')} ({r.Plate}) ครบแล้ว ✓"
            : $"บันทึก Track ID {r.TrackId.TrimStart('0')} ({r.Plate}) แล้ว ✓ — ยังขาด: {string.Join(", ", miss)}\nแตะรายการใน \"รถรอบันทึก\" เพื่อทำต่อ";
        _missing.Foreground = Kit.B("Green");
    }
}
