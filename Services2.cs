using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Tempo;

/// <summary>A shortcut that works in every app (RegisterHotKey), used to start / pause the timer.</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int WM_HOTKEY = 0x0312, HotkeyId = 0x7E57;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;

    public static readonly string[] Choices = { "Off", "Ctrl+Alt+Space", "Ctrl+Alt+T", "Ctrl+Shift+Space" };

    readonly HwndSource source;
    bool registered;
    public event Action? Pressed;

    public HotkeyService()
    {
        source = new HwndSource(new HwndSourceParameters("TempoHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        source.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId) { Pressed?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }

    public static (uint Mods, uint Key) Parse(string combo) => combo switch
    {
        "Ctrl+Alt+Space" => (MOD_CONTROL | MOD_ALT, 0x20u),
        "Ctrl+Alt+T" => (MOD_CONTROL | MOD_ALT, 0x54u),
        "Ctrl+Shift+Space" => (MOD_CONTROL | MOD_SHIFT, 0x20u),
        _ => (0u, 0u),
    };

    /// <summary>Registers the shortcut (or removes it for "Off"). False means another app already owns it.</summary>
    public bool Apply(string combo)
    {
        if (registered) { UnregisterHotKey(source.Handle, HotkeyId); registered = false; }
        var (mods, key) = Parse(combo);
        if (key == 0) return true;
        registered = RegisterHotKey(source.Handle, HotkeyId, mods | MOD_NOREPEAT, key);
        return registered;
    }

    public void Dispose()
    {
        if (registered) UnregisterHotKey(source.Handle, HotkeyId);
        source.Dispose();
    }
}

/// <summary>Start Tempo (quietly, in the tray) when the user signs in to Windows.</summary>
public static class StartupService
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Tempo";

    public static bool Enabled
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(Key); return !string.IsNullOrEmpty(k?.GetValue(ValueName) as string); }
            catch { return false; }
        }
    }

    public static bool Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true) ?? Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
            else k.DeleteValue(ValueName, false);
            return true;
        }
        catch { return false; }
    }
}

/// <summary>Once-a-day copies of the data file into a folder the user picks (for example a OneDrive or Dropbox folder).</summary>
public static class BackupService
{
    public static DateTime? LastBackupUtc { get; private set; }
    public static readonly int[] KeepChoices = { 7, 14, 30, 60 };

    public static string FileNameFor(DateTime local) => $"tempo-backup-{local:yyyy-MM-dd}.json";

    /// <summary>Keeps the newest <paramref name="keep"/> files named tempo-backup-*.json; never touches anything else in the folder.</summary>
    public static int Prune(string folder, int keep)
    {
        int removed = 0;
        var files = new DirectoryInfo(folder).GetFiles("tempo-backup-*.json").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        foreach (var f in files.Skip(Math.Max(1, keep)))
        {
            try { f.Delete(); removed++; } catch { }
        }
        return removed;
    }

    /// <summary>Writes today's backup. Returns the file path, or null if backups are off or it failed.</summary>
    public static string? RunNow()
    {
        var s = State.Data.Settings;
        if (string.IsNullOrWhiteSpace(s.BackupFolder)) return null;
        try
        {
            Directory.CreateDirectory(s.BackupFolder);
            State.Save();
            var dest = Path.Combine(s.BackupFolder, FileNameFor(DateTime.Now));
            File.Copy(State.DataPath, dest, true);
            Prune(s.BackupFolder, s.BackupKeep);
            LastBackupUtc = DateTime.UtcNow;
            return dest;
        }
        catch { return null; }
    }

    /// <summary>Backs up if there is no copy from today yet.</summary>
    public static void RunIfDue()
    {
        var s = State.Data.Settings;
        if (string.IsNullOrWhiteSpace(s.BackupFolder)) return;
        try { if (File.Exists(Path.Combine(s.BackupFolder, FileNameFor(DateTime.Now)))) return; } catch { return; }
        RunNow();
    }
}

/// <summary>The "tracking reminders" rules, kept free of UI so they can be tested.</summary>
public static class Reminders
{
    /// <summary>
    /// Nudge when nothing has been tracked for a while but the person is clearly at the computer.
    /// notTrackingSince: when we first saw "active but not tracking"; lastReminder: when we last nudged.
    /// </summary>
    public static bool ShouldRemind(bool running, TimeSpan idle, DateTime? notTrackingSinceUtc, DateTime? lastReminderUtc, int everyMinutes, DateTime nowUtc)
    {
        if (everyMinutes <= 0 || running || idle > TimeSpan.FromMinutes(2) || notTrackingSinceUtc == null) return false;
        var span = TimeSpan.FromMinutes(everyMinutes);
        if (nowUtc - notTrackingSinceUtc.Value < span) return false;
        return lastReminderUtc == null || nowUtc - lastReminderUtc.Value >= span;
    }

    /// <summary>At the chosen time of day, warn once if a timer is still going.</summary>
    public static bool ShouldWarnEndOfDay(bool hasTimerRunning, string endTime, DateTime nowLocal, DateTime? lastWarnedDate)
    {
        if (!hasTimerRunning || string.IsNullOrWhiteSpace(endTime)) return false;
        if (!TimeSpan.TryParse(endTime.Trim(), out var t)) return false;
        if (lastWarnedDate?.Date == nowLocal.Date) return false;
        return nowLocal.TimeOfDay >= t;
    }
}
