using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tempo;

public class InvoicesPage : PageBase
{
    ComboBox project = null!, preset = null!, group = null!;
    TextBox number = null!, from = null!, to = null!, dueDays = null!, taxLabel = null!, taxPct = null!, discount = null!, po = null!, note = null!;
    StackPanel previewHost = null!;
    TextBlock err = null!;
    Button export = null!;
    bool building;
    string? pendingProject = null;

    static readonly string[] Presets = { "This week", "Last week", "This month", "Last month", "All unbilled", "Custom" };

    public void SelectProject(string id)
    {
        var p = State.Proj(id);
        if (p != null && project != null) project.SelectedItem = project.Items.Cast<Project>().FirstOrDefault(x => x.Id == id);
    }

    public override void Build()
    {
        building = true;
        var d = State.Data; var s = d.Settings;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Invoices", "Pick a project and date range. Its saved hourly rates do the rest.",
            Ui.Btn("Billing details", () => MainWindow.Instance.Navigate("Settings"), "GhostButton")));

        // ---------- builder ----------
        var projects = d.Projects.Where(p => !p.Archived || d.Sessions.Any(x => x.ProjectId == p.Id && x.InvoiceId == null)).ToList();
        project = Ui.Combo(projects, projects.FirstOrDefault(p => p.Id == pendingProject) ?? projects.OrderByDescending(p => State.UnbilledFor(p.Id)).FirstOrDefault(), "Label");
        preset = Ui.Combo(Presets, "All unbilled");
        group = Ui.Combo(new[] { "Task", "Day", "Entry" }, s.InvoiceGrouping);
        number = Ui.Input(d.InvoiceNumber, "INV-2026-001");
        from = Ui.Input(); to = Ui.Input();
        dueDays = Ui.Input(s.PaymentTermsDays.ToString(), "14");
        taxLabel = Ui.Input(s.TaxLabel, "Tax"); taxPct = Ui.Input(Fmt.Dec(s.DefaultTaxPercent), "0");
        discount = Ui.Input("0", "0"); po = Ui.Input("", "Optional");
        note = Ui.Input(d.Billing.Note, "Thank you for your business.");
        err = Dialogs.ErrorLabel();

        void Changed() { if (!building) UpdatePreview(); }
        void PresetChanged() { ApplyPreset(); Changed(); }
        project.SelectionChanged += (_, _) => PresetChanged();
        preset.SelectionChanged += (_, _) => PresetChanged();
        group.SelectionChanged += (_, _) => Changed();
        foreach (var t in new[] { number, from, to, dueDays, taxLabel, taxPct, discount, po, note }) t.TextChanged += (_, _) => Changed();
        from.TextChanged += (_, _) => { if (!building && (string)preset.SelectedItem != "Custom" && !applying) preset.SelectedItem = "Custom"; };
        to.TextChanged += (_, _) => { if (!building && (string)preset.SelectedItem != "Custom" && !applying) preset.SelectedItem = "Custom"; };

        var form = new StackPanel();
        form.Children.Add(Ui.Text("Invoice details", 18, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 16));
        form.Children.Add(Ui.Field("PROJECT", project));
        form.Children.Add(Pair(Ui.Field("INVOICE NUMBER", number), Ui.Field("PO NUMBER", po)));
        form.Children.Add(Ui.Field("DATE RANGE", preset, 8));
        form.Children.Add(Pair(Ui.Field("FROM", from), Ui.Field("TO", to)));
        form.Children.Add(Pair(Ui.Field("GROUP LINES BY", group), Ui.Field("DUE IN (DAYS)", dueDays)));
        form.Children.Add(Pair(Ui.Field(taxLabelCaption(), taxLabel), Ui.Field("RATE %", taxPct)));
        form.Children.Add(Ui.Field("DISCOUNT %", discount));
        form.Children.Add(Ui.Field("NOTE TO CLIENT", note, 4));
        var left = Ui.Card(form);

        // ---------- preview ----------
        previewHost = new StackPanel();
        export = Ui.Btn("Export invoice PDF", Export, "PrimaryButton", Icons.Download);
        export.Padding = new Thickness(18, 12, 18, 12);
        var right = new StackPanel();
        right.Children.Add(Ui.Text("Live preview", 18, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 14));
        right.Children.Add(previewHost);
        right.Children.Add(err.WithMargin(0, 12, 0, 0));
        right.Children.Add(export.WithMargin(0, 14, 0, 0));
        var rightCard = Ui.Card(right);

        var cols = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
        left.Margin = new Thickness(0, 0, 9, 0); rightCard.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(rightCard, 1);
        cols.Children.Add(left); cols.Children.Add(rightCard);
        root.Children.Add(cols);

        // ---------- history ----------
        root.Children.Add(Ui.Card(BuildHistory()));

        Content = root;
        building = false;
        ApplyPreset();
        UpdatePreview();
    }

    static string taxLabelCaption() => "TAX LABEL";

    static Grid Pair(FrameworkElement a, FrameworkElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(b, 2);
        g.Children.Add(a); g.Children.Add(b);
        return g;
    }

    bool applying;
    void ApplyPreset()
    {
        var p = (string?)preset.SelectedItem;
        if (p == null || p == "Custom") { SetDates(null, null); return; }
        var today = State.TodayLocal;
        DateTime f, t;
        switch (p)
        {
            case "This week": f = State.WeekStart(today); t = f.AddDays(6); break;
            case "Last week": f = State.WeekStart(today).AddDays(-7); t = f.AddDays(6); break;
            case "This month": f = new DateTime(today.Year, today.Month, 1); t = f.AddMonths(1).AddDays(-1); break;
            case "Last month": f = new DateTime(today.Year, today.Month, 1).AddMonths(-1); t = f.AddMonths(1).AddDays(-1); break;
            default:
                var pr = project.SelectedItem as Project;
                var first = State.Data.Sessions.Where(x => pr != null && x.ProjectId == pr.Id && x.InvoiceId == null).Select(x => State.ToLocal(x.StartedAtUtc).Date).DefaultIfEmpty(today).Min();
                f = first; t = today; break;
        }
        SetDates(f, t);
    }

    void SetDates(DateTime? f, DateTime? t)
    {
        applying = true;
        if (f != null) from.Text = f.Value.ToString("yyyy-MM-dd");
        if (t != null) to.Text = t.Value.ToString("yyyy-MM-dd");
        from.IsEnabled = to.IsEnabled = (string?)preset.SelectedItem == "Custom";
        applying = false;
    }

    sealed record Draft(Invoice Invoice, List<Session> Sessions);

    Draft? Calc(out string? error)
    {
        error = null;
        if (project.SelectedItem is not Project p) { error = "Create a project to invoice."; return null; }
        if (!Ui.TryDate(from.Text, out var f) || !Ui.TryDate(to.Text, out var t)) { error = "Dates should look like 2026-11-30."; return null; }
        if (t < f) { error = "The end date is before the start date."; return null; }
        if (!Ui.TryDec(taxPct.Text.Length == 0 ? "0" : taxPct.Text, out var tax) || tax < 0 || tax > 100) { error = "Tax % should be between 0 and 100."; return null; }
        if (!Ui.TryDec(discount.Text.Length == 0 ? "0" : discount.Text, out var disc) || disc < 0 || disc > 100) { error = "Discount % should be between 0 and 100."; return null; }
        if (!int.TryParse(dueDays.Text.Length == 0 ? "0" : dueDays.Text, out var due) || due < 0 || due > 365) { error = "Due days should be a number from 0 to 365."; return null; }

        var d = State.Data; var s = d.Settings; var b = d.Billing;
        var (lines, sessions) = InvoiceService.BuildLines(p, f, t, (string)group.SelectedItem!, s.RoundingMinutes);
        var now = DateTime.UtcNow;
        var inv = new Invoice
        {
            Number = number.Text.Trim(), ProjectId = p.Id, ProjectName = p.Name, ClientName = p.Client, ClientEmail = p.ClientEmail, ClientAddress = p.ClientAddress,
            PoNumber = po.Text.Trim(), IssuedUtc = now, DueUtc = now.AddDays(due), PeriodFrom = f, PeriodTo = t, TimeZoneId = State.Tz.Id, Currency = s.CurrencyCode,
            Lines = lines, DiscountPercent = disc, TaxLabel = string.IsNullOrWhiteSpace(taxLabel.Text) ? "Tax" : taxLabel.Text.Trim(), TaxPercent = tax,
            Note = note.Text.Trim(), PaymentInstructions = b.PaymentInstructions, StudioName = b.Name, StudioEmail = b.Email, StudioAddress = b.Address,
            TaxId = b.TaxId, LogoPath = b.LogoPath, SessionIds = sessions.Select(x => x.Id).ToList()
        };
        InvoiceService.ApplyTotals(inv);
        return new Draft(inv, sessions);
    }

    void UpdatePreview()
    {
        previewHost.Children.Clear();
        var draft = Calc(out var error);
        Dialogs.SetError(err, error);
        export.IsEnabled = draft != null && draft.Invoice.Lines.Count > 0;
        if (draft == null) return;
        var inv = draft.Invoice;

        // "paper" stays light in both themes so what you see matches the PDF
        var ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x2B, 0x34));
        var mute = new SolidColorBrush(Color.FromRgb(0x5E, 0x6A, 0x74));
        var coral = new SolidColorBrush(Color.FromRgb(0xE8, 0x57, 0x3C));
        TextBlock T(string s, double size, Brush fg, FontWeight? w = null) => new() { Text = s, FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };

        var paper = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hl = new StackPanel();
        hl.Children.Add(T(string.IsNullOrWhiteSpace(inv.StudioName) ? "Your studio" : inv.StudioName, 16, ink, FontWeights.SemiBold));
        hl.Children.Add(T(inv.StudioEmail, 12, mute));
        head.Children.Add(hl);
        var hr = new StackPanel();
        hr.Children.Add(T("INVOICE", 22, coral, FontWeights.SemiBold));
        hr.Children.Add(T(inv.Number, 12, ink, FontWeights.SemiBold));
        Grid.SetColumn(hr, 1); head.Children.Add(hr);
        paper.Children.Add(head);

        var meta = new Grid { Margin = new Thickness(0, 16, 0, 14) };
        meta.ColumnDefinitions.Add(new ColumnDefinition()); meta.ColumnDefinitions.Add(new ColumnDefinition());
        var bt = new StackPanel();
        bt.Children.Add(T("BILL TO", 10, mute, FontWeights.SemiBold));
        bt.Children.Add(T(inv.ClientName, 13, ink, FontWeights.SemiBold));
        bt.Children.Add(T(inv.ProjectName, 12, mute));
        var dt = new StackPanel();
        dt.Children.Add(T("PERIOD", 10, mute, FontWeights.SemiBold));
        dt.Children.Add(T($"{inv.PeriodFrom:MMM d} – {inv.PeriodTo:MMM d, yyyy}", 12.5, ink));
        dt.Children.Add(T($"Due {Fmt.Date(State.ToLocal(inv.DueUtc))}", 12, mute));
        Grid.SetColumn(dt, 1);
        meta.Children.Add(bt); meta.Children.Add(dt);
        paper.Children.Add(meta);

        paper.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE0)), Margin = new Thickness(0, 0, 0, 8) });
        if (inv.Lines.Count == 0)
        {
            paper.Children.Add(T("Nothing to invoice for this project and date range.", 13, mute).WithMargin(0, 14, 0, 14));
        }
        foreach (var l in inv.Lines.Take(7))
        {
            var r = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            r.ColumnDefinitions.Add(new ColumnDefinition()); r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            r.Children.Add(T(l.Description, 12.5, ink));
            var h = T(l.Hours.ToString("0.00") + " h", 12.5, mute); h.TextAlignment = TextAlignment.Right; Grid.SetColumn(h, 1); r.Children.Add(h);
            var a = T(Fmt.Money(l.Amount), 12.5, ink, FontWeights.SemiBold); a.TextAlignment = TextAlignment.Right; Grid.SetColumn(a, 2); r.Children.Add(a);
            paper.Children.Add(r);
        }
        if (inv.Lines.Count > 7) paper.Children.Add(T($"+ {inv.Lines.Count - 7} more line{(inv.Lines.Count - 7 == 1 ? "" : "s")} in the PDF", 12, mute).WithMargin(0, 4, 0, 0));

        if (inv.Lines.Count > 0)
        {
            paper.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE0)), Margin = new Thickness(0, 10, 0, 8) });
            void Tot(string k, string v, bool big = false)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(T(k, big ? 13 : 12.5, big ? ink : mute, FontWeights.SemiBold));
                var vv = T(v, big ? 18 : 12.5, big ? coral : ink, FontWeights.SemiBold); Grid.SetColumn(vv, 1); g.Children.Add(vv);
                paper.Children.Add(g);
            }
            Tot("Total hours", inv.TotalHours.ToString("0.00"));
            if (inv.DiscountAmount != 0 || inv.TaxAmount != 0) Tot("Subtotal", Fmt.Money(inv.Subtotal));
            if (inv.DiscountAmount != 0) Tot($"Discount ({Fmt.Dec(inv.DiscountPercent)}%)", "-" + Fmt.Money(inv.DiscountAmount));
            if (inv.TaxAmount != 0) Tot($"{inv.TaxLabel} ({Fmt.Dec(inv.TaxPercent)}%)", Fmt.Money(inv.TaxAmount));
            Tot("TOTAL DUE", Fmt.Money(inv.Total), true);
        }

        previewHost.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(14), Padding = new Thickness(24),
            Child = paper, BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderThickness = new Thickness(1)
        });
        if (inv.Lines.Count == 0 && error == null) Dialogs.SetError(err, null);
        if (string.IsNullOrWhiteSpace(State.Data.Billing.Name))
            Dialogs.SetError(err, "Tip: add your studio name and payment details in Settings → Billing profile so they appear on the PDF.");
    }

    // ---------- export ----------
    void Export()
    {
        var draft = Calc(out var error);
        if (draft == null || draft.Invoice.Lines.Count == 0) { Dialogs.SetError(err, error ?? "Nothing to invoice."); return; }
        var inv = draft.Invoice;
        if (string.IsNullOrWhiteSpace(inv.Number)) { Dialogs.SetError(err, "Give the invoice a number."); return; }
        if (State.Data.Invoices.Any(i => i.Number.Equals(inv.Number, StringComparison.OrdinalIgnoreCase) && i.Status != "Void"))
        { Dialogs.SetError(err, $"Invoice number {inv.Number} is already used."); return; }

        var dlg = new SaveFileDialog
        {
            Title = "Export invoice PDF", Filter = "PDF invoice (*.pdf)|*.pdf", DefaultExt = "pdf",
            FileName = InvoiceService.SafeName($"{inv.Number} {inv.ClientName}") + ".pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            InvoiceService.WritePdf(inv, dlg.FileName);
        }
        catch (Exception ex) { State.Toast("The invoice could not be saved: " + ex.Message, true); return; }

        inv.FilePath = dlg.FileName;
        foreach (var s in draft.Sessions) s.InvoiceId = inv.Id;
        var d = State.Data;
        d.Invoices.Add(inv);
        d.InvoiceCounter = Math.Max(d.InvoiceCounter, 1) + 1;
        d.InvoiceNumber = State.NextInvoiceNumber();
        State.Notify();
        State.Toast($"Invoice {inv.Number} exported · {draft.Sessions.Count} entries marked as billed");
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
    }

    // ---------- history ----------
    StackPanel BuildHistory()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text("Invoice history", 18, "TextBrush", FontWeights.SemiBold));
        sp.Children.Add(Ui.Text("Track what's been sent and what's been paid.", 13, "MutedBrush").WithMargin(0, 2, 0, 12));
        var all = State.Data.Invoices.OrderByDescending(i => i.IssuedUtc).ToList();
        if (all.Count == 0)
        {
            sp.Children.Add(Ui.Empty(Icons.Doc, "No invoices yet", "Exported invoices appear here so you can re-download them or mark them paid."));
            return sp;
        }
        var outstanding = all.Where(i => i.Status == "Issued").Sum(i => i.Total);
        if (outstanding > 0) sp.Children.Add(Ui.Text($"Outstanding: {Fmt.Money(outstanding)}", 13, "WarnBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 8));
        foreach (var inv in all) sp.Children.Add(HistoryRow(inv));
        return sp;
    }

    Border HistoryRow(Invoice inv)
    {
        var g = new Grid();
        for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var l = new StackPanel();
        l.Children.Add(Ui.Text(inv.Number + "  ·  " + inv.ClientName, 14.5, "TextBrush", FontWeights.SemiBold));
        l.Children.Add(Ui.Text($"{inv.ProjectName}  ·  Issued {Fmt.Date(State.ToLocal(inv.IssuedUtc))}  ·  Due {Fmt.Date(State.ToLocal(inv.DueUtc))}", 12.5, "MutedBrush"));
        g.Children.Add(l);

        var total = Ui.Text(Fmt.Money(inv.Total, inv.Currency), 15, "TextBrush", FontWeights.SemiBold);
        total.Margin = new Thickness(12, 0, 14, 0); total.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(total, 1); g.Children.Add(total);

        var overdue = inv.Status == "Issued" && inv.DueUtc < DateTime.UtcNow;
        var pill = inv.Status switch
        {
            "Paid" => Ui.Pill("Paid", "GoodBrush", "GoodSoftBrush"),
            "Void" => Ui.Pill("Void", "FaintBrush", "FieldBrush"),
            _ => overdue ? Ui.Pill("Overdue", "DangerBrush", "DangerSoftBrush") : Ui.Pill("Issued", "WarnBrush", "WarnSoftBrush")
        };
        pill.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(pill, 2); g.Children.Add(pill);

        var acts = new StackPanel { Orientation = Orientation.Horizontal };
        if (File.Exists(inv.FilePath))
            acts.Children.Add(Ui.IconBtn(Icons.Doc, "Open PDF", () => { try { Process.Start(new ProcessStartInfo(inv.FilePath) { UseShellExecute = true }); } catch (Exception ex) { State.Toast(ex.Message, true); } }));
        acts.Children.Add(Ui.IconBtn(Icons.Download, "Re-export PDF", () => ReExport(inv)));
        if (inv.Status == "Issued")
            acts.Children.Add(Ui.IconBtn(Icons.Check, "Mark as paid", () => { inv.Status = "Paid"; inv.PaidUtc = DateTime.UtcNow; State.Notify(); State.Toast($"{inv.Number} marked as paid"); }));
        else if (inv.Status == "Paid")
            acts.Children.Add(Ui.IconBtn(Icons.Edit, "Mark as unpaid", () => { inv.Status = "Issued"; inv.PaidUtc = null; State.Notify(); }));
        if (inv.Status != "Void")
            acts.Children.Add(Ui.IconBtn(Icons.Trash, "Void invoice and release its entries", () =>
            {
                if (!Dialogs.Confirm("Void invoice", $"Void {inv.Number}? Its {inv.SessionIds.Count} time entries become unbilled again so you can re-invoice them.", "Void invoice", true)) return;
                inv.Status = "Void";
                foreach (var s in State.Data.Sessions.Where(s => s.InvoiceId == inv.Id)) s.InvoiceId = null;
                State.Notify(); State.Toast($"{inv.Number} voided");
            }, true));
        Grid.SetColumn(acts, 3); g.Children.Add(acts);

        var b = new Border { Padding = new Thickness(12, 10, 8, 10), CornerRadius = new CornerRadius(12), Child = g, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }

    static void ReExport(Invoice inv)
    {
        var dlg = new SaveFileDialog { Title = "Export invoice PDF", Filter = "PDF invoice (*.pdf)|*.pdf", DefaultExt = "pdf", FileName = InvoiceService.SafeName($"{inv.Number} {inv.ClientName}") + ".pdf" };
        if (dlg.ShowDialog() != true) return;
        try { InvoiceService.WritePdf(inv, dlg.FileName); inv.FilePath = dlg.FileName; State.Notify(); State.Toast("Invoice PDF saved"); }
        catch (Exception ex) { State.Toast("The invoice could not be saved: " + ex.Message, true); }
    }
}
