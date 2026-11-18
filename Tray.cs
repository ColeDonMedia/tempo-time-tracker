using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Tempo;

/// <summary>Notification-area icon built on the native Shell_NotifyIcon API (no WinForms): click to open, right-click for timer controls.</summary>
public sealed class TrayService : IDisposable
{
    const int WM_APP_TRAY = 0x8001, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_NULL = 0;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_GRAYED = 1, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2;
    const int ID_OPEN = 1, ID_TOGGLE = 2, ID_STOP = 3, ID_QUIT = 4, ID_MINI = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA d);
    [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint ver, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr m, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr hwnd, IntPtr p);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);

    readonly Window win;
    readonly HwndSource source;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    IntPtr hicon;
    string lastText = "";
    public bool Quitting { get; private set; }

    public TrayService(Window window)
    {
        win = window;
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Ui.LogoBitmap(32)));
        using var ms = new MemoryStream(); enc.Save(ms);
        var png = ms.ToArray();
        hicon = CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, 32, 32, 0);

        source = new HwndSource(new HwndSourceParameters("TempoTray") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        source.AddHook(WndProc);
        Add();
    }

    NOTIFYICONDATA Data(string tip) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = source.Handle, uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_APP_TRAY, hIcon = hicon, szTip = tip, szInfo = "", szInfoTitle = ""
    };

    void Add() { var d = Data(lastText.Length > 0 ? lastText : "Tempo"); Shell_NotifyIcon(NIM_ADD, ref d); }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == taskbarCreated) { Add(); }
        else if (msg == WM_APP_TRAY)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            if (ev == WM_LBUTTONUP) { Show(); handled = true; }
            else if (ev == WM_RBUTTONUP) { ShowMenu(); handled = true; }
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        var m = CreatePopupMenu();
        AppendMenu(m, MF_STRING, (UIntPtr)ID_OPEN, Loc.T("Open Tempo"));
        AppendMenu(m, MF_STRING, (UIntPtr)ID_MINI, Loc.T("Mini timer"));
        AppendMenu(m, MF_STRING, (UIntPtr)ID_TOGGLE, Loc.T(State.Running ? "Pause timer" : State.HasTimer ? "Resume timer" : "Start / pause timer"));
        AppendMenu(m, State.HasTimer ? MF_STRING : MF_GRAYED, (UIntPtr)ID_STOP, Loc.T("Stop & save timer"));
        AppendMenu(m, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenu(m, MF_STRING, (UIntPtr)ID_QUIT, Loc.T("Quit Tempo"));
        GetCursorPos(out var p);
        SetForegroundWindow(source.Handle);
        int cmd = TrackPopupMenuEx(m, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, source.Handle, IntPtr.Zero);
        PostMessage(source.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(m);
        switch (cmd)
        {
            case ID_OPEN: Show(); break;
            case ID_MINI: MainWindow.Instance.ShowMini(); break;
            case ID_TOGGLE: MainWindow.Instance.ToggleTimerShortcut(); break;
            case ID_STOP:
                if (State.HasTimer)
                {
                    var pr = State.Proj(State.Data.Timer.ProjectId); var ms = State.ElapsedMs; var s = State.StopTimer();
                    State.Toast(s != null ? Loc.F("Saved {0} to {1}", Fmt.Hm(ms), pr?.Name) : "Timer cleared");
                }
                break;
            case ID_QUIT: Quitting = true; Application.Current.Shutdown(); break;
        }
    }

    public void Show() => MainWindow.Instance.ShowFull();

    public void Update()
    {
        var p = State.Proj(State.Data.Timer.ProjectId);
        var text = State.HasTimer ? $"Tempo · {(State.Running ? "" : Loc.T("paused") + " · ")}{Fmt.Clock(State.ElapsedMs)}{(p != null ? " · " + p.Name : "")}" : "Tempo";
        if (text.Length > 120) text = text[..120];
        if (text == lastText) return;
        lastText = text;
        var d = Data(text); Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    /// <summary>A Windows notification from the tray icon (used for reminders while the window is hidden).</summary>
    public void Balloon(string title, string text)
    {
        var d = Data(lastText.Length > 0 ? lastText : "Tempo");
        d.uFlags |= 0x10; // NIF_INFO
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..255] : text;
        d.dwInfoFlags = 1; // NIIF_INFO
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    public void Dispose()
    {
        var d = Data(""); Shell_NotifyIcon(NIM_DELETE, ref d);
        source.Dispose();
        if (hicon != IntPtr.Zero) DestroyIcon(hicon);
    }
}
