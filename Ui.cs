using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Tempo;

public static class Icons
{
    public const string Home = "M3,11 L12,3 L21,11 M5,9.5 V20 H19 V9.5";
    public const string Folder = "M3,6 H9 L11,8 H21 V19 H3 Z";
    public const string Clock = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 M12,7 V12 L15.5,14";
    public const string Doc = "M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M9,13 H16 M9,17 H16";
    public const string Sliders = "M4,7 H20 M4,17 H20 M9,4 V10 M15,14 V20";
    public const string Play = "M7,4 L19,12 L7,20 Z";
    public const string Pause = "M7,4 H10 V20 H7 Z M14,4 H17 V20 H14 Z";
    public const string Stop = "M6,6 H18 V18 H6 Z";
    public const string Plus = "M12,5 V19 M5,12 H19";
    public const string Edit = "M4,20 L5,15 L16,4 L20,8 L9,19 Z M14,6 L18,10";
    public const string Trash = "M5,7 H19 M9,7 V4 H15 V7 M7,7 L8,20 H16 L17,7";
    public const string Download = "M12,4 V15 M7,10 L12,15 L17,10 M5,20 H19";
    public const string Check = "M5,12.5 L10,17.5 L19,7";
    public const string Archive = "M3,5 H21 V9 H3 Z M5,9 V19 H19 V9 M10,13 H14";
}

public static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public static T Dyn<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    { e.SetResourceReference(p, key); return e; }

    public static TextBlock Text(string s, double size = 14, string fg = "TextBrush", FontWeight? w = null, bool wrap = false)
    {
        var t = new TextBlock
        {
            Text = s, FontSize = size, FontWeight = w ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        return t;
    }

    public static Border Card(UIElement child, double pad = 22)
    {
        var b = new Border { Child = child, Padding = new Thickness(pad) };
        b.Style = (Style)Application.Current.FindResource("Card");
        return b;
    }

    public static FrameworkElement Icon(string data, double size = 18, string stroke = "TextBrush", bool fill = false)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        p.SetResourceReference(Shape.StrokeProperty, stroke);
        if (fill) p.SetResourceReference(Shape.FillProperty, stroke);
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { p } } };
    }

    public static Button Btn(string text, Action click, string style = "GlassButton", string? icon = null, string? tip = null)
    {
        var b = new Button { Style = (Style)Application.Current.FindResource(style) };
        if (icon != null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var fg = style == "PrimaryButton" ? "OnAccentBrush" : style == "DangerButton" ? "DangerBrush" : "TextBrush";
            sp.Children.Add(Icon(icon, 16, fg, icon is Icons.Play or Icons.Pause or Icons.Stop));
            if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        else b.Content = text;
        if (tip != null) { b.ToolTip = tip; System.Windows.Automation.AutomationProperties.SetName(b, tip); }
        b.Click += (_, _) => click();
        return b;
    }

    public static Button IconBtn(string icon, string tip, Action click, bool danger = false)
    {
        var b = Btn("", click, "IconButton", icon, tip);
        if (danger) b.Content = Icon(icon, 16, "DangerBrush");
        else b.Content = Icon(icon, 16, "MutedBrush");
        return b;
    }

    public static Border Pill(string text, string fgKey, string bgKey)
    {
        var b = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, bgKey);
        b.Child = Text(text, 12, fgKey, FontWeights.SemiBold);
        return b;
    }

    public static StackPanel Field(string label, FrameworkElement ctl, double gap = 14)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, gap) };
        sp.Children.Add(Text(label, 12, "MutedBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 6));
        sp.Children.Add(ctl);
        return sp;
    }

    public static T HorizontalLeft<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Left; return e; }

    public static T WithMargin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement
    { e.Margin = new Thickness(l, t, r, b); return e; }

    public static TextBox Input(string text = "", string? hint = null)
    {
        var t = new TextBox { Text = text, Tag = hint ?? "" };
        return t;
    }

    public static ComboBox Combo<T>(IEnumerable<T> items, T? selected = default, string? member = null) where T : class
    {
        var c = new ComboBox { ItemsSource = items.ToList() };
        
        if (selected != null) c.SelectedItem = selected;
        return c;
    }

    public static Grid Header(string title, string subtitle, params UIElement[] actions)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 24) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        var h = Text(title, 30, "TextBrush", FontWeights.SemiBold);
        left.Children.Add(h);
        left.Children.Add(Text(subtitle, 14, "MutedBrush").WithMargin(0, 4, 0, 0));
        g.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) { if (a is FrameworkElement fe) fe.Margin = new Thickness(10, 0, 0, 0); right.Children.Add(a); }
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    public static Border Metric(string label, string value, string sub, string accent = "AccentBrush")
    {
        var sp = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        dot.SetResourceReference(Shape.FillProperty, accent);
        row.Children.Add(dot);
        row.Children.Add(Text(label.ToUpperInvariant(), 11, "MutedBrush", FontWeights.SemiBold));
        sp.Children.Add(row);
        sp.Children.Add(Text(value, 25, "TextBrush", FontWeights.SemiBold).WithMargin(0, 10, 0, 2));
        sp.Children.Add(Text(sub, 12.5, "MutedBrush"));
        return Card(sp, 20);
    }

    public static StackPanel Empty(string icon, string title, string body, UIElement? action = null)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) };
        var ring = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Center, Child = Icon(icon, 24, "AccentBrush") };
        ring.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        sp.Children.Add(ring);
        sp.Children.Add(Text(title, 17, "TextBrush", FontWeights.SemiBold).WithMargin(0, 14, 0, 4).Center());
        sp.Children.Add(Text(body, 13.5, "MutedBrush", wrap: true).Center().WithMargin(0, 0, 0, action != null ? 16 : 0));
        if (action != null) { ((FrameworkElement)action).HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(action); }
        return sp;
    }

    public static T Center<T>(this T e) where T : FrameworkElement
    {
        e.HorizontalAlignment = HorizontalAlignment.Center;
        if (e is TextBlock tb) { tb.TextAlignment = TextAlignment.Center; tb.MaxWidth = 340; }
        return e;
    }

    public static Ellipse Dot(string colorName, double size = 10)
    {
        var e = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(ProjColor(colorName)), VerticalAlignment = VerticalAlignment.Center };
        return e;
    }

    public static readonly string[] ProjectColors = { "coral", "violet", "blue", "green", "gold", "rose", "teal" };
    public static Color ProjColor(string name) => name.ToLowerInvariant() switch
    {
        "violet" => Color.FromRgb(0x8B, 0x7C, 0xF6),
        "blue" => Color.FromRgb(0x4C, 0x9A, 0xFF),
        "green" => Color.FromRgb(0x3D, 0xBE, 0x8B),
        "gold" => Color.FromRgb(0xF2, 0xB5, 0x44),
        "rose" => Color.FromRgb(0xEC, 0x6A, 0xA7),
        "teal" => Color.FromRgb(0x2B, 0xC4, 0xD0),
        _ => Color.FromRgb(0xFF, 0x77, 0x5E),
    };

    public static bool TryDec(string s, out decimal v) =>
        decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out v) ||
        decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out v);

    public static bool TryDate(string s, out DateTime d) =>
        DateTime.TryParseExact(s.Trim(), new[] { "yyyy-MM-dd", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ||
        DateTime.TryParse(s.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out d);

    // ---- logo (matches TempoLogo.svg) ----
    public static FrameworkElement Logo(double size)
    {
        var coral = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x5E));
        var ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x2B, 0x34));
        var c = new Canvas { Width = 128, Height = 128 };
        var tile = new Rectangle { Width = 120, Height = 120, RadiusX = 28, RadiusY = 28, Fill = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2)) };
        Canvas.SetLeft(tile, 4); Canvas.SetTop(tile, 4);
        c.Children.Add(tile);
        c.Children.Add(new Path { Data = Geometry.Parse("M91,37 A43,43 0 1 0 92,93"), Stroke = coral, StrokeThickness = 11, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        c.Children.Add(new Path { Data = Geometry.Parse("M53,74 L83,47"), Stroke = ink, StrokeThickness = 11, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        var dot = new Ellipse { Width = 14, Height = 14, Fill = coral };
        Canvas.SetLeft(dot, 81); Canvas.SetTop(dot, 36);
        c.Children.Add(dot);
        return new Viewbox { Width = size, Height = size, Child = c };
    }

    public static BitmapSource LogoBitmap(int px)
    {
        var v = Logo(px);
        v.Measure(new Size(px, px)); v.Arrange(new Rect(0, 0, px, px)); v.UpdateLayout();
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v); rtb.Freeze();
        return rtb;
    }
}

public abstract class PageBase : UserControl
{
    public virtual bool RebuildOnChange => true;
    public abstract void Build();
    public virtual void Tick() { }
}

