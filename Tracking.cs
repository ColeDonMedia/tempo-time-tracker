namespace Tempo;

public sealed record Gap(DateTime StartUtc, DateTime EndUtc);

/// <summary>A day's sessions on a timeline: overlaps (double-counted time) and gaps (untracked time).</summary>
public static class Timeline
{
    /// <summary>Pairs of entries that share time. Entries that only touch (one ends as the next starts) are fine.</summary>
    public static List<(Session A, Session B)> Overlaps(IEnumerable<Session> sessions)
    {
        var list = sessions.OrderBy(s => s.StartedAtUtc).ThenBy(s => s.EndedAtUtc).ToList();
        var result = new List<(Session, Session)>();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count && list[j].StartedAtUtc < list[i].EndedAtUtc; j++)
                result.Add((list[i], list[j]));
        return result;
    }

    public static HashSet<string> OverlappingIds(IEnumerable<Session> sessions) =>
        Overlaps(sessions).SelectMany(p => new[] { p.A.Id, p.B.Id }).ToHashSet();

    /// <summary>Untracked stretches of at least <paramref name="minMinutes"/> inside [from, to).</summary>
    public static List<Gap> Gaps(IEnumerable<Session> sessions, DateTime fromUtc, DateTime toUtc, int minMinutes)
    {
        var gaps = new List<Gap>();
        var cursor = fromUtc;
        foreach (var s in sessions.Where(s => s.EndedAtUtc > fromUtc && s.StartedAtUtc < toUtc).OrderBy(s => s.StartedAtUtc))
        {
            if (s.StartedAtUtc > cursor) Add(cursor, s.StartedAtUtc);
            if (s.EndedAtUtc > cursor) cursor = s.EndedAtUtc;
        }
        if (cursor < toUtc) Add(cursor, toUtc);
        return gaps;

        void Add(DateTime a, DateTime b) { if ((b - a).TotalMinutes >= minMinutes) gaps.Add(new Gap(a, b)); }
    }

    public static Session? FillGap(Gap g, Project p, string task)
    {
        var ms = (long)(g.EndUtc - g.StartUtc).TotalMilliseconds;
        if (ms <= 0) return null;
        var s = new Session
        {
            ProjectId = p.Id, Task = task.Trim(), StartedAtUtc = g.StartUtc, EndedAtUtc = g.EndUtc, DurationMilliseconds = ms,
            HourlyRate = p.HourlyRate, Billable = p.Billable, Source = "manual", Member = State.Data.Settings.MemberName,
        };
        State.Data.Sessions.Add(s);
        State.Notify();
        Budget.Check(p);
        return s;
    }
}

public enum FocusPhase { Focus, Break }

/// <summary>Pomodoro-style focus rounds: work for N minutes, rest for M, repeat.</summary>
public static class Focus
{
    public static (FocusPhase Phase, TimeSpan Left, int Round) At(DateTime startUtc, DateTime nowUtc, int focusMin, int breakMin)
    {
        var f = TimeSpan.FromMinutes(Math.Max(1, focusMin)); var b = TimeSpan.FromMinutes(Math.Max(1, breakMin));
        var elapsed = nowUtc - startUtc; if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var cycle = f + b;
        var round = (int)(elapsed.Ticks / cycle.Ticks) + 1;
        var pos = TimeSpan.FromTicks(elapsed.Ticks % cycle.Ticks);
        return pos < f ? (FocusPhase.Focus, f - pos, round) : (FocusPhase.Break, cycle - pos, round);
    }
}

public sealed record ActivitySample(DateTime Utc, string App, string Title);

/// <summary>Activity samples on disk: one JSON-lines file per day in the data folder, deleted after a few days.</summary>
public static class ActivityStore
{
    static string FileFor(string dir, DateTime utc) => System.IO.Path.Combine(dir, $"activity-{utc:yyyy-MM-dd}.jsonl");

    public static void Append(string dir, ActivitySample s)
    {
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.AppendAllText(FileFor(dir, s.Utc), System.Text.Json.JsonSerializer.Serialize(s) + "\n");
    }

    public static List<ActivitySample> Load(string dir, DateTime fromUtc, DateTime toUtc)
    {
        var list = new List<ActivitySample>();
        if (!System.IO.Directory.Exists(dir)) return list;
        for (var day = fromUtc.Date; day <= toUtc.Date; day = day.AddDays(1))
        {
            var f = FileFor(dir, day);
            if (!System.IO.File.Exists(f)) continue;
            foreach (var line in System.IO.File.ReadLines(f))
            {
                try { if (System.Text.Json.JsonSerializer.Deserialize<ActivitySample>(line) is { } s && s.Utc >= fromUtc && s.Utc < toUtc) list.Add(s); }
                catch { /* a half-written line after a crash is skipped */ }
            }
        }
        return list;
    }

    /// <summary>Deletes activity files older than <paramref name="keepDays"/> full days. Never touches other files.</summary>
    public static int Prune(string dir, int keepDays, DateTime nowUtc)
    {
        if (!System.IO.Directory.Exists(dir)) return 0;
        int n = 0;
        var cutoff = nowUtc.Date.AddDays(-keepDays);
        foreach (var f in System.IO.Directory.GetFiles(dir, "activity-*.jsonl"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(f)["activity-".Length..];
            if (DateTime.TryParseExact(name, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) && d < cutoff)
                try { System.IO.File.Delete(f); n++; } catch { }
        }
        return n;
    }
}
public sealed record Suggestion(DateTime StartUtc, DateTime EndUtc, string App, string Title);

/// <summary>Opt-in activity tracking: turns "which app was in front" samples into suggested time entries.</summary>
public static class Activity
{
    /// <summary>
    /// Runs of the same app (samples no more than two intervals apart) that no session covers and that last at least
    /// <paramref name="minMinutes"/>. The title shown is the one seen most often in that run.
    /// </summary>
    public static List<Suggestion> Suggest(IReadOnlyList<ActivitySample> samples, IEnumerable<Session> sessions, TimeSpan every, int minMinutes)
    {
        var covered = sessions.Select(s => (s.StartedAtUtc, s.EndedAtUtc)).ToList();
        var free = samples.Where(x => !covered.Any(c => x.Utc >= c.StartedAtUtc && x.Utc < c.EndedAtUtc)).OrderBy(x => x.Utc).ToList();
        var result = new List<Suggestion>();
        int i = 0;
        while (i < free.Count)
        {
            int j = i;
            while (j + 1 < free.Count && free[j + 1].App == free[i].App && free[j + 1].Utc - free[j].Utc <= every + every) j++;
            var start = free[i].Utc; var end = free[j].Utc + every;
            if ((end - start).TotalMinutes >= minMinutes)
            {
                var title = free.Skip(i).Take(j - i + 1).GroupBy(x => x.Title).OrderByDescending(g => g.Count()).First().Key;
                result.Add(new Suggestion(start, end, free[i].App, title));
            }
            i = j + 1;
        }
        return result;
    }
}
