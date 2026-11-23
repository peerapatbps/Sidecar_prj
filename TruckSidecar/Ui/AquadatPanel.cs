using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TruckSidecar.Ui;

/// <summary>
/// แท็บ "ส่งยอดสารเคมี": login Aquadat (ผู้ใช้กรอกเอง, รหัสผ่านไม่ถูกเก็บ) + ตัดยอดรายวัน
/// ตัดยอดจบเป็นวันๆ: แสดงรายการคำนวณต่อคู่ถัง → แก้ยอดได้ (≥ 0 + หมายเหตุ) → "ยืนยันและส่ง" ทีละวัน
/// login แล้ว → เลือกวันที่จะส่งได้ (วันนี้ + ย้อนหลัง DayChoices วัน) แสดงเฉพาะวันที่เลือก — ส่งได้ทุกเวลา
/// ยอดวันนี้ส่งระหว่างวันได้ (ยังไม่จบวัน): มีรถเพิ่มหลังส่ง → สถานะ "เปลี่ยนหลังยืนยัน" ต้องกดส่งใหม่ (ทับค่าเดิม)
/// ไม่มีการส่ง Aquadat อัตโนมัติ
/// </summary>
internal sealed class AquadatPanel
{
    readonly SidecarController _c;
    public UIElement View { get; }

    readonly TextBox _user = Kit.Field();
    readonly PasswordBox _pass = new() { Style = Kit.S("PasswordField") };
    readonly Button _btnLogin = new() { Style = Kit.S("GlassButton") };
    readonly TextBlock _status = Kit.T("", 12, FontWeights.SemiBold, "Muted", new Thickness(2, 8, 0, 0));
    readonly StackPanel _loginFields = new();
    readonly StackPanel _days = new();
    readonly TextBlock _sync = Kit.T("", 11.5, null, "Muted", new Thickness(2, 8, 0, 0));

    // เลือกวันที่จะส่ง (หลัง login): วันนี้, เมื่อวาน … ย้อนหลัง DayChoices วัน
    const int DayChoices = 3;
    readonly Border _dayPicker = new();
    readonly RadioButton[] _dayOpts = new RadioButton[DayChoices + 1];
    int _dayBack = 0;                    // วันที่เลือก = วันนี้ − _dayBack (เริ่มที่วันนี้)

    public AquadatPanel(SidecarController c)
    {
        _c = c;
        var root = new StackPanel();

        // ── login ──
        var login = new StackPanel();
        login.Children.Add(Kit.Header(c.AqSystemName));
        login.Children.Add(Kit.T("รหัสผ่านใช้ครั้งเดียวต่อการเข้าสู่ระบบ ไม่ถูกบันทึกลงเครื่อง", 11.5, null, "Muted", new Thickness(2, -4, 0, c.Cfg.UseAquadat2 ? 2 : 10)));
        if (c.Cfg.UseAquadat2)
            login.Children.Add(Kit.T("บัญชี AD: รหัสพนักงาน + รหัสผ่านเข้าคอมพิวเตอร์\n" +
                                     "กรอกรหัสผิดหลายครั้ง บัญชี AD จะถูกล็อก", 11, null, "Orange", new Thickness(2, 0, 0, 10)));
        _loginFields.Children.Add(Kit.Two(Kit.Labeled("ชื่อผู้ใช้", _user), Kit.Labeled("รหัสผ่าน", _pass)));
        login.Children.Add(_loginFields);
        _pass.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Login(); } };
        _btnLogin.Click += (_, _) => Login();
        login.Children.Add(_btnLogin);
        login.Children.Add(_status);
        login.Children.Add(_sync);
        root.Children.Add(Kit.Card(login));

        // ── ตัดยอดรายวัน ──
        var daily = new StackPanel();
        daily.Children.Add(Kit.Header("ตัดยอดรายวัน", Kit.T("Chemical income · ตัน · นับตามวันชั่งเข้า", 11.5, null, "Muted")));
        var opts = new UniformGrid { Rows = 1 };
        for (int i = 0; i <= DayChoices; i++)
        {
            int back = i;
            var rb = new RadioButton { Style = Kit.S("Segment"), GroupName = "aq-day", IsChecked = back == _dayBack };
            rb.Checked += (_, _) => { if (_dayBack != back) { _dayBack = back; Refresh(); } };
            _dayOpts[i] = rb;
            opts.Children.Add(rb);
        }
        _dayPicker.Child = new StackPanel
        {
            Children =
            {
                Kit.T("เลือกวันที่จะส่ง", 11.5, FontWeights.SemiBold, "Muted", new Thickness(4, 0, 0, 3)),
                new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromArgb(0x1F, 0x76, 0x76, 0x80)), Child = opts,
                },
            },
        };
        _dayPicker.Margin = new Thickness(0, 0, 0, 10);
        daily.Children.Add(_dayPicker);
        daily.Children.Add(_days);
        root.Children.Add(Kit.Card(daily));

        var note = Kit.T("ยอด = (W1−W2) ÷ 1000 (ล้างถังเป็นค่าลบ) ปันตามปริมาตรแต่ละถัง แล้วรวมตาม configID:\n" +
                         "Alum A+B = 8037 · Alum C+D = 8036 · PACL A+B = 8423 · PACL C+D = 8424\n" +
                         "คู่ถังที่ติดลบ ย้ายไปหักอีกคู่ถังของสารเดียวกันในวันนั้น (Aquadat ไม่รับค่าติดลบ)",
                         11, null, "Muted", new Thickness(6, 0, 6, 10));
        root.Children.Add(note);

        // ช่องว่างเสมอ — ชื่อผู้ใช้ใน config แสดงเป็นค่าตัวอย่างเท่านั้น
        _user.Tag = c.Cfg.AquadatUsername.Length > 0 ? $"เช่น {c.Cfg.AquadatUsername}" : null;
        View = root;
        Refresh();
    }

    /// <summary>จำนวนวันที่รอยืนยันตัดยอด (badge แท็บ + ปุ่มไปแท็บนี้)</summary>
    public int PendingCount { get; private set; }
    string _sig = "";

    /// <summary>เข้าแท็บนี้ → ล้างช่องชื่อผู้ใช้/รหัสผ่าน แล้ววาง cursor ที่ชื่อผู้ใช้ (ถ้ายังไม่ได้ login)</summary>
    public void OnEnter()
    {
        if (_c.AqLoggedIn || !_btnLogin.IsEnabled) return;     // login แล้ว / กำลัง login อยู่
        _user.Clear();
        _pass.Clear();
        // รอให้หน้าแท็บแสดงก่อน (ช่องที่ยังซ่อนอยู่รับ focus ไม่ได้)
        _user.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(_user), _user);
            if (Window.GetWindow(_user)?.IsActive == true) _user.Focus();
        });
    }

    public void Refresh()
    {
        bool loggedIn = _c.AqLoggedIn;
        _btnLogin.Content = loggedIn ? "เข้าสู่ระบบใหม่" : $"เข้าสู่ระบบ {_c.AqSystemName}";
        _loginFields.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        if (!loggedIn && _status.Text.StartsWith("เข้าสู่ระบบแล้ว")) { _status.Text = "หมดเวลาเข้าสู่ระบบ — กรุณาเข้าสู่ระบบใหม่"; _status.Foreground = Kit.B("Orange"); }
        _status.Visibility = _status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        List<DayCutResult> cuts;
        try { cuts = _c.DayCuts(); }
        catch { cuts = new(); }
        PendingCount = cuts.Count(x => x.Status != DayCutStatus.Confirmed);
        _sync.Text = PendingCount == 0 ? "ตัดยอดยืนยันครบทุกวัน ✓"
            : $"รอยืนยันตัดยอด {PendingCount} วัน" + (loggedIn ? "" : " · เข้าสู่ระบบเพื่อยืนยันและส่ง");

        // ยังไม่ login: ทุกวันที่ยังไม่ยืนยัน + วันที่ยืนยันแล้ว 3 วันล่าสุด
        // login แล้ว: เฉพาะวันที่เลือก (ย้อนหลัง DayChoices วัน) — วันที่ไม่มียอดก็แสดง เพื่อให้รู้ว่าไม่มี
        _dayPicker.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        List<DayCutResult> show;
        List<DayCutResult> older = new();
        if (loggedIn)
        {
            for (int i = 0; i <= DayChoices; i++)
            {
                var d = DateTime.Today.AddDays(-i);
                var c = cuts.FirstOrDefault(x => x.Day == d);
                var mark = c == null ? "" : c.Status == DayCutStatus.Confirmed ? " ✓" : " •";
                var text = (i == 0 ? $"วันนี้ {d:dd/MM}" : $"{d:ddd dd/MM}") + mark;
                if (_dayOpts[i].Content as string != text) _dayOpts[i].Content = text;
                System.Windows.Automation.AutomationProperties.SetName(_dayOpts[i], $"วันที่ {d:dd/MM}");
            }
            var sel = DateTime.Today.AddDays(-_dayBack);
            DayCutResult? cut;
            try { cut = cuts.FirstOrDefault(x => x.Day == sel) ?? _c.CutOf(sel); }
            catch { cut = null; }
            show = cut == null ? new() : new() { cut };
            older = cuts.Where(x => x.Status != DayCutStatus.Confirmed && x.Day < DateTime.Today.AddDays(-DayChoices)).ToList();
        }
        else
            show = cuts.Where(x => x.Status != DayCutStatus.Confirmed)
                .Concat(cuts.Where(x => x.Status == DayCutStatus.Confirmed).Take(3))
                .OrderByDescending(x => x.Day).ToList();

        // สร้างใหม่เฉพาะเมื่อเนื้อหาเปลี่ยน (Refresh ถูกเรียกทุก 3 วิ — สร้างใหม่ทุกรอบปุ่มจะกดหาย)
        var sig = $"{loggedIn}{_c.Pushing}{DateTime.Today:yyyyMMdd}{_dayBack}|{string.Join(",", older.Select(x => x.Key))}|" + string.Join("|", show.Select(x =>
            $"{x.Key}{x.Status}{x.Sig}{x.ConfirmedAt}{x.Pending.Count}" +
            string.Join(",", x.Configs.Select(cf => $"{cf.Auto}{cf.Override}{cf.OverrideNote}{cf.Lines.Count}{cf.NeedsCheck}"))));
        if (sig == _sig && _days.Children.Count > 0) return;
        _sig = sig;
        _days.Children.Clear();
        if (show.Count == 0)
            _days.Children.Add(Kit.T(loggedIn ? "อ่านยอดของวันที่เลือกไม่ได้" : $"ยังไม่มียอดใน {_c.Cfg.PendingLookbackDays} วันที่ผ่านมา", 12, null, "Muted", new Thickness(2, 0, 0, 0)));
        foreach (var cut in show) _days.Children.Add(DayCard(cut, loggedIn));
        if (older.Count > 0)
            _days.Children.Add(Kit.T($"⚠ ยังมีวันที่รอยืนยันเก่ากว่า {DayChoices} วัน: {string.Join(", ", older.Select(x => x.Day.ToString("dd/MM")))}",
                11.5, FontWeights.SemiBold, "Orange", new Thickness(2, 2, 0, 0)));
    }

    UIElement DayCard(DayCutResult cut, bool loggedIn)
    {
        var box = new StackPanel();
        bool empty = cut.Configs.Count == 0 && cut.Pending.Count == 0;     // วันที่เลือกเองแต่ไม่มีรถ

        // หัว: วันที่ + สถานะ
        var (chipText, chipBrush, chipFg) = empty ? ("ไม่มียอด", "Muted", "Muted") : cut.Status switch
        {
            DayCutStatus.Confirmed => ($"ยืนยันแล้ว {(cut.ConfirmedAt?.Length >= 16 ? cut.ConfirmedAt[11..16] : "")}".Trim(), "GreenDot", "Green"),
            DayCutStatus.ChangedAfterConfirm => ("ยอดเปลี่ยนหลังยืนยัน — ยืนยันใหม่", "RedDot", "Red"),
            _ => ("รอยืนยัน", "OrangeDot", "Orange"),
        };
        var head = new Grid { Margin = new Thickness(2, 0, 2, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Kit.T(cut.Day.ToString("ddd dd/MM"), 15, FontWeights.Bold));
        var chip = Chip(chipText, chipBrush, chipFg);
        Grid.SetColumn(chip, 1);
        head.Children.Add(chip);
        box.Children.Add(head);

        // ต่อสาร → ต่อคู่ถัง
        foreach (var chem in cut.Configs.GroupBy(x => x.Chemical))
        {
            box.Children.Add(Kit.T(chem.Key, 13.5, FontWeights.Bold, "Ink2", new Thickness(2, 4, 0, 2)));
            foreach (var cfg in chem) box.Children.Add(ConfigBlock(cut, cfg));   // วันที่ยืนยันแล้วก็แก้ได้ (ต้องยืนยันใหม่)
        }

        if (empty)
            box.Children.Add(Kit.T("ไม่มียอดของวันนี้", 12, null, "Muted", new Thickness(2, 0, 0, 4)));

        // งานคงค้างของวัน
        if (cut.Pending.Count > 0)
        {
            var pend = new StackPanel { Margin = new Thickness(2, 4, 0, 6) };
            pend.Children.Add(Kit.T($"⚠ งานคงค้าง {cut.Pending.Count} รายการ (ไม่นับในยอด / ประมาณการ)", 12, FontWeights.SemiBold, "Orange"));
            foreach (var p in cut.Pending)
                pend.Children.Add(Kit.T($"   • #{p.Ticket1.TrimStart('0')} {p.Plate} — {p.Reason}", 11.5, null, "Ink2"));
            box.Children.Add(pend);
        }

        if (cut.Status != DayCutStatus.Confirmed && !empty)
        {
            var btn = new Button
            {
                Style = Kit.S("PrimaryButton"), Margin = new Thickness(0, 6, 0, 0),
                Content = loggedIn ? $"ยืนยันและส่งยอดวันที่ {cut.Day:dd/MM}" : $"เข้าสู่ระบบ {_c.AqSystemName} ก่อนยืนยัน",
                IsEnabled = loggedIn && !_c.Pushing && cut.Configs.Count > 0,
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, $"ยืนยัน {cut.Key}");
            btn.Click += async (_, _) => await ConfirmAsync(cut.Day);
            box.Children.Add(btn);
        }

        return new Border
        {
            Child = box, Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 255, 255, 255)),
            BorderBrush = cut.Status == DayCutStatus.Confirmed || empty ? null : new SolidColorBrush(Kit.WithAlpha(Kit.B(chipBrush), 0x66)),
            BorderThickness = new Thickness(cut.Status == DayCutStatus.Confirmed || empty ? 0 : 1),
        };
    }

    /// <summary>คู่ถัง 1 แถว: ชื่อ + ยอดที่จะส่ง + ปุ่มแก้ยอด แล้วตามด้วยรายการคำนวณ</summary>
    UIElement ConfigBlock(DayCutResult cut, DayCutConfig cfg)
    {
        var sp = new StackPanel { Margin = new Thickness(6, 2, 0, 6) };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Kit.T($"{cfg.Name}", 13, FontWeights.SemiBold));
        left.Children.Add(Kit.T($"#{cfg.ConfigId}", 11, null, "Muted"));
        g.Children.Add(left);
        var val = Kit.T($"{cfg.Final:0.###} ตัน", 14.5, FontWeights.Bold, cfg.Override != null ? "Orange" : "Ink", new Thickness(0, 0, 10, 0));
        val.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(val, 1);
        g.Children.Add(val);
        var edit = new Button
        {
            Content = "แก้ยอด", Style = Kit.S("GlassButton"), Padding = new Thickness(10, 3, 10, 3), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_c.Pushing,
        };
        System.Windows.Automation.AutomationProperties.SetName(edit, $"แก้ยอด {cut.Key} {cfg.ConfigId}");
        edit.Click += (_, _) => Edit(cut, cfg);
        Grid.SetColumn(edit, 2);
        g.Children.Add(edit);
        sp.Children.Add(g);

        // รายการคำนวณ
        foreach (var l in cfg.Lines)
            sp.Children.Add(Kit.T($"   {l.Text}   {(l.Tonnes >= 0 ? "+" : "−")}{Math.Abs(l.Tonnes):0.###}", 11.5, null, l.Tonnes < 0 ? "Orange" : "Muted"));
        if (cfg.Lines.Count == 0)
            sp.Children.Add(Kit.T("   ไม่มีรถลงคู่ถังนี้", 11.5, null, "Muted"));
        if (Math.Abs(cfg.Auto - cfg.Raw) > 0.0005 || cfg.Override != null)
            sp.Children.Add(Kit.T($"   ยอดคำนวณ {cfg.Auto:0.###} t" + (Math.Abs(cfg.Auto - cfg.Raw) > 0.0005 ? $" (ยอดดิบ {cfg.Raw:0.###})" : ""), 11.5, FontWeights.SemiBold, "Ink2"));
        if (cfg.Override != null)
            sp.Children.Add(Kit.T($"   ✎ แก้ด้วยมือ → {cfg.Override:0.###} t · {cfg.OverrideNote} ({cfg.OverrideBy})", 11.5, FontWeights.SemiBold, "Orange"));
        else if (cfg.NeedsCheck)
            sp.Children.Add(Kit.T("   ⚠ ต้องตรวจ: ยอดติดลบหักกับคู่ถังอื่นไม่หมด ส่งเป็น 0 — แก้ยอดถ้าจำเป็น", 11.5, FontWeights.SemiBold, "Red"));
        return sp;
    }

    void Edit(DayCutResult cut, DayCutConfig cfg)
    {
        var info = $"ยอดคำนวณ {cfg.Auto:0.###} t" + (cfg.NeedsCheck ? " (ยอดติดลบถูกตั้งเป็น 0)" : "") +
                   "\nค่าที่แก้จะใช้แทนยอดคำนวณ และต้องกด \"ยืนยันและส่ง\" ของวันนี้อีกครั้ง";
        var (act, value, note) = GlassDialog.EditValue(Window.GetWindow(View), $"แก้ยอด {cfg.Name} วันที่ {cut.Day:dd/MM}", info,
            cfg.Final, cfg.OverrideNote ?? "", cfg.Override != null);
        try
        {
            if (act == GlassDialog.EditAction.Save) _c.SetOverride(cut.Day, cfg.ConfigId, value, note);
            else if (act == GlassDialog.EditAction.Clear) _c.ClearOverride(cut.Day, cfg.ConfigId);
        }
        catch (Exception ex) { GlassDialog.Info(Window.GetWindow(View), "แก้ยอดไม่สำเร็จ: " + ex.Message, GlassDialog.Kind.Error); }
        Refresh();
    }

    async Task ConfirmAsync(DateTime day)
    {
        var cut = _c.CutOf(day);      // คำนวณใหม่ล่าสุดก่อนส่ง
        var sb = new System.Text.StringBuilder($"ส่งยอดวันที่ {cut.Day:dd/MM} เข้า {_c.AqSystemName}:\n");
        foreach (var x in cut.Configs)
            sb.AppendLine($" • {x.Name} (#{x.ConfigId}) = {x.Final:0.###} t" + (x.Override != null ? $"  ✎ แก้ด้วยมือ: {x.OverrideNote}" : x.NeedsCheck ? "  ⚠ ต้องตรวจ" : ""));
        var pend = SidecarController.PendingText(cut);
        if (pend.Length > 0) sb.Append("\n" + pend + "\n(ยังไม่นับในยอด / เป็นยอดประมาณการ)\n");
        bool today = cut.Day == DateTime.Today;
        if (today) sb.Append("\nยอดวันนี้ยังไม่จบวัน — ถ้ามีรถชั่งเพิ่มหลังส่ง ต้องกด \"ยืนยันและส่ง\" ของวันนี้อีกครั้ง (ทับค่าเดิม)\n");
        sb.Append("\nยืนยันและส่งหรือไม่?");
        if (!GlassDialog.Confirm(Window.GetWindow(View), sb.ToString(), "ยืนยันและส่ง",
                kind: today || pend.Length > 0 || cut.NeedsCheck ? GlassDialog.Kind.Warn : GlassDialog.Kind.Info, list: true))
            return;

        _status.Text = "กำลังส่ง…";
        _status.Foreground = Kit.B("Muted");
        _status.Visibility = Visibility.Visible;
        var (sent, err) = await _c.ConfirmDayAsync(day);
        if (err != null)
        {
            _status.Text = "ส่งไม่สำเร็จ: " + err;
            _status.Foreground = Kit.B("Red");
        }
        else
        {
            _status.Text = $"ยืนยันและส่งยอดวันที่ {day:dd/MM} แล้ว ({sent} configID) ✓";
            _status.Foreground = Kit.B("Green");
        }
        Refresh();
    }

    static Border Chip(string text, string brush, string fg) => new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(Kit.WithAlpha(Kit.B(brush), 0x33)),
        Child = Kit.T(text, 11, FontWeights.Bold, fg),
    };

    async void Login()
    {
        _btnLogin.IsEnabled = false;
        _status.Text = "กำลังเข้าสู่ระบบ...";
        _status.Visibility = Visibility.Visible;
        _status.Foreground = Kit.B("Muted");
        var (ok, msg) = await _c.AqLoginAsync(_user.Text.Trim(), _pass.Password);
        _btnLogin.IsEnabled = true;
        if (ok)
        {
            _pass.Clear();                     // ไม่เก็บรหัสผ่านไว้
            _dayBack = 0;                      // login แล้วเริ่มที่ "วันนี้" เสมอ
            _dayOpts[0].IsChecked = true;
        }
        _status.Text = msg;
        _status.Foreground = Kit.B(ok ? "Green" : "Red");
        Refresh();
    }
}
