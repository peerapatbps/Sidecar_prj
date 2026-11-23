using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TruckSidecar.Ui;

/// <summary>
/// เลือกรายการจาก WDATA ด้วยมือ (สำรอง เมื่อระบบตรวจจับอัตโนมัติไม่เจอ)
/// ทะเบียน/Track ID มาจากโปรแกรมชั่งเท่านั้น พนักงานพิมพ์เองไม่ได้
/// </summary>
internal sealed class ScalePickerWindow : Window
{
    public ScaleRow? Selected { get; private set; }

    public ScalePickerWindow(Window owner, List<ScaleRow> rows, Func<string, UnloadRecord?> linked, Func<string, string?> chemOf)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 440;
        Height = 520;
        FontFamily = new FontFamily("Leelawadee UI");
        if (owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

        var head = Kit.Header("เลือกรถจากตาชั่ง");
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var hint = Kit.T(rows.Count == 0
                ? "ไม่พบรายการชั่งในช่วง 24 ชม. — ให้ชั่งเข้าในโปรแกรมชั่งก่อน"
                : "ตัวหนา = ชั่งเข้าแล้วยังไม่ชั่งออก · แตะเพื่อเลือก", 12, null, "Muted", new Thickness(0, -4, 0, 8));
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var cancel = new Button { Content = "ยกเลิก", Style = Kit.S("GlassButton"), IsCancel = true, Margin = new Thickness(0, 10, 0, 0) };
        cancel.Click += (_, _) => Close();
        DockPanel.SetDock(cancel, Dock.Bottom);
        root.Children.Add(cancel);

        var list = new StackPanel();
        foreach (var r in rows.OrderBy(r => r.WeighedOut).ThenByDescending(r => r.Ticket1))
        {
            var rec = linked(r.Ticket1);
            var chem = chemOf(r.Product);
            string state = (r.WeighedOut ? "ชั่งออกแล้ว" : "ยังไม่ชั่งออก")
                         + (chem == null ? " · ไม่ใช่สารเคมี" : $" · {chem}")
                         + (rec == null ? "" : rec.IsComplete ? " · บันทึกครบแล้ว" : " · มีบันทึกค้าง");

            var g = new Grid { Margin = new Thickness(10, 7, 10, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(Kit.T($"{r.Truck}   #{r.Ticket1Short}", 14, r.WeighedOut ? FontWeights.Normal : FontWeights.Bold));
            left.Children.Add(Kit.T(state, 11.5, null, "Muted"));
            g.Children.Add(left);
            var right = Kit.T($"{r.WeighInAt:dd/MM HH:mm}\n{r.W1:#,0} กก.", 11.5, null, "Ink2");
            right.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            var btn = new Button
            {
                Style = Kit.S("FlatButton"), Content = g, Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromArgb(0xB3, 255, 255, 255)),
                Opacity = rec?.IsComplete == true ? 0.5 : 1,
            };
            btn.Click += (_, _) => { Selected = r; DialogResult = true; };
            list.Children.Add(btn);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Content = new Border
        {
            Margin = new Thickness(14),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0xF7, 0xF2, 0xF4, 0xF8)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25 },
            Child = root,
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
    }
}
