using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Tempo;

/// <summary>App-wide workspace, safe persistence, timer logic and timezone-aware date maths.</summary>
public static class State
{
    public static readonly string Dir =
        Environment.GetEnvironmentVariable("TEMPO_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TempoStudio");
    public static string DataPath => Path.Combine(Dir, "tempo-data.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static Workspace Data { get; set; } = new();
    public static string? LoadNotice;
    public static event Action? Changed;
    public static event Action<string, bool>? ToastRequested;

    public static event Action<string, string, Action>? ToastActionRequested;

    public static void Toast(string message, bool error = false) => ToastRequested?.Invoke(Loc.T(message), error);

    /// <summary>Toast with an Undo button that brings the deleted item back.</summary>
    public static void ToastUndo(string message, TrashItem item) =>
        ToastActionRequested?.Invoke(Loc.T(message), Loc.T("Undo"), () => { if (TrashBin.Restore(item)) Toast("Restored"); });

    public static Client? ClientOf(Project? p) => p == null ? null : Data.Clients.FirstOrDefault(c => c.Id == p.ClientId);
    public static string ClientName(Project? p) => ClientOf(p)?.Name ?? p?.Client ?? "";

    // ---------- persistence ----------
    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        if (File.Exists(DataPath))
        {
            try
            {
                Data = Read(DataPath);
                var v1 = Path.Combine(Dir, "tempo-data.v1-backup.json");   // one-time safety copy of the original app's file
                if (!File.Exists(v1)) File.Copy(DataPath, v1);
            }
            catch
            {
                var bad = Path.Combine(Dir, $"tempo-data.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try { File.Copy(DataPath, bad, true); } catch { }
                var bak = DataPath + ".bak";
                try { Data = Read(bak); LoadNotice = "Your data file was damaged, so Tempo restored the last good backup."; }
                catch { Data = new Workspace(); LoadNotice = "Your data file was damaged. A copy was kept next to it as tempo-data.corrupt-*.json."; }
            }
        }
        Normalize();
    }

    static Workspace Read(string path) =>
        JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("empty");

    public static void Normalize()
    {
        Data.Projects ??= new(); Data.Sessions ??= new(); Data.Invoices ??= new();
        Data.Settings ??= new(); Data.Billing ??= new(); Data.Timer ??= new();
        Data.Clients ??= new(); Data.Expenses ??= new(); Data.Trash ??= new(); Data.Recurring ??= new();
        foreach (var s in Data.Sessions) s.Tags ??= new();
        if (string.IsNullOrWhiteSpace(Data.Settings.TimeZoneId)) Data.Settings.TimeZoneId = TimeZoneInfo.Local.Id;

        // Older data keeps the client as text on each project; turn those into real client records (merged by name).
        foreach (var p in Data.Projects)
        {
            if (!string.IsNullOrEmpty(p.ClientId) && Data.Clients.Any(c => c.Id == p.ClientId)) continue;
            var name = (p.Client ?? "").Trim();
            if (name.Length == 0) continue;
            var c = Data.Clients.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null) { c = new Client { Name = name, Email = p.ClientEmail ?? "", Address = p.ClientAddress ?? "" }; Data.Clients.Add(c); }
            else
            {
                if (c.Email.Length == 0) c.Email = p.ClientEmail ?? "";
                if (c.Address.Length == 0) c.Address = p.ClientAddress ?? "";
            }
            p.ClientId = c.Id;
        }
        if (string.IsNullOrWhiteSpace(Data.InvoiceNumber) || Data.Invoices.Any(i => i.Number == Data.InvoiceNumber))
            Data.InvoiceNumber = NextInvoiceNumber();
    }

    public static string NextInvoiceNumber()
    {
        var n = Math.Max(1, Data.InvoiceCounter);
        string Make(int i) => $"INV-{DateTime.Now.Year}-{i:000}";
        while (Data.Invoices.Any(i => i.Number == Make(n))) n++;
        return Make(n);
    }

    /// <summary>Atomic write: temp file, then replace, keeping the previous version as .bak.</summary>
    public static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = DataPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            if (File.Exists(DataPath)) File.Replace(tmp, DataPath, DataPath + ".bak");
            else File.Move(tmp, DataPath);
            return true;
        }
        catch (Exception ex)
        {
            Toast(Loc.T("Tempo could not save your data: ") + ex.Message, true);
            return false;
        }
    }

    public static void Notify() { Save(); Changed?.Invoke(); }

    // ---------- lookups ----------
    public static Project? Proj(string? id) => Data.Projects.FirstOrDefault(p => p.Id == id);
    public static decimal Earned(Session s) => Math.Round(s.DurationMilliseconds / 3600000m * s.HourlyRate, 2);
    /// <summary>What a session is worth on an invoice: zero when it is marked non-billable.</summary>
    public static decimal Billed(Session s) => s.Billable ? Earned(s) : 0m;
    public static IEnumerable<Project> ActiveProjects => Data.Projects.Where(p => !p.Archived);

    // ---------- timezone ----------
    static TimeZoneInfo? _tz;
    public static TimeZoneInfo Tz
    {
        get
        {
            var id = Data.Settings.TimeZoneId;
            if (_tz == null || _tz.Id != id)
            {
                try { _tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { _tz = TimeZoneInfo.Local; }
            }
            return _tz;
        }
    }
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);
    public static DateTime ToUtc(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, Tz);
    }
    public static DateTime NowLocal => ToLocal(DateTime.UtcNow);
    public static DateTime TodayLocal => NowLocal.Date;
    public static DateTime WeekStart(DateTime localDate)
    {
        int first = Data.Settings.WeekStart switch { "Sunday" => 0, "Saturday" => 6, _ => 1 };
        return localDate.Date.AddDays(-(((int)localDate.DayOfWeek - first + 7) % 7));
    }
    public static string OffsetText(DateTime utc)
    {
        var o = Tz.GetUtcOffset(utc);
        return (o < TimeSpan.Zero ? "-" : "+") + o.ToString(@"hh\:mm");
    }

    // ---------- timer ----------
    public static bool Running => Data.Timer.RunSinceUtc != null;
    public static bool HasTimer => Running || Data.Timer.AccumulatedMilliseconds > 0;
    public static long ElapsedMs
    {
        get
        {
            var t = Data.Timer;
            var ms = t.AccumulatedMilliseconds;
            if (t.RunSinceUtc is DateTime r) ms += (long)(DateTime.UtcNow - r).TotalMilliseconds;
            return Math.Max(0, ms);
        }
    }

    public static void StartTimer(string projectId, string task)
    {
        var t = Data.Timer;
        if (!HasTimer) t.StartedAtUtc = DateTime.UtcNow;
        t.ProjectId = projectId;
        t.Task = task;
        t.RunSinceUtc = DateTime.UtcNow;
        Notify();
    }

    public static void PauseTimer()
    {
        var t = Data.Timer;
        if (t.RunSinceUtc is DateTime r)
        {
            t.AccumulatedMilliseconds += (long)(DateTime.UtcNow - r).TotalMilliseconds;
            t.RunSinceUtc = null;
        }
        Notify();
    }

    /// <summary>Drop time the user was away. Optionally stop and save the timer at the moment they left.</summary>
    public static void RemoveIdle(DateTime idleSinceUtc, bool stop)
    {
        var t = Data.Timer;
        if (t.RunSinceUtc is DateTime r)
        {
            if (idleSinceUtc > r) t.AccumulatedMilliseconds += (long)(idleSinceUtc - r).TotalMilliseconds;
            t.RunSinceUtc = stop ? null : DateTime.UtcNow;
        }
        if (stop) StopTimer(); else Notify();
    }

    public static void DiscardTimer()
    {
        Data.Timer = new TimerSnapshot { ProjectId = Data.Timer.ProjectId };
        Notify();
    }

    public static Session? StopTimer()
    {
        var t = Data.Timer;
        var ms = ElapsedMs;
        var project = Proj(t.ProjectId);
        Session? saved = null;
        if (project != null && ms >= 1000)
        {
            var end = DateTime.UtcNow;
            saved = new Session
            {
                ProjectId = project.Id,
                Task = t.Task.Trim(),
                StartedAtUtc = t.StartedAtUtc ?? end.AddMilliseconds(-ms),
                EndedAtUtc = end,
                DurationMilliseconds = ms,
                HourlyRate = project.HourlyRate,
                Billable = project.Billable,
                Source = "timer"
            };
            Data.Sessions.Add(saved);
        }
        Data.Timer = new TimerSnapshot { ProjectId = t.ProjectId };
        Notify();
        if (saved != null) Budget.Check(project);
        return saved;
    }

    // ---------- aggregates ----------
    public static long DayMs(DateTime localDay)
    {
        long ms = Data.Sessions.Where(s => ToLocal(s.StartedAtUtc).Date == localDay.Date).Sum(s => s.DurationMilliseconds);
        if (HasTimer && Data.Timer.StartedAtUtc is DateTime st && ToLocal(st).Date == localDay.Date) ms += ElapsedMs;
        return ms;
    }
    public static long WeekMs(DateTime localDay)
    {
        var ws = WeekStart(localDay);
        return Enumerable.Range(0, 7).Sum(i => DayMs(ws.AddDays(i)));
    }
    public static decimal WeekEarned(DateTime localDay)
    {
        var ws = WeekStart(localDay); var we = ws.AddDays(7);
        var sum = Data.Sessions.Where(s => { var d = ToLocal(s.StartedAtUtc).Date; return d >= ws && d < we; }).Sum(Billed);
        if (HasTimer && Data.Timer.StartedAtUtc is DateTime st && ToLocal(st).Date >= ws && ToLocal(st).Date < we && Proj(Data.Timer.ProjectId) is Project { Billable: true } p)
            sum += Math.Round(ElapsedMs / 3600000m * p.HourlyRate, 2);
        return sum;
    }
    public static decimal Unbilled => Data.Sessions.Where(s => s.InvoiceId == null && s.Billable).Sum(Earned);
    public static decimal UnbilledFor(string projectId) => Data.Sessions.Where(s => s.InvoiceId == null && s.Billable && s.ProjectId == projectId).Sum(Earned);
}

public static class Fmt
{
    static readonly Dictionary<string, (string Sym, int Dec)> Cur = new()
    {
        ["USD"] = ("$", 2), ["EUR"] = ("€", 2), ["GBP"] = ("£", 2), ["CAD"] = ("CA$", 2), ["AUD"] = ("A$", 2),
        ["NZD"] = ("NZ$", 2), ["JPY"] = ("¥", 0), ["INR"] = ("₹", 2), ["CHF"] = ("CHF ", 2), ["SEK"] = ("kr ", 2),
        ["MXN"] = ("MX$", 2), ["BRL"] = ("R$", 2), ["SGD"] = ("S$", 2), ["ZAR"] = ("R ", 2),
    };
    public static IEnumerable<string> Currencies => Cur.Keys;
    public static bool UsLetter(string code) => code is "USD" or "CAD" or "MXN";

    public static string Money(decimal v, string? code = null)
    {
        code ??= State.Data.Settings.CurrencyCode;
        var (sym, dec) = Cur.TryGetValue(code, out var c) ? c : (code + " ", 2);
        return (v < 0 ? "-" : "") + sym + Math.Abs(v).ToString("N" + dec, CultureInfo.InvariantCulture);
    }
    public static string Hm(long ms)
    {
        var m = (long)Math.Round(ms / 60000.0);
        return $"{m / 60}h {m % 60:00}m";
    }
    public static string Clock(long ms) { var s = ms / 1000; return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}"; }
    public static string Time(DateTime local) =>
        local.ToString(State.Data.Settings.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
    public static string Date(DateTime d, string? lang = null) => d.ToString(Loc.DatePattern(lang), Loc.Culture(lang));
    public static string DayHeading(DateTime d)
    {
        var t = State.TodayLocal;
        if (d.Date == t) return Loc.T("Today");
        if (d.Date == t.AddDays(-1)) return Loc.T("Yesterday");
        return d.ToString(d.Year == t.Year ? "dddd, MMM d" : "dddd, MMM d, yyyy", CultureInfo.CurrentCulture);
    }
    public static string Dec(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
