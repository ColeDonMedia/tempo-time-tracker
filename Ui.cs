using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
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
    public const string Ban = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 M5.6,5.6 L18.4,18.4";
    public const string Mini = "M4,5 H20 V19 H4 Z M12,12 H20 V19 H12 Z";
    public const string Expand = "M4,10 V4 H10 M20,14 V20 H14 M4,4 L11,11 M20,20 L13,13";
    public const string Pin = "M9,3 H15 L14.5,9 L18,13 H6 L9.5,9 Z M12,13 V21";
    public const string Users = "M9,11 A3.5,3.5 0 1 0 9,4 A3.5,3.5 0 1 0 9,11 M2.5,20 V18 A4,4 0 0 1 6.5,14 H11.5 A4,4 0 0 1 15.5,18 V20 M16,4.3 A3.5,3.5 0 0 1 16,10.7 M18.5,14.2 A4,4 0 0 1 21.5,18 V20";
    public const string Chart = "M4,20 V10 M10,20 V4 M16,20 V13 M22,20 H2";
    public const string Receipt = "M6,3 H18 V21 L15,19 L12,21 L9,19 L6,21 Z M9,8 H15 M9,12 H15";
    public const string Calendar = "M4,6 H20 V20 H4 Z M4,10 H20 M8,3 V7 M16,3 V7";
    public const string Search = "M10.5,17 A6.5,6.5 0 1 0 10.5,4 A6.5,6.5 0 1 0 10.5,17 M15.5,15.5 L20,20";
    public const string Copy = "M9,9 H20 V20 H9 Z M5,15 V4 H16";
    public const string Tag = "M3,12 L12,3 H20 V11 L11,20 Z M16,8 H16.01";
    public const string Undo = "M9,7 L4,12 L9,17 M4,12 H15 A5,5 0 0 1 15,22 H11";
    public const string Mail = "M3,6 H21 V18 H3 Z M3,6 L12,13 L21,6";
    public const string Bell ="M6,17 V11 A6,6 0 0 1 18,11 V17 L20,19 H4 Z M10,21 H14";
    public const string Link = "M10,14 A4,4 0 0 0 15.7,14.3 L19,11 A4,4 0 0 0 13.3,5.3 L12,6.6 M14,10 A4,4 0 0 0 8.3,9.7 L5,13 A4,4 0 0 0 10.7,18.7 L12,17.4";
    public const string Dollar = "M12,3 V21 M16,7 H10 A3,3 0 0 0 10,13 H14 A3,3 0 0 1 14,19 H8";
    public const string Upload = "M12,20 V9 M7,14 L12,9 L17,14 M5,4 H19";
    public const string Grid = "M4,4 H20 V20 H4 Z M4,12 H20 M12,4 V20";
    public const string Split = "M12,3 V21 M5,8 H9 M15,16 H19";
    public const string Merge = "M5,4 L12,12 L19,4 M12,12 V21";
    public const string ChevronLeft = "M15,5 L8,12 L15,19";
    public const string ChevronRight = "M9,5 L16,12 L9,19";
    public const string Archive = "M3,5 H21 V9 H3 Z M5,9 V19 H19 V9 M10,13 H14";
}

public static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public static T Dyn<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    { e.SetResourceReference(p, key); return e; }

    public static TextBlock Text(string s, double size = 14, string fg = "TextBrush", FontWeight? w = null, bool wrap = false, [CallerArgumentExpression("s")] string? expr = null)
    {
        var t = new TextBlock
        {
            Text = (expr != null && expr.StartsWith("\"")) ? Loc.T(s) : s, FontSize = size, FontWeight = w ?? FontWeights.Normal,
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
        text = Loc.T(text); tip = tip != null ? Loc.T(tip) : null;
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
        else if (text.Length > 0) System.Windows.Automation.AutomationProperties.SetName(b, text);   // icon + text buttons: screen readers can't see into the StackPanel
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
        b.Child = Text(Loc.T(text), 12, fgKey, FontWeights.SemiBold);
        return b;
    }

    public static StackPanel Field(string label, FrameworkElement ctl, double gap = 14)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, gap) };
        sp.Children.Add(Text(Loc.T(label), 12, "MutedBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 6));
        sp.Children.Add(ctl);
        System.Windows.Automation.AutomationProperties.SetName(ctl, Loc.T(label));
        return sp;
    }

    public static T HorizontalLeft<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Left; return e; }

    /// <summary>A labelled check box (label is translated).</summary>
    public static CheckBox Check(string label, bool value)
    {
        var c = new CheckBox { Content = new TextBlock { Text = Loc.T(label), TextWrapping = TextWrapping.Wrap }, IsChecked = value };
        System.Windows.Automation.AutomationProperties.SetName(c, Loc.T(label));
        return c;
    }

    /// <summary>Segmented tab strip; onSelect gets the index of the tab the user picked.</summary>
    public static StackPanel Tabs(string[] labels, int selected, Action<int> onSelect)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var group = "tabs" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < labels.Length; i++)
        {
            var rb = new RadioButton { Content = Loc.T(labels[i]), GroupName = group, IsChecked = i == selected, Style = (Style)Application.Current.FindResource("TabButton"), Margin = new Thickness(0, 0, 6, 0) };
            int idx = i;
            rb.Checked += (_, _) => onSelect(idx);
            sp.Children.Add(rb);
        }
        return sp;
    }

    public static Border Progress(double fraction, string fillBrushKey, double height = 6)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "FieldBorderBrush");
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, fillBrushKey);
        track.Child = fill;
        track.SizeChanged += (_, e) => fill.Width = Math.Max(0, Math.Min(1, fraction)) * e.NewSize.Width;
        return track;
    }

    public static T WithMargin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement
    { e.Margin = new Thickness(l, t, r, b); return e; }

    public static TextBox Input(string text = "", string? hint = null)
    {
        var t = new TextBox { Text = text, Tag = Loc.T(hint ?? "") };
        if (!string.IsNullOrEmpty(hint)) System.Windows.Automation.AutomationProperties.SetName(t, Loc.T(hint));   // Ui.Field replaces this with the real label
        return t;
    }

    public static ComboBox Combo<T>(IEnumerable<T> items, T? selected = default, string? member = null) where T : class
    {
        var c = new ComboBox { ItemsSource = items.ToList() };
        
        if (typeof(T) == typeof(string))
        {
            // Stored values stay English; only what the user sees is translated.
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new Binding { Converter = LocConverter.Instance });
            f.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            c.ItemTemplate = new DataTemplate { VisualTree = f };
        }
        if (selected != null) c.SelectedItem = selected;
        return c;
    }

    public static Grid Header(string title, string subtitle, params UIElement[] actions)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 24) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        var h = Text(Loc.T(title), 30, "TextBrush", FontWeights.SemiBold);
        left.Children.Add(h);
        left.Children.Add(Text(Loc.T(subtitle), 14, "MutedBrush").WithMargin(0, 4, 0, 0));
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
        row.Children.Add(Text(Loc.T(label).ToUpperInvariant(), 11, "MutedBrush", FontWeights.SemiBold));
        sp.Children.Add(row);
        sp.Children.Add(Text(value, 25, "TextBrush", FontWeights.SemiBold).WithMargin(0, 10, 0, 2));
        sp.Children.Add(Text(Loc.T(sub), 12.5, "MutedBrush", wrap: true));
        return Card(sp, 20);
    }

    public static StackPanel Empty(string icon, string title, string body, UIElement? action = null)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) };
        var ring = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Center, Child = Icon(icon, 24, "AccentBrush") };
        ring.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        sp.Children.Add(ring);
        sp.Children.Add(Text(Loc.T(title), 17, "TextBrush", FontWeights.SemiBold).WithMargin(0, 14, 0, 4).Center());
        sp.Children.Add(Text(Loc.T(body), 13.5, "MutedBrush", wrap: true).Center().WithMargin(0, 0, 0, action != null ? 16 : 0));
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

    public static readonly string[] ProjectColors = { "blue", "mint", "teal", "violet", "coral", "gold", "rose" };
    public static Color ProjColor(string name) => name.ToLowerInvariant() switch
    {
        "violet" => Color.FromRgb(0x8B, 0x7C, 0xF6),
        "blue" => Color.FromRgb(0x00, 0x68, 0xF0),
        "mint" => Color.FromRgb(0x00, 0xF1, 0xA0),
        "green" => Color.FromRgb(0x3D, 0xBE, 0x8B),
        "gold" => Color.FromRgb(0xF2, 0xB5, 0x44),
        "rose" => Color.FromRgb(0xEC, 0x6A, 0xA7),
        "teal" => Color.FromRgb(0x2B, 0xC4, 0xD0),
        "coral" => Color.FromRgb(0xFF, 0x77, 0x5E),
        _ => Color.FromRgb(0x00, 0x68, 0xF0),
    };

    // No thousands separators: "37,50" must read as 37.50 in a comma-decimal language, never 3750.
    const NumberStyles DecStyles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
    public static bool TryDec(string s, out decimal v) =>
        decimal.TryParse(s.Trim(), DecStyles, CultureInfo.CurrentCulture, out v) ||
        decimal.TryParse(s.Trim(), DecStyles, CultureInfo.InvariantCulture, out v);

    public static bool TryDate(string s, out DateTime d) =>
        DateTime.TryParseExact(s.Trim(), new[] { "yyyy-MM-dd", "yyyy/MM/dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ||
        DateTime.TryParse(s.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out d);

    // ---- logo: blue-to-mint ring, bold T, four clock ticks (redrawn as vectors from the supplied artwork) ----
    static Brush Grad(Point a, Point b, params (double At, string Hex)[] stops)
    {
        var g = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = a, EndPoint = b };
        foreach (var (at, hex) in stops) g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), at));
        return g;
    }

    public static FrameworkElement Logo(double size)
    {
        // Artwork lives in the original 1240-unit space; the outer canvas crops tightly around it (transparent background).
        var art = new Canvas { Width = 1240, Height = 1240, RenderTransform = new TranslateTransform(-94, -137) };
        void Add(UIElement e) => art.Children.Add(e);
        Path Stroke(string d, Brush brush, double w, PenLineCap cap = PenLineCap.Flat) =>
            new() { Data = Geometry.Parse(d), Stroke = brush, StrokeThickness = w, StrokeStartLineCap = cap, StrokeEndLineCap = cap };

        var ring = Grad(new Point(115, 1155), new Point(1105, 152), (0, "#0030D8"), (0.35, "#0068F0"), (0.6, "#00A8E0"), (0.8, "#00D8B8"), (1, "#00F1A0"));
        Add(Stroke("M1030.7,506.8 A446,446 0 1 1 746.8,227.8", ring, 97));

        var mint = new SolidColorBrush(Color.FromRgb(0x05, 0xEE, 0x8E));
        Add(Stroke("M790.4,244.6 A446,446 0 0 1 877.4,295.8", mint, 90));
        Add(Stroke("M913.2,325.8 A446,446 0 0 1 978.8,402.6", mint, 90));

        var tBrush = Grad(new Point(440, 950), new Point(1035, 457), (0, "#0060F8"), (0.5, "#00A0E8"), (1, "#00F1A0"));
        Add(new Path { Data = Geometry.Parse("M268,587 L298,520 Q320,457 395,457 L1035,457 L985,540 Q960,587 915,587 L745,587 Q712,590 705,620 L628,895 Q612,950 555,950 L440,950 L545,587 Z"), Fill = tBrush });
        Add(new Path { Data = Geometry.Parse("M545,587 L503,738 Q620,640 705,618 Q760,520 940,462 Q700,490 545,587 Z"), Fill = new SolidColorBrush(Color.FromArgb(70, 0x00, 0x30, 0xB0)) });

        var tick = new SolidColorBrush(Color.FromRgb(0x1F, 0x72, 0xC8));   // lighter than the original navy so it reads on dark and light
        foreach (var d in new[] { "M623,308 L623,355", "M263,680 L310,680", "M900,680 L948,680", "M623,968 L623,1013" })
            Add(Stroke(d, tick, 38, PenLineCap.Round));

        var crop = new Canvas { Width = 1030, Height = 1030, ClipToBounds = true };
        crop.Children.Add(art);
        return new Viewbox { Width = size, Height = size, Child = crop };
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

public sealed class LocConverter : IValueConverter
{
    public static readonly LocConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.T(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public abstract class PageBase : UserControl
{
    public virtual bool RebuildOnChange => true;
    public abstract void Build();
    public virtual void Tick() { }
}

