using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Tempo;

public class EntriesPage : PageBase
{
    string? projectFilter;       // null = all
    string status = "All";       // All | Unbilled | Invoiced
    string search = "";
    int limit = 120;

    public override void Build()
    {
        var d = State.Data;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Time entries", "Completed sessions, with the rate saved when each one ended.",
            Ui.Btn("Add entry", () => EditEntry(null), "GlassButton", Icons.Plus),
            Ui.Btn("Export CSV", ExportCsv, "GlassButton", Icons.Download)));

        // ---- filters ----
        var projects = new List<Project> { new() { Id = "", Name = "All projects" } };
        projects.AddRange(d.Projects);
        var pc = Ui.Combo(projects, projects.FirstOrDefault(p => p.Id == (projectFilter ?? "")), "Name");
        pc.MinWidth = 220;
        var sc = Ui.Combo(new[] { "All", "Unbilled", "Invoiced" }, status); sc.MinWidth = 140;
        var tb = Ui.Input(search, "Search descriptions…"); tb.MinWidth = 240;
        var list = new StackPanel();
        void Refill() { FillList(list); }
        pc.SelectionChanged += (_, _) => { projectFilter = (pc.SelectedItem as Project)?.Id is { Length: > 0 } id ? id : null; limit = 120; Refill(); };
        sc.SelectionChanged += (_, _) => { status = (string)sc.SelectedItem; limit = 120; Refill(); };
        tb.TextChanged += (_, _) => { search = tb.Text; limit = 120; Refill(); };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        pc.Margin = new Thickness(0, 0, 10, 0); sc.Margin = new Thickness(0, 0, 10, 0);
        bar.Children.Add(pc); bar.Children.Add(sc); bar.Children.Add(tb);
        root.Children.Add(bar);

        root.Children.Add(Ui.Card(list, 14));
        Refill();
        Content = root;
    }

    IEnumerable<Session> Filtered()
    {
        var q = State.Data.Sessions.AsEnumerable();
        if (projectFilter != null) q = q.Where(s => s.ProjectId == projectFilter);
        if (status == "Unbilled") q = q.Where(s => s.InvoiceId == null);
        if (status == "Invoiced") q = q.Where(s => s.InvoiceId != null);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(s => (s.Task ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                             (State.Proj(s.ProjectId)?.Name ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                             (State.Proj(s.ProjectId)?.Client ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase));
        return q.OrderByDescending(s => s.StartedAtUtc);
    }

    void FillList(StackPanel list)
    {
        list.Children.Clear();
        var all = Filtered().ToList();
        if (all.Count == 0)
        {
            list.Children.Add(Ui.Empty(Icons.Clock, State.Data.Sessions.Count == 0 ? "No saved sessions yet" : "Nothing matches those filters",
                State.Data.Sessions.Count == 0 ? "Start a timer or add an entry by hand to record your work." : "Try clearing the search or choosing All projects."));
            return;
        }
        var summary = Ui.Text($"{all.Count} entr{(all.Count == 1 ? "y" : "ies")}  ·  {Fmt.Hm(all.Sum(s => s.DurationMilliseconds))}  ·  {Fmt.Money(all.Sum(State.Earned))}", 13, "MutedBrush");
        summary.Margin = new Thickness(10, 4, 0, 6);
        list.Children.Add(summary);

        DateTime? day = null;
        foreach (var s in all.Take(limit))
        {
            var ld = State.ToLocal(s.StartedAtUtc).Date;
            if (day != ld)
            {
                day = ld;
                var total = all.Where(x => State.ToLocal(x.StartedAtUtc).Date == ld).Sum(x => x.DurationMilliseconds);
                var g = new Grid { Margin = new Thickness(10, 14, 10, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Ui.Text(Fmt.DayHeading(ld).ToUpperInvariant(), 11, "FaintBrush", FontWeights.SemiBold));
                var t = Ui.Text(Fmt.Hm(total), 12, "FaintBrush", FontWeights.SemiBold); Grid.SetColumn(t, 1); g.Children.Add(t);
                list.Children.Add(g);
            }
            list.Children.Add(SessionRow(s, true));
        }
        if (all.Count > limit)
            list.Children.Add(Ui.Btn($"Show {Math.Min(120, all.Count - limit)} more", () => { limit += 120; FillList(list); }, "GhostButton").Center().WithMargin(0, 10, 0, 4));
    }

    public static Border SessionRow(Session s, bool actions)
    {
        var p = State.Proj(s.ProjectId);
        var g = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = Ui.Dot(p?.Color ?? "gold", 10); dot.Margin = new Thickness(0, 0, 14, 0);
        g.Children.Add(dot);

        var ls = State.ToLocal(s.StartedAtUtc); var le = State.ToLocal(s.EndedAtUtc);
        var mid = new StackPanel();
        mid.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(s.Task) ? "Work session" : s.Task, 14.5, "TextBrush", FontWeights.SemiBold));
        var sub = $"{(p?.Name ?? "Deleted project")}{(string.IsNullOrWhiteSpace(p?.Client) ? "" : " · " + p!.Client)}  ·  {(actions ? "" : ls.ToString("MMM d", CultureInfo.CurrentCulture) + ", ")}{Fmt.Time(ls)} – {Fmt.Time(le)}";
        mid.Children.Add(Ui.Text(sub, 12.5, "MutedBrush"));
        Grid.SetColumn(mid, 1); g.Children.Add(mid);

        var dur = Ui.Text(Fmt.Hm(s.DurationMilliseconds), 14, "TextBrush", FontWeights.SemiBold); dur.Margin = new Thickness(12, 0, 16, 0); dur.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(dur, 2); g.Children.Add(dur);
        var amt = Ui.Text(Fmt.Money(State.Earned(s)), 14, "MutedBrush"); amt.MinWidth = 78; amt.TextAlignment = TextAlignment.Right; amt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(amt, 3); g.Children.Add(amt);

        if (actions)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
            var inv = s.InvoiceId == null ? null : State.Data.Invoices.FirstOrDefault(i => i.Id == s.InvoiceId);
            if (s.InvoiceId != null) right.Children.Add(Ui.Pill(inv?.Number ?? "Invoiced", "GoodBrush", "GoodSoftBrush"));
            else
            {
                right.Children.Add(Ui.IconBtn(Icons.Edit, "Edit entry", () => EditEntry(s)));
                right.Children.Add(Ui.IconBtn(Icons.Trash, "Delete entry", () =>
                {
                    if (Dialogs.Confirm("Delete entry", "Delete this time entry? This can't be undone.", "Delete", true))
                    { State.Data.Sessions.Remove(s); State.Notify(); State.Toast("Entry deleted"); }
                }, true));
            }
            Grid.SetColumn(right, 4); g.Children.Add(right);
        }

        var b = new Border { Padding = new Thickness(actions ? 10 : 4, 10, actions ? 6 : 4, 10), CornerRadius = new CornerRadius(12), Child = g, Background = System.Windows.Media.Brushes.Transparent };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        b.MouseLeave += (_, _) => b.Background = System.Windows.Media.Brushes.Transparent;
        return b;
    }

    // ---------- add / edit ----------
    public static void EditEntry(Session? editing)
    {
        var d = State.Data;
        var projects = State.ActiveProjects.ToList();
        if (editing != null && State.Proj(editing.ProjectId) is Project cur && !projects.Contains(cur)) projects.Add(cur);
        if (projects.Count == 0) { State.Toast("Create a project first", true); return; }

        var start = editing != null ? State.ToLocal(editing.StartedAtUtc) : State.NowLocal.AddHours(-1);
        var pc = Ui.Combo(projects, editing != null ? State.Proj(editing.ProjectId) : projects[0], "Label");
        var task = Ui.Input(editing?.Task ?? "", "What did you work on?");
        var date = Ui.Input(start.ToString("yyyy-MM-dd"), "yyyy-mm-dd");
        var time = Ui.Input(start.ToString(d.Settings.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture), "9:30 AM");
        var dur = Ui.Input(editing != null ? FormatDuration(editing.DurationMilliseconds) : "1:00", "1:30 or 1.5");
        var rate = Ui.Input(editing != null ? Fmt.Dec(editing.HourlyRate) : Fmt.Dec(projects[0].HourlyRate), "0");
        pc.SelectionChanged += (_, _) => { if (editing == null && pc.SelectedItem is Project p) rate.Text = Fmt.Dec(p.HourlyRate); };
        var err = Dialogs.ErrorLabel();

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var f1 = Ui.Field("DATE", date); var f2 = Ui.Field("START TIME", time); Grid.SetColumn(f2, 2);
        row.Children.Add(f1); row.Children.Add(f2);
        var row2 = new Grid();
        row2.ColumnDefinitions.Add(new ColumnDefinition()); row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); row2.ColumnDefinitions.Add(new ColumnDefinition());
        var f3 = Ui.Field("DURATION", dur); var f4 = Ui.Field("HOURLY RATE", rate); Grid.SetColumn(f4, 2);
        row2.Children.Add(f3); row2.Children.Add(f4);

        var body = new StackPanel();
        body.Children.Add(Ui.Field("PROJECT", pc));
        body.Children.Add(Ui.Field("DESCRIPTION", task));
        body.Children.Add(row); body.Children.Add(row2);
        body.Children.Add(Ui.Text($"Times are in {State.Tz.StandardName} (change in Settings).", 12, "FaintBrush").WithMargin(0, -4, 0, 12));
        body.Children.Add(err);

        Dialogs.Show(editing == null ? "Add time entry" : "Edit time entry", body, editing == null ? "Add entry" : "Save changes", () =>
        {
            if (pc.SelectedItem is not Project p) { Dialogs.SetError(err, "Choose a project."); return false; }
            if (!Ui.TryDate(date.Text, out var dt)) { Dialogs.SetError(err, "Date should look like 2026-11-30."); return false; }
            if (!DateTime.TryParse(time.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var tm) &&
                !DateTime.TryParse(time.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out tm))
            { Dialogs.SetError(err, "Start time should look like 9:30 AM or 14:05."); return false; }
            if (!TryDuration(dur.Text, out var ms) || ms <= 0) { Dialogs.SetError(err, "Duration should look like 1:30 (h:mm) or 1.5 (hours)."); return false; }
            if (!Ui.TryDec(rate.Text, out var r) || r < 0) { Dialogs.SetError(err, "Enter an hourly rate like 85 or 37.50."); return false; }

            var startUtc = State.ToUtc(dt.Date + tm.TimeOfDay);
            var s = editing ?? new Session();
            s.ProjectId = p.Id; s.Task = task.Text.Trim(); s.StartedAtUtc = startUtc;
            s.DurationMilliseconds = ms; s.EndedAtUtc = startUtc.AddMilliseconds(ms); s.HourlyRate = Math.Round(r, 2);
            if (editing == null) d.Sessions.Add(s);
            State.Notify(); State.Toast(editing == null ? "Entry added" : "Entry updated");
            return true;
        }, 500);
    }

    static string FormatDuration(long ms) { var m = (long)Math.Round(ms / 60000.0); return $"{m / 60}:{m % 60:00}"; }

    static bool TryDuration(string s, out long ms)
    {
        ms = 0; s = s.Trim();
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h < 0 || m is < 0 or > 59) return false;
            int sec = 0; if (parts.Length == 3 && !int.TryParse(parts[2], out sec)) return false;
            ms = ((h * 60L + m) * 60 + sec) * 1000; return true;
        }
        if (Ui.TryDec(s, out var hrs) && hrs > 0) { ms = (long)(hrs * 3600000m); return true; }
        return false;
    }

    // ---------- CSV ----------
    void ExportCsv()
    {
        var rows = Filtered().OrderBy(s => s.StartedAtUtc).ToList();
        if (rows.Count == 0) { State.Toast("There are no entries to export yet.", true); return; }
        var dlg = new SaveFileDialog { Title = "Export time entries", Filter = "CSV file (*.csv)|*.csv", DefaultExt = "csv", FileName = $"tempo-time-entries-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return;

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Date,Start time,End time,Time zone,UTC offset,Start (UTC),Project,Client,Description,Duration hours,Hourly rate,Amount,Currency,Invoice");
        foreach (var s in rows)
        {
            var p = State.Proj(s.ProjectId);
            var ls = State.ToLocal(s.StartedAtUtc); var le = State.ToLocal(s.EndedAtUtc);
            var invoice = s.InvoiceId == null ? "" : State.Data.Invoices.FirstOrDefault(i => i.Id == s.InvoiceId)?.Number ?? "";
            sb.AppendLine(string.Join(",",
                Q(ls.ToString("yyyy-MM-dd", inv)), Q(ls.ToString("HH:mm", inv)), Q(le.ToString("HH:mm", inv)),
                Q(State.Tz.Id), Q(State.OffsetText(s.StartedAtUtc)), Q(s.StartedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", inv)),
                Q(p?.Name ?? "Deleted project"), Q(p?.Client ?? ""), Q(s.Task ?? ""),
                (s.DurationMilliseconds / 3600000m).ToString("0.0000", inv), s.HourlyRate.ToString("0.00", inv),
                State.Earned(s).ToString("0.00", inv), State.Data.Settings.CurrencyCode, Q(invoice)));
        }
        try
        {
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            State.Toast($"Exported {rows.Count} entries");
        }
        catch (Exception ex) { State.Toast("Export failed: " + ex.Message, true); }
    }

    /// <summary>Quote a cell and neutralise spreadsheet formulas (=, +, -, @) so imported text can't run as code.</summary>
    static string Q(string v)
    {
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}
