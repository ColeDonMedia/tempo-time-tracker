using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tempo;

/// <summary>Windows 11 Mica / Acrylic system backdrop with a solid fallback for older builds.</summary>
public static class Backdrop
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }

    public static bool Supported => Environment.OSVersion.Version.Build >= 22621;

    public static void Apply(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        int dark = App.IsDark ? 1 : 0;
        DwmSetWindowAttribute(h, 20, ref dark, 4);                    // immersive dark title bar
        int noColor = unchecked((int)0xFFFFFFFE);                        // DWMWA_COLOR_NONE: caption shows the backdrop, not the accent colour
        DwmSetWindowAttribute(h, 35, ref noColor, 4);
        int text = App.IsDark ? 0x00FAF4EE : 0x0033220E;              // caption text colour, COLORREF 0x00BBGGRR
        DwmSetWindowAttribute(h, 36, ref text, 4);
        int round = 2;                                                // DWMWCP_ROUND: soft corners, also on borderless windows
        DwmSetWindowAttribute(h, 33, ref round, 4);

        var mode = State.Data.Settings.Backdrop;
        var src = HwndSource.FromHwnd(h);
        if (!Supported || mode == "Solid" || src?.CompositionTarget == null)
        {
            var none = new Margins();
            DwmExtendFrameIntoClientArea(h, ref none);
            w.SetResourceReference(Window.BackgroundProperty, "BgBrush");
            return;
        }
        int type = mode == "Acrylic" ? 3 : 2;                         // DWMSBT_TRANSIENTWINDOW / DWMSBT_MAINWINDOW
        DwmSetWindowAttribute(h, 38, ref type, 4);
        src.CompositionTarget.BackgroundColor = Colors.Transparent;
        var all = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(h, ref all);
        w.Background = Brushes.Transparent;
    }
}
