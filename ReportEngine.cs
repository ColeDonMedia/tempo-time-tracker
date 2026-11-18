using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace Tempo;

public sealed class ReportFilter
{
    public DateTime? From { get; set; }              // local dates, inclusive
    public DateTime? To { get; set; }
    public string ProjectId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Billable { get; set; } = "All";    // All | Billable | Non-billable
    public string GroupBy { get; set; } = "Project"; // Project | Client | Task | Tag | Day | Week | Month
}

public sealed class ReportRow
{
    public string Key = "";
    public string Label = "";
    public decimal Hours;
    public decimal BillableHours;
    public decimal Earned;
    public int Entries;
}

public sealed class ReportResult
{
    public List<ReportRow> Rows = new();
    public decimal TotalHours, BillableHours, Earned, Expenses;
    public int Entries;
}

/// <summary>Turns the time log into totals by project, client, task, tag or period.</summary>
public static class ReportEngine
{
    public static (DateTime? From, DateTime? To) Preset(string preset)
    {
        var today = State.TodayLocal;
        switch (preset)
        {
            case "This week": { var f = State.WeekStart(today); return (f, f.AddDays(6)); }
            case "Last week": { var f = State.WeekStart(today).AddDays(-7); return (f, f.AddDays(6)); }
            case "This month": { var f = new DateTime(today.Year, today.Month, 1); return (f, f.AddMonths(1).AddDays(-1)); }
            case "Last month": { var f = new DateTime(today.Year, today.Month, 1).AddMonths(-1); return (f, f.AddMonths(1).AddDays(-1)); }
            case "Last 30 days": return (today.AddDays(-29), today);
            case "This year": return (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31));
            default: return (null, null);   // All time / Custom
        }
    }

    public static ReportResult Compute(ReportFilter f)
    {
        var d = State.Data;
        var clientProjects = string.IsNullOrEmpty(f.ClientId) ? null : d.Projects.Where(p => p.ClientId == f.ClientId).Select(p => p.Id).ToHashSet();

        bool InRange(DateTime local) => (f.From == null || local.Date >= f.From.Value.Date) && (f.To == null || local.Date <= f.To.Value.Date);
        bool ProjectOk(string pid) => (string.IsNullOrEmpty(f.ProjectId) || pid == f.ProjectId) && (clientProjects == null || clientProjects.Contains(pid));

        var sessions = d.Sessions.Where(s => ProjectOk(s.ProjectId) && InRange(State.ToLocal(s.StartedAtUtc))
                                             && (f.Billable == "All" || (f.Billable == "Billable") == s.Billable)).ToList();
        var res = new ReportResult { Entries = sessions.Count };
        var map = new Dictionary<string, ReportRow>();

        void Add(string key, string label, Session s)
        {
            if (!map.TryGetValue(key, out var row)) map[key] = row = new ReportRow { Key = key, Label = label };
            var h = s.DurationMilliseconds / 3600000m;
            row.Hours += h; row.Entries++;
            if (s.Billable) { row.BillableHours += h; row.Earned += State.Earned(s); }
        }

        foreach (var s in sessions)
        {
            var local = State.ToLocal(s.StartedAtUtc);
            var p = State.Proj(s.ProjectId);
            switch (f.GroupBy)
            {
                case "Client": { var n = State.ClientName(p); Add(n.ToLowerInvariant(), n.Length == 0 ? Loc.T("No client set") : n, s); break; }
                case "Task": { var t = string.IsNullOrWhiteSpace(s.Task) ? Loc.T("Work session") : s.Task.Trim(); Add(t.ToLowerInvariant(), t, s); break; }
                case "Tag":
                    if (s.Tags.Count == 0) Add("~", Loc.T("(no tag)"), s);
                    else foreach (var tag in s.Tags) Add(tag.ToLowerInvariant(), tag, s);
                    break;
                case "Day": Add(local.ToString("yyyy-MM-dd"), Fmt.Date(local), s); break;
                case "Week": { var w = State.WeekStart(local); Add(w.ToString("yyyy-MM-dd"), Loc.F("Week of {0}", Fmt.Date(w)), s); break; }
                case "Month": Add(local.ToString("yyyy-MM"), local.ToString("MMMM yyyy", CultureInfo.CurrentCulture), s); break;
                default: { var n = p?.Name ?? Loc.T("Deleted project"); Add(s.ProjectId, n, s); break; }
            }
        }

        res.TotalHours = sessions.Sum(s => s.DurationMilliseconds) / 3600000m;
        res.BillableHours = sessions.Where(s => s.Billable).Sum(s => s.DurationMilliseconds) / 3600000m;
        res.Earned = sessions.Where(s => s.Billable).Sum(State.Earned);
        res.Expenses = d.Expenses.Where(e => ProjectOk(e.ProjectId) && InRange(e.Date)).Sum(e => e.Amount);

        res.Rows = f.GroupBy is "Day" or "Week" or "Month"
            ? map.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToList()
            : map.Values.OrderByDescending(r => r.Hours).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        return res;
    }

    public static string Csv(ReportResult r, ReportFilter f)
    {
        var inv = CultureInfo.InvariantCulture;
        string Q(string v) { if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v; return "\"" + v.Replace("\"", "\"\"") + "\""; }
        var sb = new StringBuilder();
        sb.AppendLine($"Group,Hours,Billable hours,Earned,Entries,Currency");
        foreach (var row in r.Rows)
            sb.AppendLine(string.Join(",", Q(row.Label), row.Hours.ToString("0.00", inv), row.BillableHours.ToString("0.00", inv), row.Earned.ToString("0.00", inv), row.Entries, State.Data.Settings.CurrencyCode));
        sb.AppendLine(string.Join(",", Q("TOTAL"), r.TotalHours.ToString("0.00", inv), r.BillableHours.ToString("0.00", inv), r.Earned.ToString("0.00", inv), r.Entries, State.Data.Settings.CurrencyCode));
        return sb.ToString();
    }

    // ---------------- PDF ----------------
    public static void WritePdf(ReportResult r, ReportFilter f, string rangeText, string filterText, string path)
    {
        Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
        Color ink = C("#1E2B34"), muted = C("#5E6A74"), faint = C("#8A949C"), rule = C("#DDE2E0"), blue = C("#0068F0");
        string Money(decimal v) => Fmt.Money(v);
        string Hrs(decimal v) => v.ToString("0.00", CultureInfo.CurrentCulture);

        var w = new PdfWriter();
        bool letter = Fmt.UsLetter(State.Data.Settings.CurrencyCode);
        double PW = letter ? 612 : 595.28, PH = letter ? 792 : 841.89, M = 44, cw = PW - 2 * M, limit = PH - M - 24;
        var page = w.AddPage(PW, PH);
        double y = M;

        var studio = State.Data.Billing.Name;
        page.Text(M, y, Loc.T("Time report"), 24, true, blue);
        if (!string.IsNullOrWhiteSpace(studio)) page.TextRight(M + cw, y + 6, studio, 12, true, ink);
        y += 34;
        page.Text(M, y, rangeText, 11, false, muted); y += 16;
        if (filterText.Length > 0) { page.Text(M, y, filterText, 10, false, faint); y += 15; }
        y += 6; page.Line(M, y, M + cw, y, 0.8, rule); y += 16;

        // KPI strip
        double kx = M, kw = cw / 4;
        void Kpi(string label, string value)
        {
            page.Text(kx, y, label.ToUpperInvariant(), 8, true, faint);
            page.Text(kx, y + 12, value, 15, true, ink);
            kx += kw;
        }
        Kpi(Loc.T("Tracked"), Hrs(r.TotalHours) + " h");
        Kpi(Loc.T("Billable"), Hrs(r.BillableHours) + " h");
        Kpi(Loc.T("Earned"), Money(r.Earned));
        Kpi(Loc.T("Expenses"), Money(r.Expenses));
        y += 46;

        double c0 = cw * 0.46, c1 = cw * 0.16, c2 = cw * 0.18, c3 = cw * 0.20;
        void Header()
        {
            page.Text(M, y, Loc.T("GROUP"), 8, true, faint);
            page.TextRight(M + c0 + c1, y, Loc.T("HOURS"), 8, true, faint);
            page.TextRight(M + c0 + c1 + c2, y, Loc.T("BILLABLE HOURS"), 8, true, faint);
            page.TextRight(M + cw, y, Loc.T("EARNED"), 8, true, faint);
            y += 16; page.Line(M, y, M + cw, y, 1, C("#BFC7C4"));
        }
        Header();
        foreach (var row in r.Rows)
        {
            var lines = w.Wrap(row.Label, c0 - 10, 10, false);
            double rowH = lines.Count * 13.5 + 12;
            if (y + rowH > limit) { page = w.AddPage(PW, PH); y = M; Header(); }
            double ty = y + 6;
            foreach (var ln in lines) { page.Text(M, ty, ln, 10, false, ink); ty += 13.5; }
            page.TextRight(M + c0 + c1, y + 6, Hrs(row.Hours), 10, false, ink);
            page.TextRight(M + c0 + c1 + c2, y + 6, Hrs(row.BillableHours), 10, false, muted);
            page.TextRight(M + cw, y + 6, Money(row.Earned), 10, true, ink);
            y += rowH; page.Line(M, y, M + cw, y, 0.6, C("#E6EAE8"));
        }
        if (y + 30 > limit) { page = w.AddPage(PW, PH); y = M; }
        y += 8;
        page.Text(M, y, Loc.T("Total"), 11, true, ink);
        page.TextRight(M + c0 + c1, y, Hrs(r.TotalHours), 11, true, ink);
        page.TextRight(M + c0 + c1 + c2, y, Hrs(r.BillableHours), 11, true, muted);
        page.TextRight(M + cw, y, Money(r.Earned), 11, true, blue);

        for (int i = 0; i < w.PageCount; i++)
            w.PageAt(i).TextRight(M + cw, PH - M + 6, Loc.F("Page {0} of {1}", i + 1, w.PageCount), 8, false, faint);
        w.Save(path, Loc.T("Time report"));
    }
}
