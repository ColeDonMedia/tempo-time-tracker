using System.Windows;
using System.Windows.Controls;

namespace Tempo;

public static class Dialogs
{
    /// <summary>Developer screenshot mode: when set, dialogs are rendered to PNG files here instead of being shown.</summary>
    public static string? CaptureDir;
    public static string CaptureTag = "dialog";

    /// <summary>Glass-styled modal. onOk returns true to close (so it can validate and keep the dialog open).</summary>
    public static bool Show(string title, UIElement body, string okText, Func<bool>? onOk = null, double width = 480, bool danger = false)
    {
        var w = new Window
        {
            Title = Loc.T(title), Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false, FontSize = 14, UseLayoutRounding = true,
            MaxHeight = SystemParameters.WorkArea.Height * 0.92
        };
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive && x.IsVisible)
                    ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsVisible);
        if (owner != null) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        w.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        w.SourceInitialized += (_, _) => Backdrop.Apply(w);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(Ui.Text(Loc.T(title), 20, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 16));
        root.Children.Add(body);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = Ui.Btn("Cancel", () => { }, "GhostButton"); cancel.IsCancel = true;
        cancel.Click += (_, _) => w.DialogResult = false;
        var ok = Ui.Btn(okText, () => { }, danger ? "DangerButton" : "PrimaryButton"); ok.IsDefault = true;
        ok.Margin = new Thickness(8, 0, 0, 0);
        ok.Click += (_, _) => { if (onOk == null || onOk()) w.DialogResult = true; };
        row.Children.Add(cancel); row.Children.Add(ok);
        root.Children.Add(row);

        w.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (CaptureDir != null)
        {
            // developer screenshot mode: draw the dialog to a PNG instead of waiting for a click
            w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -6000; w.Top = 0; w.Owner = null; w.ShowInTaskbar = false;
            w.Show(); w.UpdateLayout();
            var fe = (FrameworkElement)w.Content;
            int cw = (int)Math.Ceiling(w.ActualWidth), ch = (int)Math.Ceiling(Math.Min(w.ActualHeight, 1400));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(cw, ch, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var bg = new System.Windows.Media.DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Ui.B("PopupBrush"), null, new Rect(0, 0, cw, ch));
            rtb.Render(bg); rtb.Render(fe);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(System.IO.Path.Combine(CaptureDir, $"dialog-{CaptureTag}.png"))) enc.Save(fs);
            w.Close();
            return false;
        }
        return w.ShowDialog() == true;
    }

    public static bool Confirm(string title, string message, string okText, bool danger = false) =>
        Show(title, Ui.Text(Loc.T(message), 14, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14), okText, null, 440, danger);

    public static TextBlock ErrorLabel()
    {
        var t = Ui.Text("", 13, "DangerBrush", wrap: true);
        t.Margin = new Thickness(0, 0, 0, 10);
        t.Visibility = Visibility.Collapsed;
        return t;
    }

    public static void SetError(TextBlock t, string? msg)
    {
        t.Text = Loc.T(msg ?? ""); t.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }
}
