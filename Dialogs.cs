using System.Windows;
using System.Windows.Controls;

namespace Tempo;

public static class Dialogs
{
    /// <summary>Glass-styled modal. onOk returns true to close (so it can validate and keep the dialog open).</summary>
    public static bool Show(string title, UIElement body, string okText, Func<bool>? onOk = null, double width = 480, bool danger = false)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow,
            ShowInTaskbar = false, FontSize = 14, UseLayoutRounding = true
        };
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        w.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        w.SourceInitialized += (_, _) => Backdrop.Apply(w);

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        root.Children.Add(Ui.Text(title, 20, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 16));
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
        return w.ShowDialog() == true;
    }

    public static bool Confirm(string title, string message, string okText, bool danger = false) =>
        Show(title, Ui.Text(message, 14, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14), okText, null, 440, danger);

    public static TextBlock ErrorLabel()
    {
        var t = Ui.Text("", 13, "DangerBrush", wrap: true);
        t.Margin = new Thickness(0, 0, 0, 10);
        t.Visibility = Visibility.Collapsed;
        return t;
    }

    public static void SetError(TextBlock t, string? msg)
    {
        t.Text = msg ?? ""; t.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }
}
