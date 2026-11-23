using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TruckSidecar.Ui;

/// <summary>ตัวช่วยสร้าง control ตามสไตล์ใน Glass.xaml (UI สร้างด้วยโค้ด ไม่ต้องมี XAML ต่อหน้า)</summary>
internal static class Kit
{
    public static Brush B(string key) => (Brush)Application.Current.Resources[key];
    public static Style S(string key) => (Style)Application.Current.Resources[key];

    public static TextBlock T(string text, double size = 13, FontWeight? weight = null, string brush = "Ink", Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = B(brush),
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0),
    };

    public static ContentControl Card(UIElement content, Thickness? margin = null) => new()
    {
        Style = S("Card"),
        Content = content,
        Margin = margin ?? new Thickness(0, 0, 0, 10),
    };

    /// <summary>หัวการ์ด: ชื่อหนา + ข้อความเล็กด้านขวา</summary>
    public static Grid Header(string title, TextBlock? right = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(T(title, 15, FontWeights.Bold));
        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
        }
        return g;
    }

    public static TextBox Field(bool readOnly = false) => new() { Style = S("Field"), IsReadOnly = readOnly, IsTabStop = !readOnly };

    /// <summary>ป้ายเล็ก + ช่องกรอกใต้ป้าย</summary>
    public static StackPanel Labeled(string label, UIElement field, Thickness? margin = null)
    {
        System.Windows.Automation.AutomationProperties.SetName(field, label);
        var sp = new StackPanel { Margin = margin ?? new Thickness(0, 0, 0, 8) };
        sp.Children.Add(T(label, 11.5, FontWeights.SemiBold, "Muted", new Thickness(4, 0, 0, 3)));
        sp.Children.Add(field);
        return sp;
    }

    /// <summary>แบ่ง 2 คอลัมน์เท่ากัน</summary>
    public static Grid Two(UIElement a, UIElement b, double gap = 8)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(a);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return g;
    }

    public static Ellipse Dot(string brush, double size = 8) => new()
    {
        Width = size, Height = size, Fill = B(brush), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>pill สถานะ (จุดสี + ข้อความ)</summary>
    public static Border Pill(out Ellipse dot, out TextBlock text)
    {
        dot = Dot("GreenDot", 7);
        text = T("", 11.5, FontWeights.SemiBold, "Ink2");
        text.TextWrapping = TextWrapping.NoWrap;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        dot.Margin = new Thickness(0, 0, 5, 0);
        sp.Children.Add(dot);
        sp.Children.Add(text);
        return new Border
        {
            CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(0x8C, 255, 255, 255)),
            BorderBrush = B("Hairline"), BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 9, 3),
            Child = sp, Margin = new Thickness(6, 0, 0, 0),
        };
    }

    /// <summary>ป้ายตัวเลขบนแท็บ</summary>
    public static Border Badge(out TextBlock text)
    {
        text = T("", 10.5, FontWeights.Bold, "Ink");
        text.Foreground = Brushes.White;
        text.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            CornerRadius = new CornerRadius(8), Background = B("RedDot"), MinWidth = 16, Height = 16,
            Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0), Child = text,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
    }

    public static void FadeIn(UIElement e, double ms = 200)
    {
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>สุทธิแบบมีเครื่องหมาย: "สุทธิ +14,205 กก." (รับ) / "นำออก −1,300 กก." (ล้างถัง)</summary>
    public static string NetText(double? netKg) => netKg switch
    {
        null => "ยังไม่ชั่งออก",
        < 0 => $"นำออก −{-netKg:#,0} กก.",
        _ => $"สุทธิ +{netKg:#,0} กก.",
    };

    public static Color WithAlpha(Brush b, byte a) => b is SolidColorBrush s ? Color.FromArgb(a, s.Color.R, s.Color.G, s.Color.B) : Colors.Transparent;
}
