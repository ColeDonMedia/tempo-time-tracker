using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Tempo;

public class OverviewPage : PageBase
{
    TextBlock clock = null!, stateLbl = null!;
    Path ring = null!;
    Border? goalFill;
    TextBlock? goalText;

    public override void Build()
    {
        var d = State.Data;
        var now = State.NowLocal;
        var root = new StackPanel();

        string greet = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        root.Children.Add(Ui.Header(greet, $"{now:dddd, MMMM d}  ·  {State.Tz.StandardName}",
            Ui.Btn("New project", () => ProjectsPage.EditProject(null), "GlassButton", Icons.Plus)));

        // ---- metrics ----
        var week = State.WeekMs(now);
        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 18) };
        void Add(Border b, bool last = false) { b.Margin = new Thickness(0, 0, last ? 0 : 14, 0); metrics.Children.Add(b); }
        Add(Ui.Metric("Tracked today", Fmt.Hm(State.DayMs(now)), State.Running ? "timer running now" : "so far today"));
        Add(Ui.Metric("This week", Fmt.Hm(week), Loc.F("{0} billable", Fmt.Money(State.WeekEarned(now))), "GoodBrush"));
        Add(Ui.Metric("Unbilled", Fmt.Money(State.Unbilled), "not yet invoiced", "WarnBrush"));
        Add(Ui.Metric("Active projects", State.ActiveProjects.Count().ToString(), "clients & brands"), true);
        root.Children.Add(metrics);

        // ---- timer + week ----
        var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var timer = BuildTimerCard(); timer.Margin = new Thickness(0, 0, 9, 0);
        var wk = BuildWeekCard(now); wk.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(wk, 1);
        row.Children.Add(timer); row.Children.Add(wk);
        root.Children.Add(row);

        // ---- recent ----
        var recent = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ht = new StackPanel();
        ht.Children.Add(Ui.Text("Recent sessions", 18, "TextBrush", FontWeights.SemiBold));
        ht.Children.Add(Ui.Text("Your latest bits of focused work.", 13, "MutedBrush").WithMargin(0, 2, 0, 0));
        head.Children.Add(ht);
        var all = Ui.Btn("View all", () => MainWindow.Instance.Navigate("Entries"), "GhostButton");
        Grid.SetColumn(all, 1); head.Children.Add(all);
        recent.Children.Add(head);
        recent.Children.Add(new Border { Height = 12 });

        var latest = d.Sessions.OrderByDescending(s => s.StartedAtUtc).Take(6).ToList();
        if (latest.Count == 0)
            recent.Children.Add(Ui.Empty(Icons.Clock, "No sessions yet", "Pick a project above and start the timer. Everything you track shows up here."));
        foreach (var s in latest) recent.Children.Add(EntriesPage.SessionRow(s, false));
        root.Children.Add(Ui.Card(recent));

        Content = root;
        Tick();
    }

    Border BuildTimerCard()
    {
        var d = State.Data;
        var projects = State.ActiveProjects.ToList();
        var locked = State.Proj(d.Timer.ProjectId);
        if (locked != null && !projects.Contains(locked)) projects.Add(locked);

        var sp = new StackPanel();
        stateLbl = Ui.Text("READY WHEN YOU ARE", 11, "MutedBrush", FontWeights.SemiBold);
        sp.Children.Add(stateLbl);

        // progress ring + clock
        ring = new Path { StrokeThickness = 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        ring.Stroke = new LinearGradientBrush(Color.FromRgb(0x00, 0x68, 0xF0), Color.FromRgb(0x00, 0xF1, 0xA0), new Point(20, 150), new Point(150, 20)) { MappingMode = BrushMappingMode.Absolute };
        var track = new Ellipse { Width = 168, Height = 168, StrokeThickness = 9 };
        track.SetResourceReference(Shape.StrokeProperty, "FieldBorderBrush");
        clock = new TextBlock { FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "00:00:00" };
        clock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        System.Windows.Documents.Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        var dial = new Grid { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 18) };
        var inner = new Canvas { Width = 168, Height = 168, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(track); inner.Children.Add(ring);
        dial.Children.Add(inner); dial.Children.Add(clock);
        sp.Children.Add(dial);

        var combo = Ui.Combo(projects, locked ?? projects.FirstOrDefault(), "Label");
        combo.IsEnabled = !State.HasTimer;
        if (projects.Count == 0) combo.IsEnabled = false;
        var task = Ui.Input(d.Timer.Task, "What are you working on?");
        task.LostFocus += (_, _) => { d.Timer.Task = task.Text.Trim(); State.Save(); };

        if (projects.Count == 0)
        {
            sp.Children.Add(Ui.Text("Create a project first so Tempo knows which rate to use.", 13, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 12));
            sp.Children.Add(Ui.Btn("Create a project", () => ProjectsPage.EditProject(null), "PrimaryButton", Icons.Plus));
            return Ui.Card(sp);
        }

        sp.Children.Add(combo);
        sp.Children.Add(task.WithMargin(0, 10, 0, 14));

        var btns = new Grid();
        btns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var main = State.Running
            ? Ui.Btn("Pause", () => { d.Timer.Task = task.Text.Trim(); State.PauseTimer(); }, "GlassButton", Icons.Pause)
            : Ui.Btn(State.HasTimer ? "Resume" : "Start timer", () =>
            {
                if (combo.SelectedItem is not Project p) { State.Toast("Choose a project first", true); return; }
                State.StartTimer(p.Id, task.Text.Trim());
            }, "PrimaryButton", Icons.Play);
        main.Padding = new Thickness(16, 12, 16, 12);
        btns.Children.Add(main);
        if (State.HasTimer)
        {
            var stop = Ui.Btn("Stop & save", () =>
            {
                d.Timer.Task = task.Text.Trim();
                var ms = State.ElapsedMs;
                var p = State.Proj(d.Timer.ProjectId);
                var s = State.StopTimer();
                State.Toast(s != null ? Loc.F("Saved {0} to {1}", Fmt.Hm(ms), p?.Name) : "Timer cleared (under 1 second)");
            }, "GlassButton", Icons.Stop);
            stop.Padding = new Thickness(16, 12, 16, 12); stop.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(stop, 1); btns.Children.Add(stop);
        }
        sp.Children.Add(btns);
        if (!State.HasTimer)
        {
            var earlier = Ui.Btn("Forgot to start? I began earlier…", () =>
            {
                if (combo.SelectedItem is not Project p) { State.Toast("Choose a project first", true); return; }
                var mins = new[] { 5, 10, 15, 20, 30, 45, 60, 90, 120 };
                var labels = mins.Select(m => Loc.F("{0} minutes ago", m)).ToList();
                var pick = Ui.Combo(labels, labels[3]);
                var body = new StackPanel();
                body.Children.Add(Ui.Text(Loc.F("Starts the timer on {0} with that time already counted.", p.Name), 13.5, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14));
                body.Children.Add(Ui.Field("I STARTED", pick, 4));
                Dialogs.Show("Start the timer earlier", body, "Start timer", () =>
                {
                    State.StartTimerAgo(p.Id, task.Text.Trim(), mins[Math.Max(0, pick.SelectedIndex)]);
                    return true;
                }, 420);
            }, "GhostButton");
            sp.Children.Add(earlier.Center().WithMargin(0, 8, 0, 0));
        }
        sp.Children.Add(Ui.Text("Ctrl + Space starts or pauses from anywhere in Tempo", 11.5, "FaintBrush").WithMargin(0, 12, 0, 0).Center());
        return Ui.Card(sp);
    }

    Border BuildWeekCard(DateTime now)
    {
        var d = State.Data;
        var ws = State.WeekStart(now);
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("This week", 18, "TextBrush", FontWeights.SemiBold));
        sp.Children.Add(Ui.Text("A steady rhythm, one day at a time.", 13, "MutedBrush").WithMargin(0, 2, 0, 16));

        var days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).ToList();
        var vals = days.Select(x => State.DayMs(x) / 3600000.0).ToList();
        var max = Math.Max(1.0, vals.Max());
        var cols = new Grid { Height = 190 };
        for (int i = 0; i < 7; i++)
        {
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new Grid { Margin = new Thickness(5, 0, 5, 0) };
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var barArea = new Grid();
            var h = Math.Max(vals[i] > 0 ? 6 : 3, vals[i] / max * 130);
            var bar = new Border { Height = h, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Bottom };
            bool today = days[i] == now.Date;
            bar.SetResourceReference(Border.BackgroundProperty, today ? "AccentGradientVBrush" : vals[i] > 0 ? "FaintBrush" : "FieldBorderBrush");
            if (!today && vals[i] > 0) bar.Opacity = 0.6;
            var lbl = Ui.Text(vals[i] > 0 ? $"{vals[i]:0.#}h" : "", 11.5, "MutedBrush").Center();
            lbl.VerticalAlignment = VerticalAlignment.Top;
            lbl.Margin = new Thickness(0, 0, 0, 0);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            stack.Children.Add(lbl.WithMargin(0, 0, 0, 4));
            stack.Children.Add(bar);
            barArea.Children.Add(stack);
            col.Children.Add(barArea);
            var dayName = Ui.Text(days[i].ToString("ddd"), 12, today ? "TextBrush" : "MutedBrush", today ? FontWeights.SemiBold : FontWeights.Normal).Center();
            dayName.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(dayName, 1); col.Children.Add(dayName);
            Grid.SetColumn(col, i);
            cols.Children.Add(col);
        }
        sp.Children.Add(cols);

        // goal bar
        var goal = Math.Max(1, d.Settings.WeeklyGoalHours);
        var hrs = vals.Sum();
        var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 18, 0, 6) };
        track.SetResourceReference(Border.BackgroundProperty, "FieldBorderBrush");
        goalFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
        goalFill.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        track.Child = goalFill;
        track.SizeChanged += (_, e) => goalFill.Width = Math.Min(1, hrs / goal) * e.NewSize.Width;
        sp.Children.Add(track);
        goalText = Ui.Text(Loc.F("{0}h of {1}h weekly goal", hrs.ToString("0.#"), goal.ToString("0.#")), 12.5, "MutedBrush");
        sp.Children.Add(goalText);
        return Ui.Card(sp);
    }

    public override void Tick()
    {
        if (clock == null) return;
        var ms = State.ElapsedMs;
        clock.Text = Fmt.Clock(ms);
        stateLbl.Text = Loc.T(State.Running ? "TIMER RUNNING" : State.HasTimer ? "TIMER PAUSED" : "READY WHEN YOU ARE");
        UpdateRing(State.HasTimer ? (ms % 60000) / 60000.0 : 0);
    }

    void UpdateRing(double p)
    {
        const double r = 80, c = 84;
        if (p <= 0.001) { ring.Data = Geometry.Empty; return; }
        p = Math.Min(p, 0.9995);
        var a = p * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true));
        ring.Data = new PathGeometry(new[] { fig });
    }
}

