using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Tempo;

public class ReportsPage : PageBase
{
    // remembered while Tempo is open, so coming back to Reports shows the same view
    static string preset = "This month";
    static string customFrom = "", customTo = "";
    static string projectId = "", clientId = "";
    static string billable = "All";
    static string groupBy = "Project";

    static readonly string[] Presets = { "This week", "Last week", "This month", "Last month", "Last 30 days", "This year", "All time", "Custom" };
    static readonly string[] Groups = { "Project", "Client", "Member", "Task", "Tag", "Day", "Week", "Month" };

    ReportFilter Filter()
    {
        var f = new ReportFilter { ProjectId = projectId, ClientId = clientId, Billable = billable, GroupBy = groupBy };
        if (preset == "Custom")
        {
            if (Ui.TryDate(customFrom, out var a)) f.From = a;
            if (Ui.TryDate(customTo, out var b)) f.To = b;
        }
        else { var (a, b) = ReportEngine.Preset(preset); f.From = a; f.To = b; }
        return f;
    }

    string RangeText(ReportFilter f) =>
        f.From == null && f.To == null ? Loc.T("All time")
        : $"{(f.From != null ? Fmt.Date(f.From.Value) : "…")} – {(f.To != null ? Fmt.Date(f.To.Value) : "…")}";

    string FilterText()
    {
        var parts = new List<string>();
        if (projectId != "") parts.Add(State.Proj(projectId)?.Name ?? "");
        if (clientId != "") parts.Add(State.Data.Clients.FirstOrDefault(c => c.Id == clientId)?.Name ?? "");
        if (billable != "All") parts.Add(Loc.T(billable));
        parts.Add(Loc.T("GROUP BY") + ": " + Loc.T(groupBy));
        return string.Join("  ·  ", parts.Where(x => x.Length > 0));
    }

    public override void Build()
    {
        var d = State.Data;
        var f = Filter();
        var res = ReportEngine.Compute(f);
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Reports", "See where your time and money went, by project, client, task, tag or period.",
            Ui.Btn("Tax report", () => TaxReportDialog(f), "GlassButton", Icons.Receipt),
            Ui.Btn("Export CSV", () => ExportCsv(res, f), "GlassButton", Icons.Download),
            Ui.Btn("Export report PDF", () => ExportPdf(res, f), "GlassButton", Icons.Download)));

        // ---- filters
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        Border Slot(string label, FrameworkElement ctl, double minWidth)
        {
            ctl.MinWidth = minWidth;
            var field = Ui.Field(label, ctl, 0);
            field.Margin = new Thickness(0, 0, 12, 12);
            return new Border { Child = field };
        }
        var pc = Ui.Combo(Presets, preset);
        pc.SelectionChanged += (_, _) => { preset = (string)pc.SelectedItem; Build(); };
        wrap.Children.Add(Slot("DATE RANGE", pc, 160));

        if (preset == "Custom")
        {
            var a = Ui.Input(customFrom, "yyyy-mm-dd"); var b = Ui.Input(customTo, "yyyy-mm-dd");
            a.LostFocus += (_, _) => { customFrom = a.Text; Build(); };
            b.LostFocus += (_, _) => { customTo = b.Text; Build(); };
            wrap.Children.Add(Slot("FROM DATE", a, 130)); wrap.Children.Add(Slot("TO DATE", b, 130));
        }

        var projects = new List<Project> { new() { Id = "", Name = Loc.T("All projects") } }; projects.AddRange(d.Projects);
        var prc = Ui.Combo(projects, projects.FirstOrDefault(p => p.Id == projectId), "Name");
        prc.SelectionChanged += (_, _) => { projectId = (prc.SelectedItem as Project)?.Id ?? ""; Build(); };
        wrap.Children.Add(Slot("PROJECT", prc, 200));

        var clients = new List<Client> { new() { Id = "", Name = Loc.T("All clients") } }; clients.AddRange(d.Clients.OrderBy(c => c.Name));
        var cc = Ui.Combo(clients, clients.FirstOrDefault(c => c.Id == clientId));
        cc.SelectionChanged += (_, _) => { clientId = (cc.SelectedItem as Client)?.Id ?? ""; Build(); };
        wrap.Children.Add(Slot("CLIENT", cc, 180));

        var bc = Ui.Combo(new[] { "All", "Billable", "Non-billable" }, billable);
        bc.SelectionChanged += (_, _) => { billable = (string)bc.SelectedItem; Build(); };
        wrap.Children.Add(Slot("BILLABLE", bc, 140));

        var gc = Ui.Combo(Groups, groupBy);
        gc.SelectionChanged += (_, _) => { groupBy = (string)gc.SelectedItem; Build(); };
        wrap.Children.Add(Slot("GROUP BY", gc, 140));
        root.Children.Add(wrap);

        // ---- KPIs
        bool costs = res.Cost > 0;
        showProfit = costs;
        var kpis = new UniformGrid { Columns = costs ? 5 : 4, Margin = new Thickness(0, 0, 0, 18) };
        void K(Border b, bool last = false) { b.Margin = new Thickness(0, 0, last ? 0 : 14, 0); kpis.Children.Add(b); }
        var pct = res.TotalHours > 0 ? (int)Math.Round(res.BillableHours / res.TotalHours * 100) : 0;
        K(Ui.Metric("Tracked", Fmt.Hm((long)(res.TotalHours * 3600000m)), RangeText(f)));
        K(Ui.Metric("Billable", Fmt.Hm((long)(res.BillableHours * 3600000m)), Loc.F("{0}% of tracked time", pct), "GoodBrush"));
        K(Ui.Metric("Earned", Fmt.Money(res.Earned), "billable time only", "AccentBrush"));
        K(Ui.Metric("Expenses", Fmt.Money(res.Expenses), "in this range", "WarnBrush"), !costs);
        if (costs)
        {
            var margin = res.Earned > 0 ? (int)Math.Round(res.Profit / res.Earned * 100) : 0;
            K(Ui.Metric("Profit", Fmt.Money(res.Profit), Loc.F("{0}% margin after costs", margin), res.Profit < 0 ? "DangerBrush" : "GoodBrush"), true);
        }
        root.Children.Add(kpis);
        if (res.MissingRates.Count > 0)
        {
            var warn = Ui.Text(Loc.F("No exchange rate for {0}, so those amounts are counted 1:1. Add rates in Settings → Billing defaults.", string.Join(", ", res.MissingRates.OrderBy(x => x))), 12.5, "WarnBrush", wrap: true);
            root.Children.Add(warn.WithMargin(2, -6, 0, 14));
        }
        if (!costs)
        {
            var tip = new WrapPanel { Margin = new Thickness(2, -6, 0, 14) };
            var tt = Ui.Text("Tip: set a cost rate (what an hour costs you) in Settings or on a project to see profit here.", 12.5, "MutedBrush", wrap: true);
            tt.VerticalAlignment = VerticalAlignment.Center; tt.Margin = new Thickness(0, 0, 8, 0);
            tip.Children.Add(tt);
            var go = Ui.Btn("Open Settings", () => MainWindow.Instance.Navigate("Settings"), "GhostButton"); go.Padding = new Thickness(8, 4, 8, 4);
            tip.Children.Add(go);
            root.Children.Add(tip);
        }

        // ---- results
        var card = new StackPanel();
        if (res.Rows.Count == 0)
        {
            card.Children.Add(Ui.Empty(Icons.Chart, "Nothing tracked in this range.", "Try another date range or clear a filter."));
        }
        else
        {
            var head = new Grid { Margin = new Thickness(4, 0, 4, 8) };
            SetCols(head);
            head.Children.Add(Ui.Text(Loc.T("GROUP BY") + " · " + Loc.T(groupBy), 10.5, "FaintBrush", FontWeights.SemiBold));
            AddCell(head, 2, Ui.Text(Loc.T("HOURS"), 10.5, "FaintBrush", FontWeights.SemiBold), true);
            AddCell(head, 3, Ui.Text(Loc.T("BILLABLE HOURS"), 10.5, "FaintBrush", FontWeights.SemiBold), true);
            AddCell(head, 4, Ui.Text(Loc.T("EARNED"), 10.5, "FaintBrush", FontWeights.SemiBold), true);
            if (costs) AddCell(head, 5, Ui.Text(Loc.T("PROFIT"), 10.5, "FaintBrush", FontWeights.SemiBold), true);
            card.Children.Add(head);

            var max = res.Rows.Max(r => r.Hours); if (max <= 0) max = 1;
            foreach (var row in res.Rows.Take(200))
            {
                var g = new Grid { Margin = new Thickness(4, 6, 4, 6) };
                SetCols(g);
                g.Children.Add(Ui.Text(row.Label, 13.5, "TextBrush", FontWeights.SemiBold));
                var bar = Ui.Progress((double)(row.Hours / max), "AccentGradientBrush", 8); bar.Margin = new Thickness(10, 0, 14, 0); bar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(bar, 1); g.Children.Add(bar);
                AddCell(g, 2, Ui.Text(row.Hours.ToString("0.00", CultureInfo.CurrentCulture), 13.5, "TextBrush", FontWeights.SemiBold), true);
                AddCell(g, 3, Ui.Text(row.BillableHours.ToString("0.00", CultureInfo.CurrentCulture), 13.5, "MutedBrush"), true);
                AddCell(g, 4, Ui.Text(Fmt.Money(row.Earned), 13.5, "TextBrush"), true);
                if (costs) AddCell(g, 5, Ui.Text(Fmt.Money(row.Profit), 13.5, row.Profit < 0 ? "DangerBrush" : "MutedBrush"), true);
                card.Children.Add(g);
            }
            var tot = new Grid { Margin = new Thickness(4, 14, 4, 2) };
            SetCols(tot);
            tot.Children.Add(Ui.Text("Total", 13.5, "TextBrush", FontWeights.Bold));
            AddCell(tot, 2, Ui.Text(res.TotalHours.ToString("0.00", CultureInfo.CurrentCulture), 13.5, "TextBrush", FontWeights.Bold), true);
            AddCell(tot, 3, Ui.Text(res.BillableHours.ToString("0.00", CultureInfo.CurrentCulture), 13.5, "MutedBrush", FontWeights.Bold), true);
            AddCell(tot, 4, Ui.Text(Fmt.Money(res.Earned), 13.5, "AccentBrush", FontWeights.Bold), true);
            if (costs) AddCell(tot, 5, Ui.Text(Fmt.Money(res.Earned - res.Cost), 13.5, "TextBrush", FontWeights.Bold), true);
            card.Children.Add(tot);
        }
        root.Children.Add(Ui.Card(card, 18));
        Content = root;
    }

    static bool showProfit;

    static void SetCols(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showProfit ? 110 : 0) });   // profit (only when cost rates are set)
    }

    /// <summary>Tax collected per tax and currency for the chosen range, with CSV export (for VAT / GST / sales-tax returns).</summary>
    static void TaxReportDialog(ReportFilter f)
    {
        var from = f.From ?? State.Data.Invoices.Select(i => State.ToLocal(i.IssuedUtc).Date).DefaultIfEmpty(State.TodayLocal).Min();
        var to = f.To ?? State.TodayLocal;
        var rows = ReportEngine.TaxReport(from, to);
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.F("Invoices issued {0} – {1}. Void invoices and quotes are left out.", Fmt.Date(from), Fmt.Date(to)), 13, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 12));
        if (rows.Count == 0) body.Children.Add(Ui.Text("No tax was charged on invoices in this range.", 13.5, "FaintBrush"));
        else
        {
            var g = new Grid();
            foreach (var w in new[] { 1.6, 1, 1.2, 1.2, 0.7 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
            int r = 0;
            void Row(bool head, params string[] cells)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cells.Length; c++)
                {
                    var t = Ui.Text(cells[c], head ? 10.5 : 13.5, head ? "FaintBrush" : "TextBrush", head ? FontWeights.SemiBold : FontWeights.Normal);
                    t.Margin = new Thickness(0, 4, 8, 4); if (c > 0) t.TextAlignment = TextAlignment.Right;
                    Grid.SetRow(t, r); Grid.SetColumn(t, c); g.Children.Add(t);
                }
                r++;
            }
            Row(true, Loc.T("TAX"), Loc.T("RATE %"), Loc.T("TAXABLE AMOUNT"), Loc.T("TAX"), Loc.T("INVOICES"));
            foreach (var x in rows) Row(false, Loc.T(x.Label), x.Label == "Reverse charge" ? "—" : Fmt.Dec(x.Percent), Fmt.Money(x.Base, x.Currency), Fmt.Money(x.Tax, x.Currency), x.Invoices.ToString());
            body.Children.Add(g);
        }
        if (!Dialogs.Show("Tax report", body, rows.Count > 0 ? "Export CSV…" : "Close", null, 640) || rows.Count == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { Title = Loc.T("Tax report"), Filter = "CSV (*.csv)|*.csv", FileName = $"tax-report-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return;
        var inv = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder("Currency,Tax,Rate %,Taxable amount,Tax,Invoices\r\n");
        foreach (var x in rows) sb.Append($"{x.Currency},\"{x.Label.Replace("\"", "\"\"")}\",{x.Percent.ToString(inv)},{x.Base.ToString("0.00", inv)},{x.Tax.ToString("0.00", inv)},{x.Invoices}\r\n");
        try { System.IO.File.WriteAllText(dlg.FileName, sb.ToString()); State.Toast("Tax report saved"); }
        catch (Exception ex) { State.Toast(ex.Message, true); }
    }

    static void AddCell(Grid g, int col, UIElement e, bool right)
    {
        if (right && e is TextBlock t) t.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(e, col); g.Children.Add(e);
    }

    void ExportCsv(ReportResult res, ReportFilter f)
    {
        if (res.Rows.Count == 0) { State.Toast("There are no entries to export yet.", true); return; }
        var dlg = new SaveFileDialog { Title = "Export CSV", Filter = "CSV file (*.csv)|*.csv", DefaultExt = "csv", FileName = $"tempo-report-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllText(dlg.FileName, ReportEngine.Csv(res, f), new UTF8Encoding(true)); State.Toast("Report saved"); }
        catch (Exception ex) { State.Toast(Loc.T("Export failed: ") + ex.Message, true); }
    }

    void ExportPdf(ReportResult res, ReportFilter f)
    {
        if (res.Rows.Count == 0) { State.Toast("There are no entries to export yet.", true); return; }
        var dlg = new SaveFileDialog { Title = "Export report PDF", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = "pdf", FileName = $"tempo-report-{DateTime.Now:yyyy-MM-dd}.pdf" };
        if (dlg.ShowDialog() != true) return;
        try { ReportEngine.WritePdf(res, f, RangeText(f), FilterText(), dlg.FileName); State.Toast("Report saved"); }
        catch (Exception ex) { State.Toast(Loc.T("Export failed: ") + ex.Message, true); }
    }
}
