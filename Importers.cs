using System.Globalization;
using System.Text;

namespace Tempo;

public sealed record CalEvent(string Uid, string Summary, DateTime StartUtc, DateTime EndUtc, bool AllDay);

/// <summary>Reads calendar files (.ics, as exported by Outlook, Google Calendar and Apple Calendar).</summary>
public static class IcsImport
{
    public const int MaxResults = 20_000, MaxEvents = 50_000;
    const long StepBudget = 2_000_000;   // total repeat-rule steps for a whole file, so no calendar can keep the importer busy for long
    [ThreadStatic] static long steps;

    sealed record Prop(string Name, Dictionary<string, string> Params, string Value);

    sealed class Raw
    {
        public string Uid = "", Summary = "", Status = "";
        public Prop? Start, End, RecurrenceId;
        public string Duration = "", RRule = "";
        public List<Prop> ExDates = new();
    }

    /// <summary>Events that overlap [fromUtc, toUtc). Repeating events (RRULE) are expanded inside that range.</summary>
    public static List<CalEvent> Parse(string text, TimeZoneInfo fallback, DateTime fromUtc, DateTime toUtc)
    {
        steps = StepBudget;
        var events = new List<Raw>();
        Raw? cur = null;
        foreach (var line in Unfold(text))
        {
            var p = ParseLine(line);
            if (p == null) continue;
            if (p.Name == "BEGIN" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) { cur = new Raw(); continue; }
            if (p.Name == "END" && p.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (cur != null) events.Add(cur);
                cur = null;
                if (events.Count >= MaxEvents) break;   // a real calendar never gets near this; a hostile file stops here
                continue;
            }
            if (cur == null) continue;
            switch (p.Name)
            {
                case "UID": cur.Uid = p.Value; break;
                case "SUMMARY": cur.Summary = Unescape(p.Value); break;
                case "STATUS": cur.Status = p.Value.ToUpperInvariant(); break;
                case "DTSTART": cur.Start = p; break;
                case "DTEND": cur.End = p; break;
                case "DURATION": cur.Duration = p.Value; break;
                case "RRULE": cur.RRule = p.Value; break;
                case "EXDATE": cur.ExDates.Add(p); break;
                case "RECURRENCE-ID": cur.RecurrenceId = p; break;
            }
        }

        // instances that were moved or edited individually replace their slot in the series
        var overridden = new HashSet<(string, DateTime)>();
        foreach (var e in events)
            if (e.RecurrenceId != null && Time(e.RecurrenceId, fallback) is { } rid) overridden.Add((e.Uid, rid.Utc));

        var result = new List<CalEvent>();
        foreach (var e in events)
        {
            if (result.Count >= MaxResults) break;
            try
            {
                if (e.Status == "CANCELLED" || e.Start == null) continue;
                if (Time(e.Start, fallback) is not { } start) continue;
                TimeSpan length;
                if (e.End != null && Time(e.End, fallback) is { } end) length = end.Utc - start.Utc;
                else if (Dur(e.Duration) is TimeSpan d) length = d;
                else length = start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
                if (length < TimeSpan.Zero || length > TimeSpan.FromDays(31)) continue;
                var summary = string.IsNullOrWhiteSpace(e.Summary) ? "(no title)" : e.Summary.Trim();

                var starts = e.RRule.Length > 0 && e.RecurrenceId == null ? Expand(start, e.RRule, fromUtc - length, toUtc) : new List<DateTime> { start.Utc };
                var ex = new HashSet<DateTime>();
                foreach (var x in e.ExDates)
                    foreach (var v in x.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(1000))
                        if (Time(x with { Value = v.Trim() }, fallback, start.Zone) is { } xt) ex.Add(xt.Utc);
                foreach (var s in starts)
                {
                    if (ex.Contains(s)) continue;
                    if (e.RecurrenceId == null && e.RRule.Length > 0 && overridden.Contains((e.Uid, s))) continue;
                    var en = s + length;
                    if (s < toUtc && en > fromUtc) result.Add(new CalEvent(e.Uid, summary, s, en, start.AllDay));
                    if (result.Count >= MaxResults) break;
                }
            }
            catch { /* one broken event never stops the rest of the calendar */ }
        }
        return result.OrderBy(r => r.StartUtc).ToList();
    }

    /// <summary>Turns chosen events into time entries on a project. Events already imported (same start and length) are skipped.</summary>
    public static (int Added, int Duplicates) Apply(IEnumerable<CalEvent> events, Project p)
    {
        int added = 0, dup = 0;
        var seen = new HashSet<(DateTime, long)>(State.Data.Sessions.Where(s => s.ProjectId == p.Id).Select(s => (s.StartedAtUtc, s.DurationMilliseconds)));
        foreach (var e in events)
        {
            var ms = (long)(e.EndUtc - e.StartUtc).TotalMilliseconds;
            if (ms <= 0) continue;
            if (!seen.Add((e.StartUtc, ms))) { dup++; continue; }
            State.Data.Sessions.Add(new Session
            {
                ProjectId = p.Id, Task = e.Summary, StartedAtUtc = e.StartUtc, EndedAtUtc = e.EndUtc, DurationMilliseconds = ms,
                HourlyRate = p.HourlyRate, Billable = p.Billable, Source = "calendar", Member = State.Data.Settings.MemberName,
            });
            added++;
        }
        if (added > 0) { State.Notify(); Budget.Check(p); }
        return (added, dup);
    }

    static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t')) { sb.Append(raw, 1, raw.Length - 1); continue; }
            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    static Prop? ParseLine(string line)
    {
        int colon = -1; bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') q = !q;
            else if (line[i] == ':' && !q) { colon = i; break; }
        }
        if (colon <= 0) return null;
        var head = line[..colon].Split(';');
        var ps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in head.Skip(1))
        {
            var eq = h.IndexOf('=');
            if (eq > 0) ps[h[..eq]] = h[(eq + 1)..].Trim('"');
        }
        return new Prop(head[0].ToUpperInvariant(), ps, line[(colon + 1)..]);
    }

    static string Unescape(string v) =>
        v.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    sealed record When(DateTime Utc, DateTime Wall, TimeZoneInfo Zone, bool AllDay, bool IsUtc);

    static When? Time(Prop p, TimeZoneInfo fallback, TimeZoneInfo? inherit = null)
    {
        var v = p.Value.Trim();
        var zone = p.Params.TryGetValue("TZID", out var id) ? FindZone(id) ?? inherit ?? fallback : inherit ?? fallback;
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new When(ToUtc(day, fallback), day, fallback, true, false);
        bool utc = v.EndsWith('Z');
        if (utc) v = v[..^1];
        if (!DateTime.TryParseExact(v, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        if (utc) return new When(DateTime.SpecifyKind(t, DateTimeKind.Utc), t, TimeZoneInfo.Utc, false, true);
        return new When(ToUtc(t, zone), t, zone, false, false);
    }

    static DateTime ToUtc(DateTime wall, TimeZoneInfo z)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (z.IsInvalidTime(wall)) wall = wall.AddHours(1);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(wall, z), DateTimeKind.Utc);
    }

    static TimeZoneInfo? FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        try { if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var w)) return TimeZoneInfo.FindSystemTimeZoneById(w); } catch { }
        return null;
    }

    /// <summary>ISO 8601 durations as used in calendars: P1W, P1D, PT1H30M, -PT15M is rejected.</summary>
    static TimeSpan? Dur(string v)
    {
        v = v.Trim().ToUpperInvariant();
        if (v.Length < 3 || v[0] != 'P' && !v.StartsWith("+P")) return null;
        v = v.TrimStart('+')[1..];
        var total = TimeSpan.Zero; var num = new StringBuilder(); bool any = false;
        foreach (var c in v)
        {
            if (char.IsDigit(c)) { num.Append(c); continue; }
            if (c == 'T') continue;
            if (num.Length == 0) return null;
            if (num.Length > 6) return null;   // nobody books a meeting for a million weeks
            var n = int.Parse(num.ToString(), CultureInfo.InvariantCulture); num.Clear(); any = true;
            total += c switch { 'W' => TimeSpan.FromDays(7 * n), 'D' => TimeSpan.FromDays(n), 'H' => TimeSpan.FromHours(n), 'M' => TimeSpan.FromMinutes(n), 'S' => TimeSpan.FromSeconds(n), _ => TimeSpan.Zero };
        }
        return any ? total : null;
    }

    /// <summary>Start times (UTC) of a repeating event. Supports DAILY / WEEKLY (with BYDAY) / MONTHLY / YEARLY, INTERVAL, COUNT, UNTIL.</summary>
    static List<DateTime> Expand(When start, string rrule, DateTime fromUtc, DateTime toUtc)
    {
        var list = new List<DateTime>();
        try { ExpandInto(list, start, rrule, fromUtc, toUtc); }
        catch (ArgumentOutOfRangeException) { }   // date maths ran off the calendar: keep what we have
        return list;
    }

    static void ExpandInto(List<DateTime> list, When start, string rrule, DateTime fromUtc, DateTime toUtc)
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in rrule.Split(';').Select(x => x.Split('=', 2)).Where(x => x.Length == 2)) r[part[0].Trim()] = part[1].Trim().ToUpperInvariant();   // last one wins
        var freq = r.GetValueOrDefault("FREQ", "");
        int interval = int.TryParse(r.GetValueOrDefault("INTERVAL", "1"), out var iv) ? Math.Clamp(iv, 1, 1000) : 1;
        int count = long.TryParse(r.GetValueOrDefault("COUNT", ""), out var cn) && cn > 0 ? (int)Math.Min(cn, int.MaxValue) : int.MaxValue;
        DateTime until = DateTime.MaxValue;
        if (r.TryGetValue("UNTIL", out var u) && Time(new Prop("UNTIL", new(), u), start.Zone, start.Zone) is { } uw)
            until = uw.AllDay ? (uw.Utc.Year >= 9999 ? DateTime.MaxValue : uw.Utc.AddDays(1).AddTicks(-1)) : uw.Utc;
        var stop = until < toUtc ? until : toUtc;

        // Without COUNT, occurrences before the import window don't matter: jump close to it instead of walking there from DTSTART.
        long skip = 0;
        if (count == int.MaxValue && fromUtc > start.Utc)
        {
            var days = (fromUtc - start.Utc).TotalDays;
            skip = freq switch
            {
                "DAILY" => (long)(days / interval) - 1,
                "WEEKLY" => (long)(days / (7.0 * interval)) - 1,
                "MONTHLY" => (long)(days / (28.0 * interval)) - 1,
                "YEARLY" => (long)(days / (365.0 * interval)) - 1,
                _ => 0,
            };
            skip = Math.Max(0, skip);
        }
        DateTime Utc(DateTime wall) => start.IsUtc ? DateTime.SpecifyKind(wall, DateTimeKind.Utc) : ToUtc(wall, start.Zone);

        int made = 0, guard = 0;
        bool Add(DateTime wall)
        {
            var utc = Utc(wall);
            if (utc > stop || made >= count) return false;
            made++; list.Add(utc); return true;
        }

        if (freq == "WEEKLY")
        {
            var days = r.TryGetValue("BYDAY", out var bd)
                ? bd.Split(',').Select(d => d.Length >= 2 ? d[^2..] : "").Select(DayIndex).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList()
                : new List<int> { ((int)start.Wall.DayOfWeek + 6) % 7 };
            if (days.Count == 0) { list.Add(start.Utc); return; }
            var weekStart = start.Wall.Date.AddDays(-(((int)start.Wall.DayOfWeek + 6) % 7)).AddDays(7.0 * interval * skip);
            while (guard++ < 5000 && steps-- > 0)
            {
                foreach (var di in days)
                {
                    var wall = weekStart.AddDays(di) + start.Wall.TimeOfDay;
                    if (wall < start.Wall) continue;
                    if (!Add(wall)) return;
                }
                weekStart = weekStart.AddDays(7 * interval);
            }
            return;
        }

        for (long k = skip; guard++ < 5000 && steps-- > 0; k++)
        {
            DateTime next = freq switch
            {
                "DAILY" => start.Wall.AddDays((double)k * interval),
                "MONTHLY" => start.Wall.AddMonths((int)Math.Min(k * interval, 120_000)),
                "YEARLY" => start.Wall.AddYears((int)Math.Min(k * interval, 10_000)),
                _ => k == 0 ? start.Wall : DateTime.MaxValue,
            };
            if (next == DateTime.MaxValue) break;
            if (freq is "MONTHLY" or "YEARLY" && next.Day != start.Wall.Day) continue;   // e.g. the 31st in a 30-day month: skipped, as in RFC 5545
            if (!Add(next)) break;
        }
    }

    static int DayIndex(string d) => d switch { "MO" => 0, "TU" => 1, "WE" => 2, "TH" => 3, "FR" => 4, "SA" => 5, "SU" => 6, _ => -1 };
}

public sealed record ImportRow(string Project, string Client, string Task, DateTime StartLocal, long DurationMs, bool Billable, List<string> Tags, decimal? Rate);
public sealed record ImportResult(int Added, int Duplicates, int NewProjects, int NewClients);

/// <summary>Imports time from Toggl Track, Harvest and Clockify CSV exports.</summary>
public static class CsvImport
{
    public static readonly string[] Formats = { "Toggl", "Harvest", "Clockify" };
    const long MaxEntryMs = 24 * 3_600_000L;   // one entry longer than a day is a broken row, not real work

    /// <summary>RFC 4180 CSV: quoted fields, doubled quotes, newlines inside quotes. Also accepts ';' files.</summary>
    public static List<string[]> ReadCsv(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n')[0];
        char sep = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        var rows = new List<string[]>();
        var row = new List<string>(); var f = new StringBuilder();
        bool q = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else q = false; }
                else f.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == sep) { row.Add(f.ToString()); f.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(f.ToString()); f.Clear();
                if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row.ToArray());
                row = new List<string>();
            }
            else f.Append(c);
        }
        if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row.ToArray()); }
        return rows;
    }

    public static string? Detect(string[] header)
    {
        var h = new HashSet<string>(header.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        bool Has(params string[] k) => k.All(h.Contains);
        if (Has("Project", "Start Date", "Start Time") && (h.Contains("Duration (h)") || h.Contains("Duration (decimal)"))) return "Clockify";
        if (Has("Project", "Description", "Start date", "Start time", "Duration")) return "Toggl";
        if (Has("Date", "Project", "Hours", "Notes")) return "Harvest";
        return null;
    }

    public static (List<ImportRow> Rows, int Skipped) Map(List<string[]> table, string format)
    {
        var rows = new List<ImportRow>();
        int skipped = 0;
        if (table.Count == 0) return (rows, 0);
        var head = table[0].Select(x => x.Trim()).ToArray();
        int Col(string name) => Array.FindIndex(head, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        int ColStarts(string prefix) => Array.FindIndex(head, x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        foreach (var r in table.Skip(1))
        {
            string G(int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";
            try
            {
                ImportRow? row = format switch
                {
                    "Toggl" => Toggl(),
                    "Harvest" => Harvest(),
                    "Clockify" => Clockify(),
                    _ => null,
                };
                if (row == null || row.DurationMs <= 0 || row.DurationMs > MaxEntryMs || row.StartLocal.Year is < 1990 or > 2100
                    || row.Rate is < 0 or > 1_000_000 || row.Project.Length == 0 && row.Task.Length == 0) skipped++;
                else rows.Add(row);
            }
            catch { skipped++; }

            ImportRow? Toggl()
            {
                if (ParseDate(G(Col("Start date"))) is not DateTime d || ParseTime(G(Col("Start time"))) is not TimeSpan t) return null;
                var ms = ParseClock(G(Col("Duration")));
                if (ms is null) return null;
                var amount = Dec(G(ColStarts("Amount")));
                decimal? rate = amount is > 0 && ms > 0 ? Math.Round(amount.Value / (ms.Value / 3_600_000m), 2) : null;
                return new ImportRow(G(Col("Project")), G(Col("Client")), Join(G(Col("Task")), G(Col("Description"))), d + t, ms.Value, Yes(G(Col("Billable"))), Tags(G(Col("Tags"))), rate);
            }
            ImportRow? Harvest()
            {
                if (ParseDate(G(Col("Date"))) is not DateTime d || Dec(G(Col("Hours"))) is not decimal hrs) return null;
                var start = ParseTime(G(Col("Started"))) ?? TimeSpan.FromHours(9);
                return new ImportRow(G(Col("Project")), G(Col("Client")), Join(G(Col("Task")), G(Col("Notes"))), d + start, (long)(hrs * 3_600_000m), Yes(G(Col("Billable?"))), new(), Dec(G(ColStarts("Billable Rate"))) is > 0 and var br ? br : null);
            }
            ImportRow? Clockify()
            {
                if (ParseDate(G(Col("Start Date"))) is not DateTime d || ParseTime(G(Col("Start Time"))) is not TimeSpan t) return null;
                var ms = ParseClock(G(Col("Duration (h)"))) ?? (Dec(G(Col("Duration (decimal)"))) is decimal dh ? (long)(dh * 3_600_000m) : null);
                if (ms is null) return null;
                return new ImportRow(G(Col("Project")), G(Col("Client")), Join(G(Col("Task")), G(Col("Description"))), d + t, ms.Value, Yes(G(Col("Billable"))), Tags(G(Col("Tags"))), Dec(G(ColStarts("Billable Rate"))) is > 0 and var br ? br : null);
            }
        }
        return (rows, skipped);
    }

    /// <summary>Adds the rows as time entries. Missing clients and projects are created; rows already imported are skipped.</summary>
    public static ImportResult Apply(List<ImportRow> rows, decimal defaultRate)
    {
        // Everything is built first and only added at the end, so a bad row can never leave a half-finished import behind.
        var d = State.Data;
        var clients = d.Clients.ToList(); var projects = d.Projects.ToList();
        var newClients = new List<Client>(); var newProjects = new List<Project>(); var newSessions = new List<Session>();
        var seen = new HashSet<(string, DateTime, long)>(d.Sessions.Select(s => (s.ProjectId, s.StartedAtUtc, s.DurationMilliseconds)));
        int dup = 0;
        foreach (var r in rows)
        {
            Client? client = null;
            if (r.Client.Length > 0)
            {
                client = clients.FirstOrDefault(c => c.Name.Equals(r.Client, StringComparison.CurrentCultureIgnoreCase));
                if (client == null) { client = new Client { Name = r.Client }; clients.Add(client); newClients.Add(client); }
            }
            var pname = r.Project.Length > 0 ? r.Project : "Imported";
            var proj = projects.FirstOrDefault(p => p.Name.Equals(pname, StringComparison.CurrentCultureIgnoreCase) && (client == null ? p.ClientId == "" : p.ClientId == client.Id));
            if (proj == null)
            {
                proj = new Project { Name = pname, ClientId = client?.Id ?? "", Client = client?.Name ?? "", ClientEmail = client?.Email ?? "", HourlyRate = r.Rate ?? defaultRate, Color = Ui.ProjectColors[projects.Count % Ui.ProjectColors.Length] };
                projects.Add(proj); newProjects.Add(proj);
            }
            var startUtc = State.ToUtc(r.StartLocal);
            if (!seen.Add((proj.Id, startUtc, r.DurationMs))) { dup++; continue; }
            newSessions.Add(new Session
            {
                ProjectId = proj.Id, Task = r.Task, StartedAtUtc = startUtc, EndedAtUtc = startUtc.AddMilliseconds(r.DurationMs), DurationMilliseconds = r.DurationMs,
                HourlyRate = r.Rate ?? proj.HourlyRate, Billable = r.Billable, Tags = r.Tags.ToList(), Source = "import", Member = State.Data.Settings.MemberName,
            });
        }
        d.Clients.AddRange(newClients); d.Projects.AddRange(newProjects); d.Sessions.AddRange(newSessions);
        State.Notify();
        return new ImportResult(newSessions.Count, dup, newProjects.Count, newClients.Count);
    }

    static string Join(string task, string notes) => task.Length == 0 ? notes : notes.Length == 0 ? task : $"{task}: {notes}";
    static bool Yes(string v) => v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    static List<string> Tags(string v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    static decimal? Dec(string v) => decimal.TryParse(v.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null;

    static readonly string[] DateFormats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy/MM/dd", "dd-MM-yyyy" };
    static DateTime? ParseDate(string v) => DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    static readonly string[] TimeFormats = { "HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "hh:mm:ss tt", "h:mm:ss tt", "hh:mm tt", "h:mm tt" };
    static TimeSpan? ParseTime(string v) => DateTime.TryParseExact(v.ToUpperInvariant(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.TimeOfDay : null;

    /// <summary>"01:30:00" or "1:30" (hours can go past 24).</summary>
    static long? ParseClock(string v)
    {
        var p = v.Split(':');
        if (p.Length is < 2 or > 3 || !p.All(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _))) return null;
        long h = long.Parse(p[0]), m = long.Parse(p[1]), s = p.Length == 3 ? long.Parse(p[2]) : 0;
        if (m > 59 || s > 59) return null;
        return ((h * 60 + m) * 60 + s) * 1000;
    }
}

/// <summary>Split, merge, duplicate and bulk-edit time entries. Invoiced entries are never changed.</summary>
public static class EntryTools
{
    public static (Session First, Session Second)? Split(Session s, DateTime atUtc)
    {
        if (s.InvoiceId != null || atUtc <= s.StartedAtUtc || atUtc >= s.EndedAtUtc) return null;
        var wall = (s.EndedAtUtc - s.StartedAtUtc).TotalMilliseconds;
        var firstMs = (long)Math.Round((atUtc - s.StartedAtUtc).TotalMilliseconds * (s.DurationMilliseconds / wall));
        var second = Copy(s);
        second.StartedAtUtc = atUtc; second.EndedAtUtc = s.EndedAtUtc; second.DurationMilliseconds = s.DurationMilliseconds - firstMs;
        s.EndedAtUtc = atUtc; s.DurationMilliseconds = firstMs;
        var list = State.Data.Sessions;
        var i = list.IndexOf(s);
        if (i >= 0) list.Insert(i + 1, second); else list.Add(second);
        State.Notify();
        return (s, second);
    }

    public static Session? Merge(IList<Session> items)
    {
        if (items.Count < 2 || items.Select(x => x.ProjectId).Distinct().Count() != 1 || items.Any(x => x.InvoiceId != null)) return null;
        var sorted = items.OrderBy(x => x.StartedAtUtc).ToList();
        var keep = sorted[0];
        keep.EndedAtUtc = sorted.Max(x => x.EndedAtUtc);
        keep.DurationMilliseconds = sorted.Sum(x => x.DurationMilliseconds);
        keep.Task = string.Join(" · ", sorted.Select(x => x.Task.Trim()).Where(t => t.Length > 0).Distinct());
        keep.Tags = sorted.SelectMany(x => x.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var x in sorted.Skip(1)) State.Data.Sessions.Remove(x);
        State.Notify();
        return keep;
    }

    public static Session Duplicate(Session s, DateTime toLocalDay)
    {
        var c = Copy(s);
        var local = State.ToLocal(s.StartedAtUtc);
        c.StartedAtUtc = State.ToUtc(toLocalDay.Date + local.TimeOfDay);
        c.EndedAtUtc = c.StartedAtUtc + (s.EndedAtUtc - s.StartedAtUtc);
        c.Source = "manual";
        State.Data.Sessions.Add(c);
        State.Notify();
        return c;
    }

    /// <summary>Applies the given changes (null = leave alone) to every entry that isn't invoiced. Returns how many changed.</summary>
    public static int BulkEdit(IEnumerable<Session> items, string? projectId, bool? billable, string? addTag)
    {
        var proj = projectId != null ? State.Proj(projectId) : null;
        var tag = addTag?.Trim() ?? "";
        int n = 0;
        foreach (var s in items.Where(x => x.InvoiceId == null).ToList())
        {
            if (proj != null && s.ProjectId != proj.Id) { s.ProjectId = proj.Id; s.HourlyRate = proj.HourlyRate; }
            if (billable is bool b) s.Billable = b;
            if (tag.Length > 0 && !s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) s.Tags.Add(tag);
            n++;
        }
        if (n > 0) State.Notify();
        return n;
    }

    static Session Copy(Session s) => new()
    {
        ProjectId = s.ProjectId, Task = s.Task, StartedAtUtc = s.StartedAtUtc, EndedAtUtc = s.EndedAtUtc, DurationMilliseconds = s.DurationMilliseconds,
        HourlyRate = s.HourlyRate, Billable = s.Billable, Tags = s.Tags.ToList(), Source = s.Source,
    };
}
