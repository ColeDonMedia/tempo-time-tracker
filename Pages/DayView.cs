using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tempo;

/// <summary>One day on a timeline: what you tracked, where entries overlap, untracked gaps, and (opt-in) what your activity suggests.</summary>
public static class DayView
{
    static DateTime? day;

    public static StackPanel Build(Func<UIElement> tabs, Action rebuild)
    {
        var d = State.Data;
        var date = day ?? State.TodayLocal;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Time entries", "Your day at a glance: spot double-counted time and fill the gaps.",
            Ui.IconBtn(Icons.ChevronLeft, "Previous day", () => { day = date.AddDays(-1); rebuild(); }),
            Ui.Btn("Today", () => { day = null; rebuild(); }, "GlassButton"),
            Ui.IconBtn(Icons.ChevronRight, "Next day", () => { day = date.AddDays(1); rebuild(); })));
        root.Children.Add(tabs());

        var fromUtc = State.ToUtc(date); var toUtc = State.ToUtc(date.AddDays(1));
        var sessions = d.Sessions.Where(s => s.EndedAtUtc > fromUtc && s.StartedAtUtc < toUtc).OrderBy(s => s.StartedAtUtc).ToList();
        var overlaps = Timeline.Overlaps(sessions);
        var overlapIds = overlaps.SelectMany(p => new[] { p.A.Id, p.B.Id }).ToHashSet();

        // ---- timeline strip ----
        var card = new StackPanel();
        card.Children.Add(Ui.Text(Fmt.DayHeading(date), 18, "TextBrush", FontWeights.SemiBold));
        card.Children.Add(Ui.Text(Loc.F("{0} tracked", Fmt.Hm(sessions.Sum(s => s.DurationMilliseconds))), 13, "MutedBrush").WithMargin(0, 2, 0, 14));
        var showFrom = 0; var showTo = 24;
        if (sessions.Count > 0)
        {
            showFrom = Math.Max(0, Math.Min(8, (int)State.ToLocal(sessions.Min(s => s.StartedAtUtc) < fromUtc ? fromUtc : sessions.Min(s => s.StartedAtUtc)).Hour));
            var lastEnd = State.ToLocal(sessions.Max(s => s.EndedAtUtc) > toUtc ? toUtc : sessions.Max(s => s.EndedAtUtc));
            showTo = lastEnd.Date > date ? 24 : Math.Min(24, Math.Max(18, lastEnd.Hour + 1));
        }
        else { showFrom = 8; showTo = 18; }
        card.Children.Add(Strip(date, sessions, overlapIds, showFrom, showTo));
        root.Children.Add(Ui.Card(card).WithMargin(0, 0, 0, 18));

        // ---- overlaps ----
        if (overlaps.Count > 0)
        {
            var ov = new StackPanel();
            ov.Children.Add(Ui.Text(Loc.N(overlaps.Count, "{0} overlap", "{0} overlaps"), 16, "WarnBrush", FontWeights.SemiBold));
            ov.Children.Add(Ui.Text("These entries share time, so it's counted twice. Edit or split one of them.", 13, "MutedBrush", wrap: true).WithMargin(0, 2, 0, 10));
            foreach (var (a, b) in overlaps.Take(10))
            {
                ov.Children.Add(EntriesPage.SessionRow(a, false));
                ov.Children.Add(EntriesPage.SessionRow(b, false).WithMargin(0, 0, 0, 10));
                var acts = new WrapPanel { Margin = new Thickness(4, 0, 0, 12) };
                acts.Children.Add(Ui.Btn("Edit first", () => EntriesPage.EditEntry(a), "GlassButton", Icons.Edit).WithMargin(0, 0, 8, 0));
                acts.Children.Add(Ui.Btn("Edit second", () => EntriesPage.EditEntry(b), "GlassButton", Icons.Edit));
                ov.Children.Add(acts);
            }
            root.Children.Add(Ui.Card(ov).WithMargin(0, 0, 0, 18));
        }

        // ---- gaps ----
        var nowUtc = DateTime.UtcNow;
        var gapFrom = State.ToUtc(date.AddHours(showFrom)); var gapTo = State.ToUtc(date.AddHours(showTo));
        if (gapTo > nowUtc) gapTo = nowUtc;
        var gaps = gapTo > gapFrom ? Timeline.Gaps(sessions, gapFrom, gapTo, 15) : new List<Gap>();
        var gp = new StackPanel();
        gp.Children.Add(Ui.Text("Untracked gaps", 16, "TextBrush", FontWeights.SemiBold));
        gp.Children.Add(Ui.Text(Loc.F("Stretches of 15 minutes or more between {0} and {1} with nothing tracked.", Hour(showFrom), Hour(showTo)), 13, "MutedBrush", wrap: true).WithMargin(0, 2, 0, 10));
        if (gaps.Count == 0) gp.Children.Add(Ui.Text("No gaps. Nice and tidy.", 13, "FaintBrush"));
        foreach (var g in gaps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var add = Ui.Btn("Add entry…", () => FillDialog(g, ""), "GlassButton", Icons.Plus);
            DockPanel.SetDock(add, Dock.Right); row.Children.Add(add);
            var lbl = Ui.Text($"{Fmt.Time(State.ToLocal(g.StartUtc))} – {Fmt.Time(State.ToLocal(g.EndUtc))}  ·  {Fmt.Hm((long)(g.EndUtc - g.StartUtc).TotalMilliseconds)}", 14, "TextBrush");
            lbl.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(lbl);
            gp.Children.Add(row);
        }
        root.Children.Add(Ui.Card(gp).WithMargin(0, 0, 0, 18));

        // ---- activity suggestions ----
        var ac = new StackPanel();
        ac.Children.Add(Ui.Text("Suggested from your activity", 16, "TextBrush", FontWeights.SemiBold));
        if (!d.Settings.TrackActivity)
        {
            ac.Children.Add(Ui.Text("Turn on activity tracking in Settings and Tempo will suggest entries for time you forgot to track, based on the app you were using. It stays on this PC.", 13, "MutedBrush", wrap: true).WithMargin(0, 2, 0, 10));
            ac.Children.Add(Ui.Btn("Open Settings", () => MainWindow.Instance.Navigate("Settings"), "GlassButton").HorizontalLeft());
        }
        else
        {
            var samples = ActivityStore.Load(ActivityRecorder.Dir, fromUtc, toUtc);
            var sug = Activity.Suggest(samples, sessions, ActivityRecorder.Every, 10);
            ac.Children.Add(Ui.Text("Apps you used for 10 minutes or more while no timer was running.", 13, "MutedBrush", wrap: true).WithMargin(0, 2, 0, 10));
            if (sug.Count == 0) ac.Children.Add(Ui.Text("Nothing to suggest for this day.", 13, "FaintBrush"));
            foreach (var s in sug)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var label = s.Title.Length > 0 ? $"{s.App} · {s.Title}" : s.App;
                var add = Ui.Btn("Add entry…", () => FillDialog(new Gap(s.StartUtc, s.EndUtc), s.Title.Length > 0 ? s.Title : s.App), "GlassButton", Icons.Plus);
                DockPanel.SetDock(add, Dock.Right); row.Children.Add(add);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var lt = Ui.Text(label, 14, "TextBrush", FontWeights.SemiBold); lt.TextTrimming = TextTrimming.CharacterEllipsis;
                info.Children.Add(lt);
                info.Children.Add(Ui.Text($"{Fmt.Time(State.ToLocal(s.StartUtc))} – {Fmt.Time(State.ToLocal(s.EndUtc))}  ·  {Fmt.Hm((long)(s.EndUtc - s.StartUtc).TotalMilliseconds)}", 12.5, "MutedBrush"));
                row.Children.Add(info);
                ac.Children.Add(row);
            }
        }
        root.Children.Add(Ui.Card(ac));
        return root;
    }

    static string Hour(int h) => Fmt.Time(DateTime.Today.AddHours(h % 24));

    /// <summary>The coloured bar: hours across, one block per entry, overlaps outlined in amber.</summary>
    static UIElement Strip(DateTime date, List<Session> sessions, HashSet<string> overlapIds, int fromH, int toH)
    {
        var span = Math.Max(1, toH - fromH);
        var host = new Grid { Height = 64 };
        var canvas = new Canvas { Height = 40, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
        var track = new Border { Height = 40, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        track.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        host.Children.Add(track); host.Children.Add(canvas);
        var labels = new Canvas { Height = 18, VerticalAlignment = VerticalAlignment.Bottom };
        host.Children.Add(labels);
        System.Windows.Automation.AutomationProperties.SetName(host, Loc.F("{0} tracked", Fmt.Hm(sessions.Sum(s => s.DurationMilliseconds))));

        void Layout(double w)
        {
            canvas.Children.Clear(); labels.Children.Clear();
            double X(DateTime utc) => Math.Clamp(((State.ToLocal(utc) - date).TotalHours - fromH) / span, 0, 1) * w;
            for (int h = fromH; h <= toH; h += span > 12 ? 2 : 1)
            {
                var t = Ui.Text(Hour(h), 11, "FaintBrush");
                Canvas.SetLeft(t, Math.Min(w - 30, Math.Max(0, (h - fromH) / (double)span * w - 12)));
                labels.Children.Add(t);
            }
            foreach (var s in sessions)
            {
                double x1 = X(s.StartedAtUtc), x2 = X(s.EndedAtUtc);
                if (x2 - x1 < 2) x2 = x1 + 2;
                var p = State.Proj(s.ProjectId);
                var blk = new Border
                {
                    Width = x2 - x1, Height = overlapIds.Contains(s.Id) ? 20 : 40, CornerRadius = new CornerRadius(8), Opacity = 0.9,
                    Background = new SolidColorBrush(Ui.ProjColor(p?.Color ?? "gold")),
                    ToolTip = $"{(string.IsNullOrWhiteSpace(s.Task) ? Loc.T("Work session") : s.Task)} · {p?.Name}\n{Fmt.Time(State.ToLocal(s.StartedAtUtc))} – {Fmt.Time(State.ToLocal(s.EndedAtUtc))}",
                };
                if (overlapIds.Contains(s.Id))
                {
                    // overlapping entries share the row: draw them half-height, stacked, with an amber edge
                    blk.BorderThickness = new Thickness(2); blk.SetResourceReference(Border.BorderBrushProperty, "WarnBrush");
                    Canvas.SetTop(blk, sessions.IndexOf(s) % 2 == 0 ? 0 : 20);
                }
                Canvas.SetLeft(blk, x1);
                canvas.Children.Add(blk);
            }
        }
        host.SizeChanged += (_, e) => Layout(e.NewSize.Width);
        return host;
    }

    static void FillDialog(Gap g, string task)
    {
        var projects = State.ActiveProjects.ToList();
        if (projects.Count == 0) { State.Toast("Create a project first", true); return; }
        var pc = Ui.Combo(projects, projects[0], "Label");
        var desc = Ui.Input(task, "What did you work on?");
        var body = new StackPanel();
        body.Children.Add(Ui.Text($"{Fmt.Time(State.ToLocal(g.StartUtc))} – {Fmt.Time(State.ToLocal(g.EndUtc))}  ·  {Fmt.Hm((long)(g.EndUtc - g.StartUtc).TotalMilliseconds)}", 14, "MutedBrush").WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("PROJECT", pc));
        body.Children.Add(Ui.Field("DESCRIPTION", desc, 4));
        Dialogs.Show("Add time entry", body, "Add entry", () =>
        {
            if (pc.SelectedItem is not Project p) return false;
            Timeline.FillGap(g, p, desc.Text);
            State.Toast("Entry added");
            return true;
        }, 460);
    }
}
