using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Tempo;

/// <summary>Focus rounds layered on the normal timer. Lives only while Tempo is open.</summary>
public static class FocusMode
{
    public static DateTime? StartUtc { get; private set; }
    static (FocusPhase Phase, int Round)? last;
    public static bool On => StartUtc != null;

    public static void Toggle()
    {
        StartUtc = On ? null : DateTime.UtcNow;
        last = null;
        State.Notify();
    }

    public static (FocusPhase Phase, TimeSpan Left, int Round)? Now
    {
        get
        {
            if (StartUtc is not DateTime s) return null;
            var st = State.Data.Settings;
            return Focus.At(s, DateTime.UtcNow, st.FocusMinutes, st.BreakMinutes);
        }
    }

    /// <summary>Called every tick. Returns a message when a focus round or a break just ended.</summary>
    public static string? Tick()
    {
        if (On && !State.HasTimer) { StartUtc = null; last = null; }   // stopping the timer ends the focus rounds
        if (Now is not { } n) return null;
        var cur = (n.Phase, n.Round);
        var prev = last; last = cur;
        if (prev == null || prev == cur) return null;
        return n.Phase == FocusPhase.Break
            ? Loc.F("Focus round {0} done. Take a {1}-minute break.", prev.Value.Round, State.Data.Settings.BreakMinutes)
            : Loc.T("Break's over. Back to focus.");
    }
}

/// <summary>
/// Opt-in (Settings → Activity): every 30 seconds, notes which app is in front, and optionally its window title.
/// Samples stay in the data folder and are deleted after a few days. Nothing leaves the PC.
/// </summary>
public static class ActivityRecorder
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);

    public static readonly TimeSpan Every = TimeSpan.FromSeconds(30);
    public static string Dir => Path.Combine(State.Dir, "activity");
    static DateTime lastSample = DateTime.MinValue, lastPrune = DateTime.MinValue;

    public static void Tick()
    {
        var s = State.Data.Settings;
        if (!s.TrackActivity || DateTime.UtcNow - lastSample < Every) return;
        lastSample = DateTime.UtcNow;
        if (Idle.IdleFor > TimeSpan.FromMinutes(2)) return;   // away from the keyboard: nothing to suggest
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId) return;      // Tempo itself
            string app;
            using (var p = Process.GetProcessById((int)pid)) app = p.ProcessName;
            var title = "";
            if (s.ActivityTitles)
            {
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, sb.Capacity);
                title = sb.ToString();
            }
            ActivityStore.Append(Dir, new ActivitySample(DateTime.UtcNow, app, title));
        }
        catch { }
        if (DateTime.UtcNow - lastPrune > TimeSpan.FromHours(6)) { lastPrune = DateTime.UtcNow; ActivityStore.Prune(Dir, Math.Max(1, s.ActivityKeepDays), DateTime.UtcNow); }
    }

    /// <summary>Deletes every stored sample (used when the user turns tracking off).</summary>
    public static void Clear()
    {
        try { if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir, "activity-*.jsonl")) File.Delete(f); } catch { }
    }
}
