using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TruckSidecar.Ui;

/// <summary>กล่องข้อความแบบ iOS alert (แทน MessageBox)</summary>
internal static class GlassDialog
{
    public enum Kind { Info, Warn, Error }

    public static void Info(Window? owner, string message, Kind kind = Kind.Info, bool list = false) =>
        Show(owner, message, kind, "ตกลง", null, list);

    public static bool Confirm(Window? owner, string message, string ok = "ยืนยัน", string cancel = "ยกเลิก", Kind kind = Kind.Warn, bool list = false) =>
        Show(owner, message, kind, ok, cancel, list);

    public enum EditAction { Cancel, Save, Clear }

    /// <summary>
    /// แก้ยอดตัด: ช่องตัวเลข (≥ 0) + หมายเหตุ (บังคับ) — canClear = มีค่าที่แก้ไว้แล้ว แสดงปุ่ม "ใช้ยอดคำนวณ"
    /// </summary>
    public static (EditAction Action, double Value, string Note) EditValue(Window? owner, string title, string info,
        double current, string note, bool canClear)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, SizeToContent = SizeToContent.Height,
            Width = 380, FontFamily = new FontFamily("Leelawadee UI"),
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true }) w.Owner = owner;
        var result = (EditAction.Cancel, current, note);

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        panel.Children.Add(Kit.T(title, 15, FontWeights.Bold, "Ink", new Thickness(0, 0, 0, 4)));
        panel.Children.Add(Kit.T(info, 12, null, "Muted", new Thickness(0, 0, 0, 12)));
        var val = Kit.Field();
        val.Text = current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var memo = Kit.Field();
        memo.Text = note;
        memo.TextWrapping = TextWrapping.Wrap;
        memo.AcceptsReturn = true;
        memo.MinHeight = 60;
        panel.Children.Add(Kit.Labeled("ยอดที่จะส่ง (ตัน)", val));
        panel.Children.Add(Kit.Labeled("หมายเหตุ (บังคับ)", memo));
        var err = Kit.T("", 12, FontWeights.SemiBold, "Red", new Thickness(2, 0, 0, 10));
        panel.Children.Add(err);

        var ok = new Button { Content = "บันทึก", Style = Kit.S("PrimaryButton"), IsDefault = true };
        ok.Click += (_, _) =>
        {
            var s = val.Text.Trim().Replace(',', '.');
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0)
            { err.Text = "ยอดต้องเป็นตัวเลขตั้งแต่ 0 ขึ้นไป (Aquadat ไม่รับค่าติดลบ)"; return; }
            if (string.IsNullOrWhiteSpace(memo.Text)) { err.Text = "ต้องกรอกหมายเหตุ เช่น สาเหตุที่แก้ยอด"; return; }
            result = (EditAction.Save, Math.Round(v, 3), memo.Text.Trim());
            w.Close();
        };
        var cancel = new Button { Content = "ยกเลิก", Style = Kit.S("GlassButton"), IsCancel = true };
        cancel.Click += (_, _) => w.Close();
        panel.Children.Add(Kit.Two(cancel, ok, 10));
        if (canClear)
        {
            var clear = new Button { Content = "ใช้ยอดคำนวณ (ยกเลิกการแก้)", Style = Kit.S("GlassButton"), Margin = new Thickness(0, 8, 0, 0) };
            clear.Click += (_, _) => { result = (EditAction.Clear, current, note); w.Close(); };
            panel.Children.Add(clear);
        }

        w.Content = new Border
        {
            Margin = new Thickness(16), CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0xF7, 0xF7, 0xF8, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25 },
            Child = panel,
        };
        w.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) try { w.DragMove(); } catch { } };
        w.Loaded += (_, _) => { val.Focus(); val.SelectAll(); };
        w.ShowDialog();
        return result;
    }

    /// <param name="list">ข้อความหลายบรรทัด (สรุปตัดยอด / รายการคงค้าง): ชิดซ้าย กว้างขึ้น และเลื่อนได้</param>
    static bool Show(Window? owner, string message, Kind kind, string okText, string? cancelText, bool list = false)
    {
        var (glyph, brush) = kind switch
        {
            Kind.Error => ("✕", "RedDot"),
            Kind.Warn => ("!", "OrangeDot"),
            _ => ("i", "Accent"),
        };

        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.Height,
            Width = list ? 420 : 340,
            FontFamily = new FontFamily("Leelawadee UI"),
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true }) w.Owner = owner;

        bool result = false;
        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Kit.B(brush),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = glyph, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        panel.Children.Add(icon);
        var text = Kit.T(message, list ? 12.5 : 13.5, null, "Ink", list ? new Thickness(0) : new Thickness(0, 0, 0, 16));
        text.TextAlignment = list ? TextAlignment.Left : TextAlignment.Center;
        if (list)
            panel.Children.Add(new ScrollViewer
            {
                Content = text, MaxHeight = 420, Margin = new Thickness(0, 0, 0, 16),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        else
            panel.Children.Add(text);

        var ok = new Button { Content = okText, Style = Kit.S("PrimaryButton"), IsDefault = true, MinWidth = 110 };
        ok.Click += (_, _) => { result = true; w.Close(); };
        if (kind == Kind.Error && cancelText != null) ok.Background = Kit.B("RedDot");
        if (cancelText != null)
        {
            var cancel = new Button { Content = cancelText, Style = Kit.S("GlassButton"), IsCancel = true, MinWidth = 110 };
            cancel.Click += (_, _) => w.Close();
            panel.Children.Add(Kit.Two(cancel, ok, 10));
        }
        else
        {
            ok.IsCancel = true;
            panel.Children.Add(ok);
        }

        w.Content = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0xF7, 0xF7, 0xF8, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25 },
            Child = panel,
        };
        w.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { w.DragMove(); } catch { } };
        w.ShowDialog();
        return result;
    }
}
