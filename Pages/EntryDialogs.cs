using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Tempo;

/// <summary>Import (calendar / other trackers), split and bulk-edit dialogs for time entries.</summary>
public static class EntryDialogs
{
    const long MaxImportBytes = 20 * 1024 * 1024;   // a 20 MB export is years of entries; anything bigger is not a real export

    static string? ReadFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = Loc.T(title), Filter = filter };
        if (dlg.ShowDialog() != true) return null;
        try
        {
            if (new FileInfo(dlg.FileName).Length > MaxImportBytes) { State.Toast("That file is too large to import.", true); return null; }
            return File.ReadAllText(dlg.FileName);
        }
        catch { State.Toast("That file couldn't be read.", true); return null; }
    }

    public static void Import()
    {
        var opts = new[] { "Calendar events (.ics)", "Toggl, Harvest or Clockify export (.csv)" };
        var pick = Ui.Combo(opts, opts[0]);
        var body = new StackPanel();
        body.Children.Add(Ui.Text("Bring in meetings from Outlook, Google or Apple Calendar, or move your history over from another time tracker.", 13.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("WHAT ARE YOU IMPORTING?", pick, 4));
        if (!Dialogs.Show("Import time", body, "Choose file…")) return;
        if (pick.SelectedIndex == 0) ImportCalendar(); else ImportCsv();
    }

    // ---------- other trackers ----------
    public static void ImportCsv()
    {
        var text = ReadFile("Import a CSV export", "CSV files (*.csv)|*.csv");
        if (text == null) return;
        var table = CsvImport.ReadCsv(text);
        var format = table.Count > 0 ? CsvImport.Detect(table[0]) : null;
        if (format == null) { State.Toast("Tempo couldn't recognise that CSV. Use a detailed export from Toggl, Harvest or Clockify.", true); return; }
        var (rows, skipped) = CsvImport.Map(table, format);
        if (rows.Count == 0) { State.Toast("No time entries were found in that file.", true); return; }

        var rate = Ui.Input(Fmt.Dec(State.ActiveProjects.Select(p => p.HourlyRate).FirstOrDefault()), "0");
        var body = new StackPanel();
        var from = rows.Min(r => r.StartLocal); var to = rows.Max(r => r.StartLocal);
        body.Children.Add(Ui.Text(Loc.F("Found {0} entries from {1} ({2} to {3}), {4} hours in total.", rows.Count, format, Fmt.Date(from), Fmt.Date(to), Fmt.Dec(Math.Round(rows.Sum(r => r.DurationMs) / 3_600_000m, 2))), 14, "TextBrush", wrap: true).WithMargin(0, 0, 0, 8));
        var projects = rows.Select(r => r.Project).Where(p => p.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        body.Children.Add(Ui.Text(Loc.F("Projects: {0}", string.Join(", ", projects.Take(8)) + (projects.Count > 8 ? " …" : "")), 12.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 8));
        if (skipped > 0) body.Children.Add(Ui.Text(Loc.F("{0} rows couldn't be read and will be skipped.", skipped), 12.5, "WarnBrush", wrap: true).WithMargin(0, 0, 0, 8));
        body.Children.Add(Ui.Text("Missing clients and projects are created. Entries you already imported are skipped, so it's safe to import the same file again.", 12, "FaintBrush", wrap: true).WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("RATE FOR NEW PROJECTS (WHEN THE FILE HAS NONE)", rate, 4));
        var err = Dialogs.ErrorLabel(); body.Children.Add(err);
        Dialogs.Show("Import time", body, "Import", () =>
        {
            if (!Ui.TryDec(rate.Text, out var r) || r < 0) { Dialogs.SetError(err, "Enter an hourly rate like 85 or 37.50."); return false; }
            var res = CsvImport.Apply(rows, r);
            State.Toast(Loc.F("Imported {0} entries ({1} already there, {2} new projects).", res.Added, res.Duplicates, res.NewProjects));
            return true;
        }, 520);
    }

    // ---------- calendar ----------
    public static async void ImportCalendar()
    {
        var projects = State.ActiveProjects.ToList();
        if (projects.Count == 0) { State.Toast("Create a project first", true); return; }
        var text = ReadFile("Import a calendar", "Calendar files (*.ics)|*.ics");
        if (text == null) return;
        var toUtc = DateTime.UtcNow;
        List<CalEvent> all;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            // parsed off the UI thread so even a huge calendar never freezes the window
            var tz = State.Tz;
            all = await Task.Run(() => IcsImport.Parse(text, tz, toUtc.AddDays(-90), toUtc));
        }
        catch { State.Toast("That file couldn't be read.", true); return; }
        finally { Mouse.OverrideCursor = null; }
        all = all.Where(e => !e.AllDay && e.EndUtc <= toUtc).OrderByDescending(e => e.StartUtc).ToList();
        if (all.Count == 0) { State.Toast("No timed events from the last 90 days were found in that calendar.", true); return; }

        var pc = Ui.Combo(projects, projects[0], "Label");
        var search = Ui.Input("", "Filter events, e.g. Orbit");
        var list = new StackPanel();
        var ticked = new HashSet<CalEvent>();   // survives filtering, so narrowing the list never loses ticks
        var shown = new List<CalEvent>();
        var count = Ui.Text("", 12.5, "MutedBrush");
        void Count() => count.Text = Loc.F("{0} ticked", ticked.Count);
        void Fill()
        {
            list.Children.Clear(); shown.Clear();
            var q = search.Text.Trim();
            foreach (var e in all.Where(e => q.Length == 0 || e.Summary.Contains(q, StringComparison.CurrentCultureIgnoreCase)).Take(200))
            {
                var ls = State.ToLocal(e.StartUtc);
                var cb = Ui.Check($"{e.Summary}  ·  {ls.ToString(Loc.ShortMonthDay(), CultureInfo.CurrentCulture)}, {Fmt.Time(ls)}  ·  {Fmt.Hm((long)(e.EndUtc - e.StartUtc).TotalMilliseconds)}", ticked.Contains(e));
                cb.Margin = new Thickness(0, 3, 0, 3);
                var ev = e;
                cb.Click += (_, _) => { if (cb.IsChecked == true) ticked.Add(ev); else ticked.Remove(ev); Count(); };
                shown.Add(e); list.Children.Add(cb);
            }
            if (shown.Count == 0) list.Children.Add(Ui.Text("No matches", 13, "FaintBrush"));
        }
        search.TextChanged += (_, _) => Fill();
        Fill(); Count();
        var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var selAll = Ui.Btn("Tick all shown", () => { foreach (var e in shown) ticked.Add(e); Fill(); Count(); }, "GhostButton");
        var selNone = Ui.Btn("Untick all", () => { ticked.Clear(); Fill(); Count(); }, "GhostButton");
        count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(count, Dock.Right);
        tools.Children.Add(count); tools.Children.Add(selAll); tools.Children.Add(selNone);
        selAll.HorizontalAlignment = HorizontalAlignment.Left;
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.F("{0} events from the last 90 days. Tick the ones you worked on.", all.Count), 13.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 12));
        body.Children.Add(Ui.Field("ADD THEM TO PROJECT", pc));
        body.Children.Add(Ui.Field("FILTER", search, 8));
        body.Children.Add(tools);
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) });
        var err = Dialogs.ErrorLabel(); body.Children.Add(err);
        Dialogs.Show("Import calendar events", body, "Import", () =>
        {
            if (pc.SelectedItem is not Project p) { Dialogs.SetError(err, "Choose a project."); return false; }
            if (ticked.Count == 0) { Dialogs.SetError(err, "Tick at least one event."); return false; }
            var (added, dup) = IcsImport.Apply(ticked.OrderBy(e => e.StartUtc), p);
            State.Toast(Loc.F("Imported {0} entries ({1} already there).", added, dup));
            return true;
        }, 600);
    }

    // ---------- split ----------
    public static void Split(Session s)
    {
        var ls = State.ToLocal(s.StartedAtUtc); var le = State.ToLocal(s.EndedAtUtc);
        var mid = ls.AddTicks((le - ls).Ticks / 2);
        mid = new DateTime(mid.Year, mid.Month, mid.Day, mid.Hour, mid.Minute / 5 * 5, 0);
        if (mid <= ls) mid = ls.AddMinutes(1);
        var fmt = State.Data.Settings.Use24Hour ? "HH:mm" : "h:mm tt";
        var at = Ui.Input(mid.ToString(fmt, CultureInfo.CurrentCulture), "9:30 AM");
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.F("Runs from {0} to {1}. Pick where the second entry should start.", Fmt.Time(ls), Fmt.Time(le)), 13.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("SPLIT AT", at, 4));
        // live preview of the two parts
        var preview = Ui.Text("", 12.5, "MutedBrush").WithMargin(0, 2, 0, 6);
        void Preview()
        {
            if ((DateTime.TryParse(at.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var pt) || DateTime.TryParse(at.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out pt)))
            {
                var l = ls.Date + pt.TimeOfDay; if (l < ls) l = l.AddDays(1);
                if (l > ls && l < le) { var ratio = s.DurationMilliseconds / (le - ls).TotalMilliseconds; var a = (long)((l - ls).TotalMilliseconds * ratio); preview.Text = $"{Fmt.Hm(a)}  +  {Fmt.Hm(s.DurationMilliseconds - a)}"; return; }
            }
            preview.Text = "";
        }
        at.TextChanged += (_, _) => Preview(); Preview();
        body.Children.Add(preview);
        var err = Dialogs.ErrorLabel(); body.Children.Add(err);
        Dialogs.Show("Split entry", body, "Split", () =>
        {
            if (!DateTime.TryParse(at.Text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var t) && !DateTime.TryParse(at.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
            { Dialogs.SetError(err, "Start time should look like 9:30 AM or 14:05."); return false; }
            var local = ls.Date + t.TimeOfDay;
            if (local < ls) local = local.AddDays(1);   // entries that cross midnight
            if (EntryTools.Split(s, State.ToUtc(local)) == null) { Dialogs.SetError(err, Loc.F("Pick a time between {0} and {1}.", Fmt.Time(ls), Fmt.Time(le))); return false; }
            State.Toast("Entry split in two");
            return true;
        }, 440);
    }

    // ---------- bulk ----------
    /// <summary>True when the user confirmed and entries moved.</summary>
    public static bool BulkProject(List<Session> items)
    {
        var projects = State.ActiveProjects.ToList();
        if (projects.Count == 0) return false;
        var pc = Ui.Combo(projects, projects[0], "Label");
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.F("Moves {0} entries. Each one takes the new project's hourly rate. Invoiced entries are left alone.", items.Count), 13.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("PROJECT", pc, 4));
        return Dialogs.Show("Change project", body, "Move entries", () =>
        {
            if (pc.SelectedItem is Project p) State.Toast(Loc.F("{0} entries updated", EntryTools.BulkEdit(items, p.Id, null, null)));
            return true;
        }, 460);
    }

    public static void BulkTag(List<Session> items)
    {
        var tag = Ui.Input("", "design, meeting");
        var body = new StackPanel();
        body.Children.Add(Ui.Field("TAG", tag, 4));
        var err = Dialogs.ErrorLabel(); body.Children.Add(err);
        Dialogs.Show("Add tag", body, "Add tag", () =>
        {
            var t = tag.Text.Trim().TrimEnd(',');
            if (t.Length == 0 || t.Contains(',')) { Dialogs.SetError(err, "Type one tag."); return false; }
            State.Toast(Loc.F("{0} entries updated", EntryTools.BulkEdit(items, null, null, t)));
            return true;
        }, 420);
    }
}
