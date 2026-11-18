using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tempo;

/// <summary>Week grid: projects down the side, days across. Type hours into a cell to log time without a timer.</summary>
public static class TimesheetView
{
    static DateTime? weekStart;

    static string Hours(long ms) => ms <= 0 ? "" : (ms / 3600000.0).ToString("0.##", CultureInfo.CurrentCulture);

    public static StackPanel Build(Func<UIElement> tabs, Action rebuild)
    {
        var d = State.Data;
        var ws = weekStart ?? State.WeekStart(State.TodayLocal);
        var days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).ToList();

        var root = new StackPanel();
        root.Children.Add(Ui.Header("Time entries", "Type hours into any cell. Tempo keeps them as a timesheet entry for that day.",
            Ui.IconBtn(Icons.ChevronLeft, "Previous week", () => { weekStart = ws.AddDays(-7); rebuild(); }),
            Ui.Btn("This week", () => { weekStart = null; rebuild(); }, "GlassButton"),
            Ui.IconBtn(Icons.ChevronRight, "Next week", () => { weekStart = ws.AddDays(7); rebuild(); })));
        root.Children.Add(tabs());

        var rowsProjects = d.Projects
            .Where(p => !p.Archived || d.Sessions.Any(s => s.ProjectId == p.Id && State.ToLocal(s.StartedAtUtc).Date >= ws && State.ToLocal(s.StartedAtUtc).Date <= ws.AddDays(6)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        if (rowsProjects.Count == 0)
        {
            root.Children.Add(Ui.Card(Ui.Empty(Icons.Grid, "No projects yet", "Connect your work to a person or brand, set an hourly rate, and you're ready to track.")));
            return root;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        for (int i = 0; i < 8; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

        int r = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = Ui.Text(Loc.F("Week of {0}", Fmt.Date(ws)), 13, "MutedBrush", FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(title);
        for (int i = 0; i < 7; i++)
        {
            bool today = days[i] == State.TodayLocal;
            var h = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            h.Children.Add(Ui.Text(days[i].ToString("ddd"), 12, today ? "TextBrush" : "MutedBrush", today ? FontWeights.Bold : FontWeights.SemiBold).Center());
            h.Children.Add(Ui.Text(days[i].ToString("d"), 11, "FaintBrush").Center());
            Grid.SetColumn(h, i + 1); grid.Children.Add(h);
        }
        var th = Ui.Text(Loc.T("Total"), 12, "MutedBrush", FontWeights.SemiBold).Center(); th.VerticalAlignment = VerticalAlignment.Bottom; th.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetColumn(th, 8); grid.Children.Add(th);

        var dayTotals = new long[7];
        foreach (var p in rowsProjects)
        {
            r++;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
            var dot = Ui.Dot(p.Color, 10); dot.Margin = new Thickness(0, 0, 10, 0);
            name.Children.Add(dot);
            var nm = new StackPanel();
            nm.Children.Add(Ui.Text(p.Name, 13.5, "TextBrush", FontWeights.SemiBold));
            nm.Children.Add(Ui.Text(State.ClientName(p), 11.5, "MutedBrush"));
            name.Children.Add(nm);
            Grid.SetRow(name, r); grid.Children.Add(name);

            long rowTotal = 0;
            for (int i = 0; i < 7; i++)
            {
                var day = days[i];
                var ms = d.Sessions.Where(s => s.ProjectId == p.Id && State.ToLocal(s.StartedAtUtc).Date == day).Sum(s => s.DurationMilliseconds);
                rowTotal += ms; dayTotals[i] += ms;
                var box = new TextBox { Text = Hours(ms), Padding = new Thickness(2, 7, 2, 7), TextAlignment = TextAlignment.Center, Margin = new Thickness(3, 3, 3, 3) };
                box.ToolTip = Loc.F("Week of {0}", Fmt.Date(ws));
                System.Windows.Automation.AutomationProperties.SetName(box, $"{p.Name} {day:ddd d}");
                var proj = p; var dd = day; long before = ms;
                void Commit()
                {
                    if (box.Text.Trim() == Hours(before)) return;
                    if (!CommitCell(proj, dd, box.Text)) box.Text = Hours(before);
                }
                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); } };
                Grid.SetRow(box, r); Grid.SetColumn(box, i + 1); grid.Children.Add(box);
            }
            var tt = Ui.Text(Hours(rowTotal) is { Length: > 0 } hs ? hs : "–", 13.5, "TextBrush", FontWeights.SemiBold).Center(); tt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(tt, r); Grid.SetColumn(tt, 8); grid.Children.Add(tt);
        }

        r++;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lbl = Ui.Text("Total", 13, "MutedBrush", FontWeights.SemiBold); lbl.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(lbl, r); grid.Children.Add(lbl);
        for (int i = 0; i < 7; i++)
        {
            var t = Ui.Text(Hours(dayTotals[i]) is { Length: > 0 } s1 ? s1 : "–", 13.5, "TextBrush", FontWeights.SemiBold).Center(); t.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(t, r); Grid.SetColumn(t, i + 1); grid.Children.Add(t);
        }
        var gt = Ui.Text(Hours(dayTotals.Sum()) is { Length: > 0 } s2 ? s2 : "–", 14, "AccentBrush", FontWeights.Bold).Center(); gt.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(gt, r); Grid.SetColumn(gt, 8); grid.Children.Add(gt);

        root.Children.Add(Ui.Card(grid, 18));
        return root;
    }

    /// <summary>Sets a project's total for one day. Other entries stay as they are; a "timesheet entry" absorbs the difference.</summary>
    public static bool CommitCell(Project p, DateTime day, string text)
    {
        text = text.Trim();
        long want = 0;
        if (text.Length > 0 && (!EntriesPage.TryDuration(text, out want) || want < 0))
        { State.Toast("Duration should look like 1:30 (h:mm) or 1.5 (hours).", true); return false; }

        var d = State.Data;
        var daySessions = d.Sessions.Where(s => s.ProjectId == p.Id && State.ToLocal(s.StartedAtUtc).Date == day.Date).ToList();
        var grid = daySessions.FirstOrDefault(s => s.Source == "grid");
        var other = daySessions.Where(s => s != grid).Sum(s => s.DurationMilliseconds);
        var gridMs = want - other;
        if (gridMs < 0) { State.Toast(Loc.F("That day already has {0} h tracked on other entries.", Hours(other)), true); return false; }
        if (grid?.InvoiceId != null) { State.Toast("That timesheet entry is already on an invoice.", true); return false; }

        if (gridMs == 0) { if (grid != null) d.Sessions.Remove(grid); }
        else
        {
            if (grid == null)
            {
                grid = new Session { ProjectId = p.Id, Source = "grid", Member = State.Data.Settings.MemberName, Task = "Timesheet entry", HourlyRate = p.HourlyRate, Billable = p.Billable };
                d.Sessions.Add(grid);
            }
            var start = State.ToUtc(day.Date.AddHours(9));
            grid.StartedAtUtc = start; grid.DurationMilliseconds = gridMs; grid.EndedAtUtc = start.AddMilliseconds(gridMs);
        }
        State.Notify();
        Budget.Check(p);
        return true;
    }
}
