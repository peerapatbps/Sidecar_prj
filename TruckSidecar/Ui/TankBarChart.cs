using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TruckSidecar.Ui;

/// <summary>
/// กราฟแท่งแนวตั้งของถังสารเคมีชนิดเดียว (ALUM หรือ PACL) — แต่ละแท่งสเกลตามพิกัดถังของตัวเอง
///   แท่งทึบ = ระดับ API ล่าสุด · กรอบประ = ระดับคาดการณ์ตอนนี้ · ชิปบน = สถานะ
///   ถังที่กำลังรับสาร (เลือกถังแล้ว ยังไม่ชั่งออก/ยังไม่บันทึกระดับหลังเติม) = แท่งลายทางสีเหลืองอำพัน + ป้ายทะเบียนรถเหนือถัง
/// </summary>
public sealed class TankBarChart : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TankBarChart),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    readonly Color _top, _bottom;
    List<SidecarController.TankView> _views = new();
    double _low = 20;
    bool _animated;
    int _hover = -1;

    public TankBarChart(Color top, Color bottom)
    {
        _top = top;
        _bottom = bottom;
        Height = 240;
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 20000);
    }

    public void SetData(List<SidecarController.TankView> views, double lowPercent)
    {
        _views = views;
        _low = lowPercent;
        if (!_animated && views.Any(v => v.ApiLevel != null || v.Pred != null))
        {
            _animated = true;
            BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        _hover = -1;
        InvalidateVisual();
    }

    const double TopPad = 50, BottomPad = 48;

    // ── สถานะ "กำลังรับสาร": เหลืองอำพันลายทางเฉียง — ไม่ชนสีสาร (ALUM ฟ้า / PACL ม่วง) และชิปสถานะ (เขียว/ส้ม/แดง) ──
    static readonly Color ActiveLight = Color.FromRgb(0xFF, 0xE1, 0x6B), ActiveDark = Color.FromRgb(0xFF, 0xB0, 0x00),
                          ActiveInk = Color.FromRgb(0x7A, 0x4A, 0x00);
    static readonly Brush ActiveFill = Stripes(ActiveLight, Color.FromRgb(0xFF, 0xC4, 0x1F), 1);
    static readonly Brush ActiveTrack = Stripes(Color.FromArgb(0x55, 0xFF, 0xF3, 0xC4), Color.FromArgb(0x40, 0xFF, 0xC4, 0x1F), 1);
    static readonly Pen ActiveBorder = Frozen(new Pen(new SolidColorBrush(ActiveDark), 2));
    static readonly Pen ActiveGlow = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xB0, 0x00)), 6));

    static Brush Stripes(Color a, Color b, double opacity)
    {
        // แถบเฉียง 45° กว้างแถบละ 6 px
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(0, 0, 6, 12))));
        var br = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None, Transform = new RotateTransform(45), Opacity = opacity,
        };
        br.Freeze();
        return br;
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        int n = Math.Max(1, _views.Count);
        double colW = w / n;
        double barW = Math.Min(54, colW * 0.56);
        double barTop = TopPad, barBottom = h - BottomPad, barH = Math.Max(10, barBottom - barTop);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // เส้นตาราง 0/50/100%
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0, 0, 0)), 1);
        foreach (var p in new[] { 0.0, 0.5, 1.0 })
        {
            double y = Math.Round(barBottom - barH * p) + 0.5;
            dc.DrawLine(grid, new Point(0, y), new Point(w, y));
        }

        for (int i = 0; i < _views.Count; i++)
        {
            var v = _views[i];
            double cx = colW * i + colW / 2;
            double x = cx - barW / 2;
            double cap = Math.Max(0.001, v.Tank.Capacity);
            bool repair = string.Equals(v.Status, "REPAIR", StringComparison.OrdinalIgnoreCase);
            bool active = v.Active.Count > 0;

            // ราง
            var track = new Rect(x, barTop, barW, barH);
            if (active)
            {
                dc.DrawRoundedRectangle(null, ActiveGlow, track, 12, 12);
                dc.DrawRoundedRectangle(ActiveTrack, ActiveBorder, track, 12, 12);
            }
            else
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x3D, 255, 255, 255)),
                    new Pen(new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)), 1), track, 12, 12);

            // แท่งทึบ = API (ถ้าไม่มี API ใช้ค่าคาดการณ์แบบจาง)
            double? shown = v.ApiLevel ?? v.Pred?.Level;
            if (shown is { } lvl)
            {
                double frac = Math.Clamp(lvl / cap, 0, 1) * Progress;
                double fh = barH * frac;
                if (fh > 0.5)
                {
                    Brush fill;
                    if (active) fill = ActiveFill;
                    else
                    {
                        var lg = new LinearGradientBrush(repair ? Color.FromRgb(0xC7, 0xC7, 0xCC) : _top,
                                                         repair ? Color.FromRgb(0x8E, 0x8E, 0x93) : _bottom, 90);
                        lg.Opacity = v.ApiLevel == null ? 0.45 : (v.StaleTs != null ? 0.7 : 1);
                        fill = lg;
                    }
                    var r = new Rect(x + 2, barBottom - fh + (fh >= barH - 2 ? 2 : 0), barW - 4, Math.Max(0, fh - 2 - (fh >= barH - 2 ? 2 : 0)));
                    dc.DrawRoundedRectangle(fill, null, r, 10, 10);
                    // เงาแสงบนแท่ง (glass highlight)
                    var shine = new LinearGradientBrush(Color.FromArgb(0x66, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0);
                    dc.DrawRoundedRectangle(shine, null, new Rect(r.X, r.Y, r.Width * 0.45, r.Height), 10, 10);
                }
            }

            // กรอบประ = คาดการณ์ตอนนี้ (แสดงเมื่อต่างจาก API พอสมควร)
            if (v.Pred is { } pred && v.ApiLevel is { } api && Math.Abs(pred.Level - api) / cap > 0.01)
            {
                double py = barBottom - barH * Math.Clamp(pred.Level / cap, 0, 1) * Progress;
                var dash = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x1C, 0x1C, 0x1E)), 1.4) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
                dc.DrawLine(dash, new Point(x - 4, py), new Point(x + barW + 4, py));
            }

            // % บนแท่ง
            double? now = v.Pred?.Level ?? v.ApiLevel;
            if (now is { } nl)
            {
                bool low = nl / cap * 100 <= _low;
                var pct = Text($"{nl / cap * 100:0}%", 12, FontWeights.Bold, low ? Color.FromRgb(0xD7, 0x00, 0x15) : Color.FromRgb(0x1C, 0x1C, 0x1E), ppd);
                double fy = barBottom - barH * Math.Clamp(nl / cap, 0, 1) * Progress;
                double ty = Math.Clamp(fy - pct.Height - 2, barTop + 2, barBottom - pct.Height - 2);
                DrawHalo(dc, pct, new Point(cx - pct.Width / 2, ty));
            }

            // ชิปสถานะด้านบน
            var (label, color) = (v.Status?.ToUpperInvariant()) switch
            {
                "ON" => ("ON", Color.FromRgb(0x30, 0xD1, 0x58)),
                "STANDBY" => ("STBY", Color.FromRgb(0xFF, 0x9F, 0x0A)),
                "REPAIR" => ("REPAIR", Color.FromRgb(0xFF, 0x3B, 0x30)),
                null => ("—", Color.FromRgb(0xAE, 0xAE, 0xB2)),
                var s => (s, Color.FromRgb(0xAE, 0xAE, 0xB2)),
            };
            var st = Text(label, 10, FontWeights.Bold, Colors.White, ppd);
            var chip = new Rect(cx - st.Width / 2 - 7, 2, st.Width + 14, 18);
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, chip, 9, 9);
            dc.DrawText(st, new Point(chip.X + 7, chip.Y + (18 - st.Height) / 2));

            // ป้ายรถที่กำลังลงถังนี้ (ใต้ชิปสถานะ เหนือถัง)
            if (active)
            {
                // คอลัมน์แคบ (4 ถัง/แถว) → แสดงทะเบียนอย่างเดียว ลูกศรชี้ลงถังสื่อว่า "จากรถคันนี้" · หลายคัน = +N (รายละเอียดใน tooltip)
                string more = v.Active.Count > 1 ? $" +{v.Active.Count - 1}" : "";
                double maxW = Math.Max(20, colW - 10);
                FormattedText pt = Text(v.Active[0].Plate + more, 10.5, FontWeights.Bold, ActiveInk, ppd);
                if (pt.Width > maxW) pt = Text(v.Active[0].Plate + more, 9.5, FontWeights.Bold, ActiveInk, ppd);
                pt.MaxTextWidth = maxW;
                pt.MaxLineCount = 1;
                pt.Trimming = TextTrimming.CharacterEllipsis;
                double pw = Math.Min(pt.Width, maxW) + 10;
                var pill = new Rect(cx - pw / 2, 24, pw, 20);
                dc.DrawRoundedRectangle(new SolidColorBrush(ActiveLight), ActiveBorder, pill, 10, 10);
                // ลูกศรชี้ลงถัง
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(cx - 5, pill.Bottom - 1), true, true);
                    g.LineTo(new Point(cx + 5, pill.Bottom - 1), true, false);
                    g.LineTo(new Point(cx, pill.Bottom + 5), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(new SolidColorBrush(ActiveDark), null, tri);
                dc.DrawText(pt, new Point(pill.X + 5, pill.Y + (20 - pt.Height) / 2));
            }

            // ชื่อ + ปริมาณ
            var name = Text(v.Tank.Name, 12.5, FontWeights.Bold, Color.FromRgb(0x1C, 0x1C, 0x1E), ppd);
            dc.DrawText(name, new Point(cx - name.Width / 2, barBottom + 5));
            string sub = now is { } q ? $"{q:0.#}/{cap:0.#} m³" : "ไม่มีข้อมูล";
            if (v.StaleTs is { } stale) sub += $" ·{stale:HH:mm}";
            var subT = Text(sub, 10.5, FontWeights.Normal, Color.FromRgb(0x6E, 0x6E, 0x73), ppd);
            subT.MaxTextWidth = colW - 2;
            subT.Trimming = TextTrimming.CharacterEllipsis;
            subT.MaxLineCount = 1;
            dc.DrawText(subT, new Point(cx - Math.Min(subT.Width, colW - 2) / 2, barBottom + 24));
        }

        // ให้ hit-test ได้ทั้งพื้นที่ (tooltip)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
    }

    static void DrawHalo(DrawingContext dc, FormattedText t, Point p)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)), null,
            new Rect(p.X - 4, p.Y - 1, t.Width + 8, t.Height + 2), 6, 6);
        dc.DrawText(t, p);
    }

    FormattedText Text(string s, double size, FontWeight weight, Color color, double ppd) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Leelawadee UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, new SolidColorBrush(color), ppd);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_views.Count == 0) return;
        int i = (int)Math.Clamp(e.GetPosition(this).X / (ActualWidth / _views.Count), 0, _views.Count - 1);
        if (i == _hover) return;
        _hover = i;
        ToolTip = Tip(_views[i]);
    }

    static string Tip(SidecarController.TankView v)
    {
        var lines = new List<string> { $"{v.Tank.Name}  (พิกัด {v.Tank.Capacity:0.#} m³)" };
        foreach (var r in v.Active)
        {
            var f = r.Fills.FirstOrDefault(x => x.Tank == v.Tank.Name);
            var wait = new List<string>();
            if (!r.StepWeighOut) wait.Add("ชั่งออก");
            if (f?.LevelAfter == null) wait.Add("ระดับหลังเติม");
            lines.Add($"▶ {(r.IsWash ? "กำลังล้างถัง" : "กำลังรับสาร")}: {r.Plate} #{r.TrackId.TrimStart('0')}"
                      + (r.WeighInAt is { } wi ? $" เข้า {wi:HH:mm}" : "") + $" — รอ{string.Join(" + ", wait)}");
        }
        if (v.Last == null && v.Pred == null) { lines.Add("ยังไม่มีข้อมูลระดับ"); return string.Join(Environment.NewLine, lines); }
        if (v.Last != null) lines.Add($"API ล่าสุด: {v.Last.Level:0.#} m³  ณ {v.Last.Ts:HH:mm}  [{v.Status}]");
        if (v.Pred is { } p)
        {
            lines.Add($"คาดการณ์ตอนนี้: ~{p.Level:0.#} m³");
            lines.Add($"ฐาน: {p.BaseSource} {p.BaseLevel:0.#} m³ ณ {p.BaseTs:HH:mm}");
            lines.Add(p.RatePerHour is { } rate
                ? $"อัตราใช้: {rate:0.00} m³/ชม.  ({p.RateSource})"
                : "อัตราใช้: ยังคำนวณไม่ได้ (ต้องมีข้อมูล API อย่างน้อย 2 รอบ หรือตั้ง defaultUsagePerHour)");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
