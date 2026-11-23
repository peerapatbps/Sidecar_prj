using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rect = System.Drawing.Rectangle;

namespace TruckSidecar.Ui;

/// <summary>
/// หน้าต่าง sidecar (WPF, liquid glass): เกาะข้างโปรแกรมชั่ง, แท็บ ALUM / PACL / ส่งยอดสารเคมี
/// ตรรกะอยู่ใน SidecarController — คลาสนี้ดูแลหน้าต่าง/การเกาะ/วาดจอเท่านั้น
/// </summary>
public sealed class MainWindow : Window
{
    readonly SidecarController _c;
    readonly string? _previewDir;
    IntPtr _hwnd;
    bool _allowClose, _nag, _glassOn;
    Rect? _appBar;              // ไม่ใช่ null = กำลังจองแถบขวาของจอ (โปรแกรมชั่งขยายเต็มจอ)
    int _widthPx;

    readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer _watchTimer = new();
    readonly DispatcherTimer _apiTimer = new();
    readonly DispatcherTimer _minuteTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    readonly Border _root = new();
    readonly Border _headerBg = new();
    readonly TextBlock _title = Kit.T("การรับสารเคมี", 18, FontWeights.Bold);
    readonly TextBlock _subtitle = Kit.T("", 11, null, "Muted");
    System.Windows.Shapes.Ellipse _scaleDot = null!, _apiDot = null!;
    TextBlock _scaleText = null!, _apiText = null!;
    readonly List<(RadioButton Seg, TextBlock Badge, Border BadgeBox, ScrollViewer Page)> _tabs = new();
    readonly Dictionary<string, ReceivePanel> _panels = new();
    readonly Dictionary<string, TankBarChart> _charts = new();
    readonly Dictionary<string, TextBlock> _chartNotes = new();
    readonly Dictionary<string, Button> _goSend = new();
    readonly AquadatPanel _aq;
    readonly Border _toast = new();
    readonly TextBlock _toastText = Kit.T("", 13, FontWeights.SemiBold);
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    static readonly Dictionary<string, (Color Top, Color Bottom)> ChemColors = new()
    {
        ["ALUM"] = (Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF)),
        ["PACL"] = (Color.FromRgb(0xDA, 0x8F, 0xFF), Color.FromRgb(0x5E, 0x5C, 0xE6)),
    };

    public MainWindow(SidecarController c, string? previewDir)
    {
        _c = c;
        _previewDir = previewDir;

        Title = "การรับสารเคมี";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = new FontFamily("Leelawadee UI");
        Width = c.Cfg.SidecarWidth;
        Height = previewDir != null ? 1480 : 860;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = previewDir != null ? -6000 : 0;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _aq = new AquadatPanel(c);
        BuildUi();

        // เปิดปกติ = ลบธง "admin สั่งปิด" เพื่อให้ watchdog กลับมาคุ้มครอง sidecar ตัวนี้
        if (previewDir == null) ClearStopMarker();

        _c.Changed += RefreshAll;
        _c.NewPrimaryArrival += OnArrival;
        _c.RecordWeighedOut += OnWeighedOut;
        _c.ScaleConflict += msg => ShowToast("⚠ " + msg, alert: true);
        // กลับมาที่ sidecar → focus ช่อง ถ.พ. ที่ค้างไว้ตอนรถเข้า (ตอนนั้นพนักงานอยู่ในโปรแกรมชั่ง)
        Activated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { foreach (var p in _panels.Values) p.OnWindowActivated(); });

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // WS_EX_TOOLWINDOW: ไม่อยู่ใน Alt+Tab / taskbar
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
            _widthPx = (int)Math.Round(c.Cfg.SidecarWidth * VisualTreeHelper.GetDpi(this).DpiScaleX);
            _glassOn = previewDir == null && GlassBackdrop.Apply(_hwnd, c.Cfg.Glass);
            ApplyTint();
            if (previewDir == null && c.Cfg.ActivateOnScale)
            {
                _hotkeyOk = Native.RegisterHotKey(_hwnd, HotkeyId, Native.MOD_NOREPEAT, Native.VK_F24);
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
                ActLog(_hotkeyOk ? "hotkey F24 registered" : "hotkey F24 NOT registered (used by another program)");
            }
        };
        Closed += (_, _) => { if (_hotkeyOk) Native.UnregisterHotKey(_hwnd, HotkeyId); };

        RefreshAll();
        if (previewDir != null)
        {
            ContentRendered += async (_, _) => await RenderPreviewAsync();
            return;
        }

        new WindowInteropHelper(this).EnsureHandle();   // มี HWND ก่อนแสดง (Track ใช้)
        _trackTimer.Tick += (_, _) => Track();
        _watchTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, c.Cfg.WatchSeconds));
        _watchTimer.Tick += async (_, _) => await _c.ScanScaleAsync();
        _apiTimer.Interval = TimeSpan.FromSeconds(Math.Max(30, c.Cfg.ApiPollSeconds));
        _apiTimer.Tick += async (_, _) => await _c.FetchApiAsync();
        _minuteTimer.Tick += (_, _) => { RefreshAll(); CheckDayCut(); };
        // ไม่มีการส่ง Aquadat อัตโนมัติ — ส่งเมื่อผู้ใช้ยืนยันตัดยอดของแต่ละวันในแท็บ "ส่งยอดสารเคมี"
        _toastTimer.Tick += (_, _) => HideToast();
        foreach (var t in new[] { _trackTimer, _watchTimer, _apiTimer, _minuteTimer }) t.Start();
        // เริ่มหลัง dispatcher ทำงานแล้ว — ถ้าเรียกตรงนี้ (ก่อน app.Run) continuation ของ await จะไปรันบน thread pool
        // แล้ว event ที่แตะ UI จะพังเงียบ
        Dispatcher.BeginInvoke(async () =>
        {
            if (_c.Store.BadFiles.Count > 0)
                ShowToast($"⚠ ไฟล์บันทึกอ่านไม่ได้ {_c.Store.BadFiles.Count} รายการ (เก็บไว้ใน data\\) — แจ้งผู้ดูแลตรวจ\n{string.Join(", ", _c.Store.BadFiles)}", alert: true);
            await _c.ScanScaleAsync();
            await _c.FetchApiAsync();
            CheckDayCut();     // เปิดโปรแกรมหลังข้ามวัน → สรุปวันที่ยังไม่เคยสรุป
        });
    }

    // ───────────────────────── สร้างหน้าจอ ─────────────────────────

    void BuildUi()
    {
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());

        // ── หัว: ชื่อ + pill สถานะ + segmented tabs ──
        var head = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(new StackPanel { Children = { _title, _subtitle } });
        var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        pills.Children.Add(Kit.Pill(out _scaleDot, out _scaleText));
        pills.Children.Add(Kit.Pill(out _apiDot, out _apiText));
        Grid.SetColumn(pills, 1);
        titleRow.Children.Add(pills);
        head.Children.Add(titleRow);

        var seg = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(2), Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x76, 0x76, 0x80)),
        };
        var segGrid = new UniformGrid { Rows = 1 };
        seg.Child = segGrid;
        head.Children.Add(seg);

        _headerBg.Child = head;
        shell.Children.Add(_headerBg);

        // ── หน้าแต่ละแท็บ ──
        var pages = new Grid();
        Grid.SetRow(pages, 1);
        shell.Children.Add(pages);

        foreach (var chem in _c.Chemicals)
        {
            var panel = new ReceivePanel(_c, chem, () => this, rec =>
            {
                // record ของสารเคมีอื่น → สลับไปแท็บของมันแล้วเปิดที่นั่น (record เดียวเปิดได้ฟอร์มเดียว)
                if (!_panels.TryGetValue(rec.ChemicalOr(_c.Cfg.Tanks), out var own)) return;
                SelectTab(own.Chemical);
                own.TryOpen(rec);
                ShowToast($"#{rec.TrackId.TrimStart('0')} ({rec.Plate}) เป็นรถ {own.Chemical} — เปิดที่แท็บ {own.Chemical}");
            });
            _panels[chem] = panel;
            var (top, bottom) = ChemColors.TryGetValue(chem, out var cc) ? cc : (Color.FromRgb(0x66, 0xD4, 0xCF), Color.FromRgb(0x30, 0xB0, 0xC7));
            var chart = new TankBarChart(top, bottom);
            _charts[chem] = chart;
            var note = Kit.T("", 11, null, "Muted", new Thickness(2, 6, 0, 0));
            _chartNotes[chem] = note;
            var chartBox = new StackPanel();
            chartBox.Children.Add(Kit.Header($"ระดับถัง {chem}", Kit.T("━ API  ┅ คาดการณ์", 11, null, "Muted")));
            chartBox.Children.Add(chart);
            chartBox.Children.Add(note);

            // ── ขั้นตอนถัดไป: บันทึกครบแล้ว → ไปแท็บส่งยอดสารเคมี (อยู่ใต้กราฟ เห็นได้โดยไม่ต้องเลื่อน) ──
            var goSend = new Button { Content = "ไปส่งยอดสารเคมี →", Style = Kit.S("GlassButton") };
            goSend.Click += (_, _) => _tabs[^1].Seg.IsChecked = true;
            _goSend[chem] = goSend;
            var next = new StackPanel();
            next.Children.Add(Kit.T("บันทึกรถครบทุกคันแล้ว → ไปส่งยอดเข้า Aquadat", 11.5, null, "Muted", new Thickness(2, 0, 0, 8)));
            next.Children.Add(goSend);

            var content = new StackPanel { Margin = new Thickness(12, 2, 12, 12) };
            content.Children.Add(Kit.Card(chartBox));
            content.Children.Add(Kit.Card(next));
            content.Children.Add(panel.QueueCard);
            content.Children.Add(panel.FormCard);
            AddTab(segGrid, pages, chem, content);
        }
        AddTab(segGrid, pages, "ส่งยอดสารเคมี", new StackPanel { Margin = new Thickness(12, 2, 12, 12), Children = { _aq.View } });
        _tabs[^1].Seg.Checked += (_, _) => _aq.OnEnter();
        _tabs[0].Seg.IsChecked = true;

        // ── toast แจ้งรถเข้า/ออก ──
        _toast.CornerRadius = new CornerRadius(16);
        _toast.Padding = new Thickness(14, 10, 14, 10);
        _toast.Margin = new Thickness(14, 6, 14, 0);
        _toast.VerticalAlignment = VerticalAlignment.Top;
        _toast.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        _toast.BorderBrush = Kit.B("Accent");
        _toast.BorderThickness = new Thickness(1.5);
        _toast.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.22 };
        _toast.Child = _toastText;
        _toast.Visibility = Visibility.Collapsed;
        _toast.MouseLeftButtonDown += (_, _) => HideToast();
        Grid.SetRow(_toast, 1);
        shell.Children.Add(_toast);

        _root.Child = shell;
        _root.BorderThickness = new Thickness(1);
        _root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
        Content = _root;
        ApplyTint();

        PreviewKeyDown += OnPreviewKeyDown;
        if (_previewDir == null) ListenAdminExit();
    }

    void AddTab(UniformGrid segGrid, Grid pages, string name, UIElement content)
    {
        var badge = Kit.Badge(out var badgeText);
        var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        label.Children.Add(new TextBlock { Text = name });
        label.Children.Add(badge);
        var rb = new RadioButton { Style = Kit.S("Segment"), Content = label, GroupName = "tabs" };
        System.Windows.Automation.AutomationProperties.SetName(rb, name);
        segGrid.Children.Add(rb);

        var page = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed,
            Focusable = false,      // คลิกพื้นหน้าไม่แย่ง focus จากช่องที่ตั้งไว้ (เช่น ถ.พ. ตอนรถเข้า)
        };
        pages.Children.Add(page);
        rb.Checked += (_, _) =>
        {
            foreach (var t in _tabs) t.Page.Visibility = t.Page == page ? Visibility.Visible : Visibility.Collapsed;
            Kit.FadeIn(page, 180);
        };
        _tabs.Add((rb, badgeText, badge, page));
    }

    /// <summary>พื้นหน้าต่าง: เบลอได้ → tint โปร่ง + แสงสีจางๆ, ไม่ได้ → ไล่สีทึบ</summary>
    void ApplyTint()
    {
        byte a = _glassOn ? (byte)0xB0 : (byte)0xFF;
        var baseBrush = new LinearGradientBrush(
            Color.FromArgb(a, 0xF2, 0xF5, 0xFA), Color.FromArgb(a, 0xE4, 0xEA, 0xF3), 90);
        var glowA = new RadialGradientBrush(Color.FromArgb(0x40, 0x64, 0xD2, 0xFF), Color.FromArgb(0, 0x64, 0xD2, 0xFF))
        { Center = new Point(0.1, 0.05), GradientOrigin = new Point(0.1, 0.05), RadiusX = 0.9, RadiusY = 0.35 };
        var glowB = new RadialGradientBrush(Color.FromArgb(0x33, 0xBF, 0x5A, 0xF2), Color.FromArgb(0, 0xBF, 0x5A, 0xF2))
        { Center = new Point(0.95, 0.6), GradientOrigin = new Point(0.95, 0.6), RadiusX = 0.8, RadiusY = 0.35 };
        var group = new DrawingGroup();
        var full = new RectangleGeometry(new System.Windows.Rect(0, 0, 1, 1));
        group.Children.Add(new GeometryDrawing(baseBrush, null, full));
        group.Children.Add(new GeometryDrawing(glowA, null, full));
        group.Children.Add(new GeometryDrawing(glowB, null, full));
        _root.Background = new DrawingBrush(group) { Stretch = Stretch.Fill };

        _headerBg.Background = _nag
            ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x3B, 0x30))
            : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        _headerBg.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
        _headerBg.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    // ───────────────────────── วาดใหม่ ─────────────────────────

    void RefreshAll()
    {
        foreach (var (chem, chart) in _charts)
        {
            chart.SetData(_c.TankViews(chem), _c.Cfg.LowLevelPercent);
            var low = _c.TankViews(chem).Where(v => (v.Pred?.Level ?? v.ApiLevel) is { } l && l / v.Tank.Capacity * 100 <= _c.Cfg.LowLevelPercent)
                                       .Select(v => v.Tank.Name).ToList();
            _chartNotes[chem].Text = low.Count > 0 ? $"⚠ ระดับต่ำ: {string.Join(", ", low)} · แตะแท่งเพื่อดูรายละเอียด" : "แตะ/ชี้ที่แท่งเพื่อดูรายละเอียด";
            _chartNotes[chem].Foreground = Kit.B(low.Count > 0 ? "Red" : "Muted");
        }
        if (_c.Watcher.LastOk != null)
            foreach (var p in _panels.Values)
                if (p.DropIfCancelled() is { } gone)
                    ShowToast($"ตั๋ว {gone} ไม่อยู่ในตาชั่งแล้ว (ถูกยกเลิกในโปรแกรมชั่ง) — ล้างฟอร์มให้แล้ว", alert: true);
        foreach (var p in _panels.Values) p.RefreshQueue();
        _aq.Refresh();

        int i = 0;
        foreach (var p in _panels.Values) SetBadge(i++, p.QueueCount, "OrangeDot");
        SetBadge(i, _aq.PendingCount, "Accent");

        // ไม่มีรถค้างในแท็บนี้แต่ยังมีวันที่รอยืนยันตัดยอด → ปุ่มไปแท็บส่งยอดเด่นขึ้นเป็นขั้นตอนถัดไป
        foreach (var (chem, btn) in _goSend)
        {
            bool ready = _panels[chem].QueueCount == 0 && _aq.PendingCount > 0;
            var style = Kit.S(ready ? "PrimaryButton" : "GlassButton");
            if (btn.Style != style) btn.Style = style;     // ตั้งเฉพาะเมื่อเปลี่ยน (RefreshAll ถูกเรียกทุก 3 วิ)
            var text = ready ? $"ไปตัดยอดสารเคมี (รอยืนยัน {_aq.PendingCount} วัน) →" : "ไปส่งยอดสารเคมี →";
            if ((string)btn.Content != text) btn.Content = text;
        }

        // pill ตาชั่ง
        var w = _c.Watcher;
        if (!w.Enabled) { _scaleDot.Fill = Kit.B("Muted"); _scaleText.Text = "ตาชั่ง: ไม่ได้ตั้งค่า"; }
        else if (w.LastError != null) { _scaleDot.Fill = Kit.B("RedDot"); _scaleText.Text = "ตาชั่ง: อ่านไม่ได้"; }
        else if (w.LastOk is { } ok) { _scaleDot.Fill = Kit.B("GreenDot"); _scaleText.Text = $"ตาชั่ง {ok:HH:mm:ss}"; }
        else { _scaleDot.Fill = Kit.B("OrangeDot"); _scaleText.Text = "ตาชั่ง…"; }
        _scaleText.ToolTip = w.LastError ?? $"ตรวจ data.mdb ทุก {_c.Cfg.WatchSeconds} วิ";

        // pill API
        var ts = _c.LatestApiTs;
        _apiDot.Fill = Kit.B(_c.LastFetchError != null ? "RedDot" : ts == default ? "OrangeDot" : "GreenDot");
        _apiText.Text = ts == default ? "API —" : $"API {ts:HH:mm}";
        _apiText.ToolTip = _c.LastFetchError != null
            ? $"ดึง API ไม่ได้ ({_c.LastFetchError}) · แสดงค่าล่าสุดที่มี"
            : ts == default ? "ยังไม่มีข้อมูลระดับถัง" : $"ข้อมูล ณ {ts:HH:mm} น. · รอบถัดไป {ts.AddHours(2):HH:mm} น.";

        int pending = _c.Store.All.Count(r => !r.IsComplete);
        if (_nag)
        {
            _subtitle.Text = $"มีตั๋วค้าง {pending} คัน — บันทึกให้ครบก่อนปิดโปรแกรมชั่ง";
            _subtitle.Foreground = Kit.B("Red");
        }
        else
        {
            _subtitle.Text = pending == 0 ? "ตั๋วบันทึกครบทุกคัน" : $"ตั๋วที่ยังบันทึกไม่ครบ {pending} คัน";
            _subtitle.Foreground = Kit.B(pending == 0 ? "Muted" : "Orange");
        }
    }

    void SetBadge(int i, int count, string brush)
    {
        if (i >= _tabs.Count) return;
        var t = _tabs[i];
        t.Badge.Text = count.ToString();
        t.BadgeBox.Background = Kit.B(brush);
        t.BadgeBox.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void SelectTab(string chem)
    {
        int i = _panels.Keys.ToList().IndexOf(chem);
        if (i >= 0) _tabs[i].Seg.IsChecked = true;
    }

    // ───────────────────────── เหตุการณ์ตาชั่ง ─────────────────────────

    /// <summary>แท็บสารที่เปิดอยู่ตอนนี้ (null = แท็บส่งยอด)</summary>
    ReceivePanel? CurrentPanel =>
        _panels.Values.FirstOrDefault(p => _tabs[_panels.Keys.ToList().IndexOf(p.Chemical)].Seg.IsChecked == true);

    void OnArrival(Arrival a)
    {
        if (!_panels.TryGetValue(a.Chemical, out var panel)) return;
        var r = a.Row;
        RequestActivate();     // รถเข้า = พนักงานต้องมากรอกที่ sidecar ต่อ
        if (panel.IsIdle)
        {
            panel.LoadArrival(r, auto: true);
            // พนักงานกำลังพิมพ์ในอีกแท็บ → ไม่สลับแท็บให้เสียจังหวะ แค่แจ้ง
            var current = CurrentPanel;
            if (current == null || current == panel || current.IsIdle)
            {
                SelectTab(a.Chemical);
                panel.FocusEntry();
                ShowToast($"รถ {a.Chemical} เข้า: {r.Truck}  #{r.Ticket1Short}\nกรอก ถ.พ. / อุณหภูมิ / ใบนำส่ง แล้วเลือกถัง");
            }
            else
                ShowToast($"รถ {a.Chemical} เข้า: {r.Truck}  #{r.Ticket1Short}\nเติมฟอร์มไว้ที่แท็บ {a.Chemical} แล้ว (แท็บนี้ยังไม่ถูกแตะ)");
        }
        else
        {
            ShowToast($"รถ {a.Chemical} เข้าใหม่: {r.Truck}  #{r.Ticket1Short}\nอยู่ในรายการ \"รถรอบันทึก\" (ฟอร์มปัจจุบันยังไม่ถูกแตะ)");
        }
    }

    void OnWeighedOut(UnloadRecord rec)
    {
        foreach (var p in _panels.Values) p.OnWeighedOut(rec);
        var miss = rec.Missing;
        if (miss.Count > 0 && _panels.TryGetValue(rec.ChemicalOr(_c.Cfg.Tanks), out var panel))
        {
            // ยังขาดข้อมูล (ปกติคือระดับหลังเติม) → เปิดรายการของรถคันนี้แล้ววาง cursor ที่ช่องระดับ
            // ไม่ทับฟอร์มที่พนักงานพิมพ์ค้าง และไม่สลับแท็บถ้าอีกแท็บกำลังพิมพ์อยู่
            var current = CurrentPanel;
            bool tabFree = current == null || current == panel || current.IsIdle;
            if (tabFree && panel.EditingId != rec.Id && panel.IsBlank) panel.OpenRecord(rec);
            if (tabFree && panel.EditingId == rec.Id)
            {
                SelectTab(panel.Chemical);
                panel.FocusLevels(rec);
            }
            RequestActivate();
        }
        ShowToast(miss.Count == 0
            ? $"✓ {rec.Plate} ชั่งออกแล้ว — บันทึกครบ"
            : $"{rec.Plate} ชั่งออกแล้ว · {Kit.NetText(rec.NetKg)}\nยังขาด: {string.Join(", ", miss)}");
    }

    void ShowToast(string text, bool alert = false)
    {
        _toastText.Text = text;
        _toastText.Foreground = Kit.B(alert ? "Red" : "Ink");
        _toast.BorderBrush = Kit.B(alert ? "RedDot" : "Accent");
        // แจ้งเตือนสีแดงค้างนานกว่า (15 วิ) ให้พนักงานเห็นแน่
        _toastTimer.Interval = TimeSpan.FromSeconds(alert ? 15 : 5);
        _toast.Visibility = Visibility.Visible;
        var tt = new TranslateTransform(0, -12);
        _toast.RenderTransform = tt;
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
        Kit.FadeIn(_toast, 220);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void HideToast()
    {
        _toastTimer.Stop();
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
        anim.Completed += (_, _) => { _toast.Visibility = Visibility.Collapsed; _toast.BeginAnimation(OpacityProperty, null); };
        _toast.BeginAnimation(OpacityProperty, anim);
    }

    // ───────────────────────── เกาะหน้าต่างโปรแกรมเดิม ─────────────────────────

    void Track()
    {
        if (_hwnd == IntPtr.Zero) return;
        var target = Native.FindMainWindow(_c.Cfg.TargetProcess, _hwnd);
        bool targetUsable = target != IntPtr.Zero && !Native.IsIconic(target);
        bool nag = !targetUsable && _c.Store.HasIncomplete;

        if (nag != _nag)
        {
            _nag = nag;
            // หัวสั้นพอให้อยู่บรรทัดเดียวข้าง pill สถานะ รายละเอียดอยู่ในบรรทัดรอง
            _title.Text = nag ? "⚠ บันทึกไม่ครบ" : "การรับสารเคมี";
            _title.Foreground = Kit.B(nag ? "Red" : "Ink");
            ApplyTint();
            RefreshAll();      // เขียนบรรทัดรองใหม่ทันที (ไม่ต้องรอรอบ 30 วิ)
        }

        if (!targetUsable || !Native.IsZoomed(target)) ReleaseAppBar();
        TryActivate(target);

        if (targetUsable)
        {
            SetTopmost(false);
            var t = Native.VisibleBounds(target);
            var (screen, wa) = Native.MonitorOf(t);
            int w = _widthPx, x, y, h;
            if (Native.IsZoomed(target))
            {
                // โปรแกรมชั่งขยายเต็มจอ: จองแถบขวาของจอเป็น AppBar แล้ว Windows จะหดหน้าต่างที่เต็มจอให้หลบเอง
                _appBar ??= Native.AppBarDockRight(_hwnd, screen, w, alreadyRegistered: false);
                var bar = _appBar.Value;
                (x, y) = (bar.Left, Math.Max(bar.Top, wa.Top));
                h = Math.Min(bar.Bottom, wa.Bottom) - y;
            }
            else
            {
                x = t.Right + w <= wa.Right ? t.Right
                  : t.Left - w >= wa.Left ? t.Left - w
                  : wa.Right - w;
                y = Math.Max(wa.Top, t.Top);
                h = Math.Min(Math.Max(t.Height, 640), wa.Bottom - y);
            }

            // วางไว้เหนือโปรแกรมเดิมหนึ่งชั้นพอดี ไม่ลอยทับหน้าต่างอื่น
            var prev = Native.GetWindow(target, Native.GW_HWNDPREV);
            Native.GetWindowRect(_hwnd, out var cur);
            if (Rect.FromLTRB(cur.Left, cur.Top, cur.Right, cur.Bottom) != new Rect(x, y, w, h) || prev != _hwnd || !IsVisible)
            {
                var after = prev == IntPtr.Zero || prev == _hwnd ? Native.HWND_TOP : prev;
                Native.SetWindowPos(_hwnd, after, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            }
            if (!IsVisible) Show();
        }
        else if (nag)
        {
            if (!IsVisible)
            {
                Native.GetWindowRect(_hwnd, out var cur);
                var (_, wa) = Native.MonitorOf(Rect.FromLTRB(cur.Left, cur.Top, cur.Right, cur.Bottom));
                if (!wa.IntersectsWith(Rect.FromLTRB(cur.Left, cur.Top, cur.Right, cur.Bottom)) || cur.Right - cur.Left < 50)
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, wa.Right - _widthPx, wa.Top, _widthPx, Math.Min(900, wa.Height), Native.SWP_NOACTIVATE);
                Show();
            }
            SetTopmost(true);
        }
        else
        {
            SetTopmost(false);
            if (IsVisible) Hide();
        }
    }

    // ───────────────────────── ดึง sidecar ขึ้นมา active (รถชั่งเข้า/ออก) ─────────────────────────

    DateTime? _activateUntil;
    const int HotkeyId = 0x5C1D;
    bool _hotkeyOk, _f24Down;
    DateTime _f24At;

    /// <summary>F24 ที่เรากดเองมาถึง → ตอนนี้ Windows ให้สิทธิ์ขึ้นหน้าแล้ว</summary>
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            if (_activateUntil != null)
            {
                bool ok = Native.SetForeground(_hwnd) && Native.GetForegroundWindow() == _hwnd;
                ActLog(ok ? "activated via hotkey" : $"hotkey arrived but SetForegroundWindow failed (foreground {Native.Describe(Native.GetForegroundWindow())})");
                if (ok) OnForced();
            }
            ReleaseF24();
        }
        return IntPtr.Zero;
    }

    void ReleaseF24()
    {
        if (!_f24Down) return;
        _f24Down = false;
        Native.F24Up();
    }

    void OnForced()
    {
        Activate();
        _activateUntil = null;
        // IsActive ค้างเป็น true → WPF ไม่ยิง Activated ซ้ำ จึงวาง cursor เอง
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { foreach (var p in _panels.Values) p.OnWindowActivated(); });
    }

    /// <summary>ขอให้ดึง sidecar ขึ้นมา active ในจังหวะที่ปลอดภัย (ดู TryActivate) ภายใน ActivateTimeoutSeconds</summary>
    void RequestActivate()
    {
        if (!_c.Cfg.ActivateOnScale || _previewDir != null) return;
        _activateUntil = DateTime.Now.AddSeconds(Math.Max(5, _c.Cfg.ActivateTimeoutSeconds));
        _actLast = "";
        ActLog("request");
    }

    /// <summary>
    /// เรียกทุก tick ของ Track — ดึงขึ้นมาเมื่อ:
    ///   1) หน้าต่างที่ active อยู่คือหน้าต่างหลักของโปรแกรมชั่ง (ไม่ใช่ dialog/กล่องข้อความของมัน และไม่ใช่โปรแกรมอื่น)
    ///   2) หน้าต่างหลักไม่ถูก dialog modal ล็อกอยู่
    ///   3) พนักงานหยุดพิมพ์/คลิก ≥ ActivateIdleMs — กันตัวเลขที่กำลังพิมพ์ในโปรแกรมชั่งตกมาที่ sidecar
    /// เกินเวลาแล้วเลิก (เหลือ toast ที่แสดงไปแล้ว)
    /// </summary>
    void TryActivate(IntPtr target)
    {
        // กัน F24 ค้าง ถ้า hotkey ไม่มาภายใน 1 วิ
        if (_f24Down && (DateTime.Now - _f24At).TotalSeconds > 1) { ActLog("hotkey did not arrive"); ReleaseF24(); }
        if (_activateUntil is not { } until) return;
        // ตัดสินจากหน้าต่าง foreground จริงของ Windows — IsActive ของ WPF ค้างเป็น true ได้
        // (เจอใน mock แบบ elevated: รถเข้าขณะพนักงานอยู่ในหน้าต่าง cmd แต่ IsActive = true → ไม่ดึงขึ้นมา)
        var fg = Native.GetForegroundWindow();
        if (fg == _hwnd) { ActLog("already foreground"); _activateUntil = null; return; }
        if (DateTime.Now > until) { ActLog("timeout"); _activateUntil = null; return; }
        if (!IsVisible) { ActLog("wait: sidecar hidden"); return; }
        if (Native.IdleMs() < (uint)Math.Max(0, _c.Cfg.ActivateIdleMs)) { ActLog("wait: user typing"); return; }
        if (fg != IntPtr.Zero && fg != target) { ActLog($"wait: foreground {Native.Describe(fg)} is not scale {Native.Describe(target)}"); return; }
        if (target != IntPtr.Zero && !Native.IsWindowEnabled(target)) { ActLog("wait: scale has modal dialog"); return; }
        bool wpfActive = IsActive;
        bool ok = Native.ForceForeground(_hwnd, out var diag);
        ActLog((ok ? $"activated ({diag})" : $"ForceForeground failed ({diag}; foreground now {Native.Describe(Native.GetForegroundWindow())})")
               + (wpfActive ? " [WPF IsActive was stale]" : ""));
        if (ok) { OnForced(); return; }
        // Windows ไม่ยอม (เกิดเมื่อพนักงานเพิ่งคลิกกลับไปที่โปรแกรมชั่ง) → ขอสิทธิ์ผ่าน hotkey ของเราเอง (ไม่เกิน 1 ครั้ง/2 วิ)
        if (_hotkeyOk && !_f24Down && (DateTime.Now - _f24At).TotalSeconds >= 2)
        {
            _f24At = DateTime.Now;
            _f24Down = true;
            ActLog("sending hotkey F24");
            Native.F24Down();
        }
    }

    string _actLast = "";

    /// <summary>บันทึกเหตุผลที่ยังดึงขึ้นมาไม่ได้ลง data\activate.log (เฉพาะเมื่อเหตุผลเปลี่ยน) — ไว้ตรวจที่หน้างาน</summary>
    void ActLog(string msg)
    {
        if (msg == _actLast) return;
        _actLast = msg;
        try { File.AppendAllText(Path.Combine(_c.DataDir, "activate.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}{Environment.NewLine}"); }
        catch { }
    }

    /// <summary>
    /// ลอยเหนือทุกหน้าต่าง (โหมดเตือน) ผ่าน Win32 ตรงๆ — Window.Topmost ของ WPF ไม่ติดกับหน้าต่างนี้
    /// (ทดสอบแล้ว WS_EX_TOPMOST ไม่ขึ้น) ตรวจสถานะจริงจาก ex-style ทุก tick จึงซ่อมตัวเองได้ถ้ามีใครดึงออก
    /// </summary>
    void SetTopmost(bool on)
    {
        bool now = (Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        if (now == on) return;
        Native.SetWindowPos(_hwnd, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    void ReleaseAppBar()
    {
        if (_appBar == null) return;
        Native.AppBarRemove(_hwnd);
        _appBar = null;
    }

    // ───────────────────────── ตัดยอดรายวัน (ข้ามวัน → popup สรุป) ─────────────────────────

    bool _dayCutShowing;
    string DayCutPath => Path.Combine(_c.DataDir, "daycut.json");

    /// <summary>
    /// ข้ามวันแล้ว (หรือเปิดโปรแกรมหลังข้ามวัน) → สรุปตัดยอดของวันที่ยังไม่เคยสรุป (นับตามวันชั่งเข้า)
    /// ทีละวัน ย้อนได้ไม่เกิน PendingLookbackDays; ปิด popup แล้วจึงจำว่าสรุปวันนั้นแล้ว
    /// ต้องอ่านรถย้อนหลังจาก data.mdb ได้ก่อน (RecentScale) ไม่งั้นจะนับรถที่ไม่มีบันทึกไม่ได้
    /// </summary>
    void CheckDayCut()
    {
        if (_previewDir != null || _dayCutShowing || _c.RecentScale == null) return;
        var yesterday = DateTime.Today.AddDays(-1);
        DateTime last;
        try
        {
            last = File.Exists(DayCutPath)
                ? DateTime.ParseExact(System.Text.Json.JsonDocument.Parse(File.ReadAllText(DayCutPath)).RootElement.GetProperty("lastSummarized").GetString()!,
                                      "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : yesterday.AddDays(-1);       // ติดตั้งใหม่: เริ่มสรุปจากเมื่อวาน ไม่ไล่ย้อนทั้งสัปดาห์
        }
        catch { last = yesterday.AddDays(-1); }
        if (last >= yesterday) return;

        var oldest = DateTime.Today.AddDays(-Math.Max(1, _c.Cfg.PendingLookbackDays));
        _dayCutShowing = true;
        try
        {
            for (var d = last.AddDays(1) < oldest ? oldest : last.AddDays(1); d <= yesterday; d = d.AddDays(1))
            {
                var (text, pending) = _c.DaySummary(d);
                if (text.Length > 0)
                {
                    if (pending) text += "\n\nรายการ ⚠ ยังไม่นับในยอด / เป็นยอดประมาณการ / ต้องตรวจ — กรอกให้ครบที่แท็บของสาร หรือแก้ยอดในหน้าตัดยอด";
                    text += "\n\nยอดยังไม่ถูกส่ง — ตรวจแล้วกด \"ยืนยันและส่ง\" ในหน้าตัดยอด";
                    if (GlassDialog.Confirm(this, text, "ไปหน้าตัดยอด", "ภายหลัง", pending ? GlassDialog.Kind.Warn : GlassDialog.Kind.Info, list: true))
                        _tabs[^1].Seg.IsChecked = true;
                    _c.Store.Audit("daycut-summary", new { day = d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), pending, text });
                }
                Directory.CreateDirectory(_c.DataDir);
                File.WriteAllText(DayCutPath, System.Text.Json.JsonSerializer.Serialize(new
                {
                    lastSummarized = d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                }));
            }
        }
        catch { /* ไม่ให้ popup สรุปทำ sidecar ล้ม — ลองใหม่รอบหน้า */ }
        finally { _dayCutShowing = false; }
    }

    // ───────────────────────── ปิดโปรแกรม / watchdog ─────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose) e.Cancel = true;     // ปิดเองไม่ได้ (Alt+F4) — ต้องใช้ทางออกผู้ดูแล
        if (!e.Cancel) ReleaseAppBar();
        base.OnClosing(e);
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ทางออกสำหรับผู้ดูแลระบบ: Ctrl+Shift+F12
        if (e.Key == Key.F12 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            AdminExit();
    }

    void AdminExit()
    {
        // เขียนธงก่อนปิด เพื่อบอก watchdog ว่านี่คือการปิดโดยตั้งใจ อย่าเปิดใหม่
        WriteStopMarker();
        _allowClose = true;
        Close();                               // → OnClosing คืนพื้นที่ AppBar
        Application.Current.Shutdown();
    }

    /// <summary>
    /// ทางออกผู้ดูแลจากสคริปต์ (เช่น mock stop-mock.ps1): set event นี้ = กด Ctrl+Shift+F12
    /// event อยู่ใน session ของผู้ใช้ และใช้ ACL เริ่มต้นของ process ที่ elevate (sidecar เป็น requireAdministrator)
    /// → เปิดได้เฉพาะ process ที่เป็น admin เหมือนสิทธิ์ที่ต้องใช้ kill sidecar อยู่แล้ว
    /// </summary>
    public const string AdminExitEventName = @"Local\TruckSidecar.AdminExit";
    EventWaitHandle? _adminExitSignal;

    void ListenAdminExit()
    {
        try
        {
            _adminExitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AdminExitEventName);
            ThreadPool.RegisterWaitForSingleObject(_adminExitSignal, (_, _) => Dispatcher.InvokeAsync(AdminExit), null, -1, true);
        }
        catch { /* ไม่มีทางออกจากสคริปต์ ก็ยังกด Ctrl+Shift+F12 ได้ */ }
    }

    // มี = admin สั่งปิด (watchdog จะไม่เปิดใหม่); ไม่มี = watchdog เปิดใหม่ถ้า sidecar หลุด
    string StopMarkerPath => Path.Combine(_c.DataDir, "sidecar.stopped");

    void WriteStopMarker()
    {
        try
        {
            Directory.CreateDirectory(_c.DataDir);
            // ใช้ serializer เพื่อ escape "\" ใน DOMAIN\user ให้เป็น JSON ถูกต้อง
            File.WriteAllText(StopMarkerPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                at = DateTime.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                by = $@"{Environment.UserDomainName}\{Environment.UserName}",
            }));
        }
        catch { /* ปิดต่อได้แม้เขียนธงไม่สำเร็จ */ }
    }

    void ClearStopMarker()
    {
        try { if (File.Exists(StopMarkerPath)) File.Delete(StopMarkerPath); }
        catch { /* ไม่เป็นไร */ }
    }

    // ───────────────────────── --preview: เรนเดอร์ทุกแท็บเป็นรูป ─────────────────────────

    async Task RenderPreviewAsync()
    {
        try
        {
            // พื้นจำลอง "วอลเปเปอร์" ข้างหลังกระจก (จริงคือหน้าต่างโปรแกรมชั่ง/เดสก์ท็อปที่ถูกเบลอ)
            Background = new LinearGradientBrush(Color.FromRgb(0x9C, 0xB8, 0xD9), Color.FromRgb(0xC9, 0xB6, 0xE4), 60);
            var fetch = _c.FetchApiAsync();
            await Task.WhenAny(fetch, Task.Delay(6000));
            if (!string.IsNullOrEmpty(_c.Cfg.MdbPath))
            {
                try
                {
                    var rows = await ScaleDb.CandidatesAsync(_c.Cfg.MdbPath);
                    var latest = rows.FirstOrDefault(r => _c.Cfg.ChemicalOf(r.Product) == "PACL") ?? rows.FirstOrDefault();
                    if (latest != null && _panels.TryGetValue("PACL", out var p)) p.LoadArrival(latest, true);
                }
                catch { /* ไม่มี mdb ก็ข้าม */ }
            }
            RefreshAll();
            await Task.Delay(900);                       // รอแอนิเมชันกราฟ

            Directory.CreateDirectory(_previewDir!);
            var dpi = VisualTreeHelper.GetDpi(this);
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Seg.IsChecked = true;
                _tabs[i].Page.BeginAnimation(OpacityProperty, null);
                _tabs[i].Page.Opacity = 1;
                UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var bmp = new RenderTargetBitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY),
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(this);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(_previewDir!, $"tab{i + 1}.png"));
                enc.Save(fs);
            }
        }
        finally
        {
            _allowClose = true;
            Close();
            Application.Current.Shutdown();
        }
    }
}
