using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tempo;

public class InvoicesPage : PageBase
{
    static string tab = "Invoices";                       // Invoices | Quotes | Recurring (remembered while Tempo is open)
    public static void SetTab(string t) => tab = t;
    static readonly string[] Tabs = { "Invoices", "Quotes", "Recurring" };
    static readonly string[] Presets = { "This week", "Last week", "This month", "Last month", "All unbilled", "Custom" };
    static readonly string[] Templates = { "Classic", "Minimal", "Bold" };

    bool IsQuote => tab == "Quotes";
    string Kind => IsQuote ? "Quote" : "Invoice";

    ComboBox project = null!, preset = null!, group = null!, template = null!;
    TextBox tax2Label = null!, tax2Pct = null!;
    CheckBox reverseCharge = null!;
    TextBox number = null!, from = null!, to = null!, dueDays = null!, taxLabel = null!, taxPct = null!, discount = null!, po = null!, note = null!, payLink = null!, fixedAmount = null!;
    CheckBox includeTime = null!, includeExpenses = null!, includeTimesheet = null!, billFixed = null!;
    StackPanel previewHost = null!, fixedRow = null!;
    TextBlock err = null!;
    Button export = null!;
    bool building, applying;
    readonly List<InvoiceLine> custom = new();

    public void SelectProject(string id)
    {
        if (project != null) project.SelectedItem = project.Items.Cast<Project>().FirstOrDefault(x => x.Id == id);
    }

    public override void Build()
    {
        building = true;
        var d = State.Data; var s = d.Settings;
        var root = new StackPanel();
        string subtitle = tab switch
        {
            "Quotes" => "Send a price before the work starts. Accepted quotes turn into invoices in one click.",
            "Recurring" => "Retainers and repeat work: Tempo creates each invoice on its due date.",
            _ => "Pick a project and date range. Its saved hourly rates do the rest.",
        };
        root.Children.Add(tab == "Recurring"
            ? Ui.Header("Invoices", subtitle, Ui.Btn("New recurring invoice", () => EditRecurring(null), "PrimaryButton", Icons.Plus))
            : Ui.Header("Invoices", subtitle, Ui.Btn("Billing details", () => MainWindow.Instance.Navigate("Settings"), "GhostButton")));
        root.Children.Add(Ui.Tabs(Tabs, Array.IndexOf(Tabs, tab), i => { tab = Tabs[i]; Build(); }));

        if (tab == "Recurring") { BuildRecurring(root); Content = root; building = false; return; }

        // ---------- builder ----------
        var projects = d.Projects.Where(p => !p.Archived || d.Sessions.Any(x => x.ProjectId == p.Id && x.InvoiceId == null)).ToList();
        project = Ui.Combo(projects, projects.OrderByDescending(p => State.UnbilledFor(p.Id)).FirstOrDefault(), "Label");
        preset = Ui.Combo(Presets, "All unbilled");
        group = Ui.Combo(new[] { "Task", "Day", "Entry" }, s.InvoiceGrouping);
        template = Ui.Combo(Templates, s.InvoiceTemplate);
        number = Ui.Input(IsQuote ? InvoiceService.NextQuoteNumber() : d.InvoiceNumber, IsQuote ? "QUO-2026-001" : "INV-2026-001");
        from = Ui.Input(); to = Ui.Input();
        dueDays = Ui.Input(s.PaymentTermsDays.ToString(), "14");
        taxLabel = Ui.Input(s.TaxLabel, "Tax"); taxPct = Ui.Input(Fmt.Dec(s.DefaultTaxPercent), "0");
        tax2Label = Ui.Input("", "e.g. PST"); tax2Pct = Ui.Input("", "0");
        reverseCharge = Ui.Check("Reverse charge: the customer accounts for VAT (no tax added)", false);
        discount = Ui.Input("0", "0"); po = Ui.Input("", "Optional");
        note = Ui.Input(d.Billing.Note, "Thank you for your business.");
        payLink = Ui.Input(s.DefaultPayLink, "https://…");
        includeTime = Ui.Check("Include tracked time", true);
        includeExpenses = Ui.Check("Include billable expenses", true);
        includeTimesheet = Ui.Check("Attach a timesheet page", false);
        billFixed = Ui.Check("Bill the fixed fee", false);
        fixedAmount = Ui.Input("", "0");
        fixedRow = new StackPanel { Visibility = Visibility.Collapsed };
        err = Dialogs.ErrorLabel();
        custom.Clear();

        void Changed() { if (!building) UpdatePreview(); }
        void ProjectDefaults()
        {
            if (project.SelectedItem is not Project p) return;
            var c = State.ClientOf(p);
            dueDays.Text = (c?.PaymentTermsDays ?? (IsQuote ? 30 : s.PaymentTermsDays)).ToString();
            taxPct.Text = Fmt.Dec(c?.TaxPercent ?? s.DefaultTaxPercent);
            reverseCharge.IsChecked = c?.ReverseCharge == true;
            bool fixedProject = p.BudgetType == "Fixed" && !IsQuote;
            fixedRow.Visibility = fixedProject ? Visibility.Visible : Visibility.Collapsed;
            fixedAmount.Text = fixedProject ? Fmt.Dec(p.BudgetValue) : "";
            billFixed.IsChecked = fixedProject;
            includeTime.IsChecked = !fixedProject;
        }
        ProjectDefaults();
        project.SelectionChanged += (_, _) => { building = true; ProjectDefaults(); building = false; ApplyPreset(); Changed(); };
        preset.SelectionChanged += (_, _) => { ApplyPreset(); Changed(); };
        group.SelectionChanged += (_, _) => Changed();
        template.SelectionChanged += (_, _) => Changed();
        foreach (var t in new[] { number, from, to, dueDays, taxLabel, taxPct, tax2Label, tax2Pct, discount, po, note, payLink, fixedAmount }) t.TextChanged += (_, _) => Changed();
        foreach (var c in new[] { includeTime, includeExpenses, includeTimesheet, billFixed, reverseCharge }) { c.Checked += (_, _) => Changed(); c.Unchecked += (_, _) => Changed(); }
        from.TextChanged += (_, _) => { if (!building && (string)preset.SelectedItem != "Custom" && !applying) preset.SelectedItem = "Custom"; };
        to.TextChanged += (_, _) => { if (!building && (string)preset.SelectedItem != "Custom" && !applying) preset.SelectedItem = "Custom"; };

        var form = new StackPanel();
        form.Children.Add(Ui.Text(IsQuote ? "Quote details" : "Invoice details", 18, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 16));
        form.Children.Add(Ui.Field("PROJECT", project));
        form.Children.Add(Pair(Ui.Field(IsQuote ? "QUOTE NUMBER" : "INVOICE NUMBER", number), Ui.Field("PO NUMBER", po)));
        if (!IsQuote)
        {
            form.Children.Add(Ui.Field("DATE RANGE", preset, 8));
            form.Children.Add(Pair(Ui.Field("FROM DATE", from), Ui.Field("TO DATE", to)));
            form.Children.Add(Pair(Ui.Field("GROUP LINES BY", group), Ui.Field("DUE IN (DAYS)", dueDays)));
        }
        else form.Children.Add(Pair(Ui.Field("VALID FOR (DAYS)", dueDays), Ui.Field("TEMPLATE", template)));
        form.Children.Add(Pair(Ui.Field("TAX LABEL", taxLabel), Ui.Field("RATE %", taxPct)));
        form.Children.Add(Pair(Ui.Field("SECOND TAX (OPTIONAL)", tax2Label), Ui.Field("RATE %", tax2Pct)));
        form.Children.Add(reverseCharge.WithMargin(0, 0, 0, 14));
        form.Children.Add(Pair(Ui.Field("DISCOUNT %", discount), IsQuote ? new StackPanel() : Ui.Field("TEMPLATE", template)));
        if (!IsQuote)
        {
            form.Children.Add(Ui.Field("PAY LINK (OPTIONAL)", payLink));
            var checks = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            includeTime.Margin = new Thickness(0, 0, 0, 8); includeExpenses.Margin = new Thickness(0, 0, 0, 8); includeTimesheet.Margin = new Thickness(0, 0, 0, 0);
            checks.Children.Add(includeTime); checks.Children.Add(includeExpenses); checks.Children.Add(includeTimesheet);
            form.Children.Add(checks);
            billFixed.Margin = new Thickness(0, 0, 0, 8);
            fixedRow.Children.Add(billFixed);
            fixedRow.Children.Add(Ui.Field(Loc.F("FIXED FEE ({0})", d.Settings.CurrencyCode), fixedAmount));
            form.Children.Add(fixedRow);
        }
        form.Children.Add(Ui.Text("LINE ITEMS", 12, "MutedBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 6));
        var lineBox = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        lineBox.Children.Add(LinesEditor.Create(custom, Changed));
        form.Children.Add(lineBox);
        form.Children.Add(Ui.Field("NOTE TO CLIENT", note, 4));
        var left = Ui.Card(form);

        // ---------- preview ----------
        previewHost = new StackPanel();
        export = Ui.Btn(IsQuote ? "Export quote PDF" : "Export invoice PDF", Export, "PrimaryButton", Icons.Download);
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

    sealed record Draft(Invoice Invoice, List<Session> Sessions, List<Expense> Expenses);

    Draft? Calc(out string? error)
    {
        error = null;
        if (project.SelectedItem is not Project p) { error = "Create a project to invoice."; return null; }
        DateTime f = State.TodayLocal, t = f;
        if (!IsQuote)
        {
            if (!Ui.TryDate(from.Text, out f) || !Ui.TryDate(to.Text, out t)) { error = "Dates should look like 2026-11-30."; return null; }
            if (t < f) { error = "The end date is before the start date."; return null; }
        }
        if (!Ui.TryDec(taxPct.Text.Length == 0 ? "0" : taxPct.Text, out var tax) || tax < 0 || tax > 100) { error = "Tax % should be between 0 and 100."; return null; }
        if (!Ui.TryDec(tax2Pct.Text.Length == 0 ? "0" : tax2Pct.Text, out var tax2) || tax2 < 0 || tax2 > 100) { error = "Tax % should be between 0 and 100."; return null; }
        if (!Ui.TryDec(discount.Text.Length == 0 ? "0" : discount.Text, out var disc) || disc < 0 || disc > 100) { error = "Discount % should be between 0 and 100."; return null; }
        if (!int.TryParse(dueDays.Text.Length == 0 ? "0" : dueDays.Text, out var due) || due < 0 || due > 365) { error = "Due days should be a number from 0 to 365."; return null; }

        var d = State.Data; var s = d.Settings; var b = d.Billing;
        var client = State.ClientOf(p);
        var lines = new List<InvoiceLine>(); var sessions = new List<Session>(); var expenses = new List<Expense>();
        if (!IsQuote)
        {
            if (includeTime.IsChecked == true) { var (tl, ss) = InvoiceService.BuildLines(p, f, t, (string)group.SelectedItem!, s.RoundingMinutes); lines.AddRange(tl); sessions = ss; }
            if (includeExpenses.IsChecked == true) { var (el, es) = InvoiceService.ExpenseLines(p, f, t); lines.AddRange(el); expenses = es; }
            if (billFixed.IsChecked == true && fixedRow.Visibility == Visibility.Visible)
            {
                if (!Ui.TryDec(fixedAmount.Text, out var fa) || fa <= 0) { error = "Enter an amount greater than zero."; return null; }
                lines.Add(new InvoiceLine { Description = $"{p.Name} — {Loc.T("Fixed fee")}", Amount = Math.Round(fa, 2), Kind = "Fee" });
            }
        }
        lines.AddRange(custom.Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.Amount != 0).Select(l => new InvoiceLine { Description = l.Description, Hours = l.Hours, Rate = l.Rate, Amount = l.Amount, Kind = "Custom" }));

        if (!IsQuote && !SafeOpen.IsPayLink(payLink.Text.Trim())) { error = "Pay links must start with https://"; return null; }
        var now = DateTime.UtcNow;
        var inv = new Invoice
        {
            Kind = Kind, Language = Loc.Current, Number = number.Text.Trim(), ProjectId = p.Id, ProjectName = p.Name, ClientId = client?.Id ?? "",
            ClientName = client?.Name ?? p.Client, ClientEmail = client?.Email ?? p.ClientEmail, ClientAddress = client?.Address ?? p.ClientAddress,
            PoNumber = po.Text.Trim(), IssuedUtc = now, DueUtc = now.AddDays(due), PeriodFrom = f, PeriodTo = t, TimeZoneId = State.Tz.Id,
            Currency = string.IsNullOrEmpty(client?.Currency) ? s.CurrencyCode : client!.Currency,
            Lines = lines, DiscountPercent = disc, TaxLabel = string.IsNullOrWhiteSpace(taxLabel.Text) ? "Tax" : taxLabel.Text.Trim(), TaxPercent = tax,
            Note = note.Text.Trim(), PaymentInstructions = b.PaymentInstructions, StudioName = b.Name, StudioEmail = b.Email, StudioAddress = b.Address,
            TaxId = b.TaxId, LogoPath = b.LogoPath, Template = (string)template.SelectedItem!, PayLink = IsQuote ? "" : payLink.Text.Trim(),
            SessionIds = sessions.Select(x => x.Id).ToList(), ExpenseIds = expenses.Select(x => x.Id).ToList(),
            IncludeTimesheet = !IsQuote && includeTimesheet.IsChecked == true, Status = IsQuote ? "Sent" : "Issued"
        };
        if (inv.IncludeTimesheet) inv.Timesheet = InvoiceService.TimesheetLines(sessions);
        if (tax2 > 0)
            inv.Taxes = new() { new TaxLine { Label = inv.TaxLabel, Percent = tax }, new TaxLine { Label = string.IsNullOrWhiteSpace(tax2Label.Text) ? "Tax" : tax2Label.Text.Trim(), Percent = tax2 } };
        inv.ReverseCharge = reverseCharge.IsChecked == true;
        inv.ClientTaxId = client?.TaxId ?? "";
        InvoiceService.ApplyTotals(inv);
        return new Draft(inv, sessions, expenses);
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
        var blue = new SolidColorBrush(Color.FromRgb(0x00, 0x68, 0xF0));
        bool bold = inv.Template == "Bold", minimal = inv.Template == "Minimal";
        var accent = minimal ? ink : blue;
        TextBlock T(string s, double size, Brush fg, FontWeight? w = null) => new() { Text = s, FontSize = size, Foreground = fg, FontWeight = w ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };

        var paper = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hl = new StackPanel();
        hl.Children.Add(T(string.IsNullOrWhiteSpace(inv.StudioName) ? Loc.T("Your studio") : inv.StudioName, 16, bold ? Brushes.White : ink, FontWeights.SemiBold));
        hl.Children.Add(T(inv.StudioEmail, 12, bold ? Brushes.White : mute));
        head.Children.Add(hl);
        var hr = new StackPanel();
        hr.Children.Add(T(Loc.T(IsQuote ? "QUOTE" : "INVOICE"), 22, bold ? Brushes.White : accent, FontWeights.SemiBold));
        hr.Children.Add(T(inv.Number, 12, bold ? Brushes.White : ink, FontWeights.SemiBold));
        Grid.SetColumn(hr, 1); head.Children.Add(hr);
        if (bold) paper.Children.Add(new Border { Background = blue, Padding = new Thickness(16), Margin = new Thickness(-24, -24, -24, 14), CornerRadius = new CornerRadius(13, 13, 0, 0), Child = head });
        else paper.Children.Add(head);

        var meta = new Grid { Margin = new Thickness(0, 16, 0, 14) };
        meta.ColumnDefinitions.Add(new ColumnDefinition()); meta.ColumnDefinitions.Add(new ColumnDefinition());
        var bt = new StackPanel();
        bt.Children.Add(T(Loc.T("BILL TO"), 10, mute, FontWeights.SemiBold));
        bt.Children.Add(T(inv.ClientName, 13, ink, FontWeights.SemiBold));
        bt.Children.Add(T(inv.ProjectName, 12, mute));
        var dt = new StackPanel();
        if (!IsQuote)
        {
            dt.Children.Add(T(Loc.T("PERIOD"), 10, mute, FontWeights.SemiBold));
            dt.Children.Add(T(Fmt.Date(inv.PeriodFrom) + " – " + Fmt.Date(inv.PeriodTo), 12.5, ink));
            dt.Children.Add(T(Loc.F("Due {0}", Fmt.Date(State.ToLocal(inv.DueUtc))), 12, mute));
        }
        else
        {
            dt.Children.Add(T(Loc.T("Valid until"), 10, mute, FontWeights.SemiBold));
            dt.Children.Add(T(Fmt.Date(State.ToLocal(inv.DueUtc)), 12.5, ink));
        }
        Grid.SetColumn(dt, 1);
        meta.Children.Add(bt); meta.Children.Add(dt);
        paper.Children.Add(meta);

        paper.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE0)), Margin = new Thickness(0, 0, 0, 8) });
        if (inv.Lines.Count == 0)
            paper.Children.Add(T(Loc.T(IsQuote ? "Add at least one line item." : "Nothing to invoice for this project and date range."), 13, mute).WithMargin(0, 14, 0, 14));
        foreach (var l in inv.Lines.Take(7))
        {
            var r = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            r.ColumnDefinitions.Add(new ColumnDefinition()); r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            r.Children.Add(T(l.Description, 12.5, ink));
            string qty = l.Kind is "Time" or "" ? l.Hours.ToString("0.00") + " h" : l.Kind == "Custom" ? "× " + l.Hours.ToString("0.##") : "";
            var h = T(qty, 12.5, mute); h.TextAlignment = TextAlignment.Right; Grid.SetColumn(h, 1); r.Children.Add(h);
            var a = T(Fmt.Money(l.Amount, inv.Currency), 12.5, ink, FontWeights.SemiBold); a.TextAlignment = TextAlignment.Right; Grid.SetColumn(a, 2); r.Children.Add(a);
            paper.Children.Add(r);
        }
        if (inv.Lines.Count > 7) paper.Children.Add(T(Loc.N(inv.Lines.Count - 7, "+ {0} more line in the PDF", "+ {0} more lines in the PDF"), 12, mute).WithMargin(0, 4, 0, 0));

        if (inv.Lines.Count > 0)
        {
            paper.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE0)), Margin = new Thickness(0, 10, 0, 8) });
            void Tot(string k, string v, bool big = false)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(T(k, big ? 13 : 12.5, big ? ink : mute, FontWeights.SemiBold));
                var vv = T(v, big ? 18 : 12.5, big ? accent : ink, FontWeights.SemiBold); Grid.SetColumn(vv, 1); g.Children.Add(vv);
                paper.Children.Add(g);
            }
            if (inv.TotalHours > 0) Tot(Loc.T("Total hours"), inv.TotalHours.ToString("0.00"));
            if (inv.DiscountAmount != 0 || inv.TaxAmount != 0) Tot(Loc.T("Subtotal"), Fmt.Money(inv.Subtotal, inv.Currency));
            if (inv.DiscountAmount != 0) Tot(Loc.F("Discount ({0}%)", Fmt.Dec(inv.DiscountPercent)), "-" + Fmt.Money(inv.DiscountAmount, inv.Currency));
            var taxable = inv.Subtotal - inv.DiscountAmount;
            foreach (var tl in InvoiceService.TaxLines(inv).Where(x => x.Percent != 0))
                Tot($"{Loc.T(tl.Label)} ({Fmt.Dec(tl.Percent)}%)", Fmt.Money(InvoiceService.TaxFor(taxable, tl.Percent), inv.Currency));
            if (inv.ReverseCharge) Tot(Loc.T("VAT reverse charge"), Fmt.Money(0, inv.Currency));
            Tot(IsQuote ? Loc.T("Total").ToUpperInvariant() : Loc.T("TOTAL DUE"), Fmt.Money(inv.GrandTotal, inv.Currency), true);
            if (!IsQuote && !string.IsNullOrWhiteSpace(inv.PayLink))
                paper.Children.Add(T(Loc.T("PAY ONLINE") + ": " + inv.PayLink, 11, blue).WithMargin(0, 10, 0, 0));
            if (inv.IncludeTimesheet && inv.Timesheet.Count > 0)
                paper.Children.Add(T(Loc.T("TIMESHEET") + $" · {inv.Timesheet.Count}", 11, mute).WithMargin(0, 6, 0, 0));
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
        if (draft == null || draft.Invoice.Lines.Count == 0) { Dialogs.SetError(err, error ?? (IsQuote ? "Add at least one line item." : "Nothing to invoice.")); return; }
        var inv = draft.Invoice;
        if (string.IsNullOrWhiteSpace(inv.Number)) { Dialogs.SetError(err, "Give the invoice a number."); return; }
        if (State.Data.Invoices.Any(i => i.Number.Equals(inv.Number, StringComparison.OrdinalIgnoreCase) && i.Status != "Void"))
        { Dialogs.SetError(err, Loc.F("Invoice number {0} is already used.", inv.Number)); return; }

        var dlg = new SaveFileDialog
        {
            Title = IsQuote ? "Export quote PDF" : "Export invoice PDF", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = "pdf",
            FileName = InvoiceService.SafeName($"{inv.Number} {inv.ClientName}") + ".pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog() != true) return;
        try { InvoiceService.WritePdf(inv, dlg.FileName); }
        catch (Exception ex) { State.Toast(Loc.T("The invoice could not be saved: ") + ex.Message, true); return; }

        inv.FilePath = dlg.FileName;
        var d = State.Data;
        foreach (var s in draft.Sessions) s.InvoiceId = inv.Id;
        foreach (var e in draft.Expenses) e.InvoiceId = inv.Id;
        d.Invoices.Add(inv);
        if (IsQuote) d.QuoteCounter = Math.Max(d.QuoteCounter, 1) + 1;
        else { d.InvoiceCounter = Math.Max(d.InvoiceCounter, 1) + 1; d.InvoiceNumber = State.NextInvoiceNumber(); }
        State.Notify();
        State.Toast(IsQuote ? Loc.F("Quote {0} exported", inv.Number) : Loc.F("Invoice {0} exported · {1} entries marked as billed", inv.Number, draft.Sessions.Count));
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
    }

    // ---------- history ----------
    StackPanel BuildHistory()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(IsQuote ? "Quote history" : "Invoice history", 18, "TextBrush", FontWeights.SemiBold));
        sp.Children.Add(Ui.Text(IsQuote ? "Track which quotes were sent, accepted or declined." : "Track what's been sent and what's been paid.", 13, "MutedBrush").WithMargin(0, 2, 0, 12));
        var all = State.Data.Invoices.Where(i => i.Kind == Kind).OrderByDescending(i => i.IssuedUtc).ToList();
        if (all.Count == 0)
        {
            sp.Children.Add(IsQuote
                ? Ui.Empty(Icons.Doc, "No quotes yet", "Quotes you export appear here. When a client says yes, convert it to an invoice in one click.")
                : Ui.Empty(Icons.Doc, "No invoices yet", "Exported invoices appear here so you can re-download them or mark them paid."));
            return sp;
        }
        if (!IsQuote)
        {
            var outstanding = all.Where(i => i.Status == "Issued").Sum(i => i.Balance);
            if (outstanding > 0) sp.Children.Add(Ui.Text(Loc.F("Outstanding: {0}", Fmt.Money(outstanding)), 13, "WarnBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 8));
        }
        foreach (var inv in all) sp.Children.Add(HistoryRow(inv));
        return sp;
    }

    static bool LateFeeDue(Invoice inv)
    {
        var s = State.Data.Settings;
        return inv.Kind == "Invoice" && inv.Status == "Issued" && s.LateFeePercent > 0 && inv.LateFeeAmount == 0 && inv.Balance > 0
               && DateTime.UtcNow > inv.DueUtc.AddDays(s.LateFeeGraceDays);
    }

    static Border StatusPill(Invoice inv)
    {
        if (inv.Kind == "Quote")
            return inv.Status switch
            {
                "Accepted" => Ui.Pill("Accepted", "GoodBrush", "GoodSoftBrush"),
                "Declined" => Ui.Pill("Declined", "FaintBrush", "FieldBrush"),
                _ => Ui.Pill("Sent", "AccentBrush", "AccentSoftBrush"),
            };
        var overdue = inv.Status == "Issued" && inv.Balance > 0 && inv.DueUtc < DateTime.UtcNow;
        return inv.Status switch
        {
            "Paid" => Ui.Pill("Paid", "GoodBrush", "GoodSoftBrush"),
            "Void" => Ui.Pill("Void", "FaintBrush", "FieldBrush"),
            _ => overdue ? Ui.Pill("Overdue", "DangerBrush", "DangerSoftBrush")
                 : inv.AmountPaid > 0 ? Ui.Pill("Partial", "AccentBrush", "AccentSoftBrush") : Ui.Pill("Issued", "WarnBrush", "WarnSoftBrush"),
        };
    }

    Border HistoryRow(Invoice inv)
    {
        var g = new Grid();
        for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var l = new StackPanel();
        l.Children.Add(Ui.Text(inv.Number + "  ·  " + inv.ClientName, 14.5, "TextBrush", FontWeights.SemiBold));
        l.Children.Add(Ui.Text(Loc.F(inv.Kind == "Quote" ? "{0}  ·  Issued {1}  ·  Valid until {2}" : "{0}  ·  Issued {1}  ·  Due {2}",
            inv.ProjectName, Fmt.Date(State.ToLocal(inv.IssuedUtc)), Fmt.Date(State.ToLocal(inv.DueUtc))), 12.5, "MutedBrush"));
        if (inv.LateFeeAmount > 0) l.Children.Add(Ui.Text($"+ {Loc.T("Late fee")} {Fmt.Money(inv.LateFeeAmount, inv.Currency)}", 12, "DangerBrush"));
        if (inv.SquareInvoiceId.Length > 0 && inv.SquareStatus.Length > 0)
        {
            // say which Square account it lives in: always for the test account, and for live ones while the test account is connected
            var acct = inv.SquareSandbox ? " · " + Loc.T("test account") : State.Data.Settings.SquareSandbox ? " · " + Loc.T("live account") : "";
            var bad = inv.SquareStatus is "CANCELED" or "FAILED" or "REFUNDED" or "PARTIALLY_REFUNDED";
            l.Children.Add(Ui.Text(IntegrationsUi.SquareStatusText(inv.SquareStatus) + acct, 12, bad ? "WarnBrush" : "AccentBrush"));
        }
        g.Children.Add(l);

        var totals = new StackPanel { Margin = new Thickness(12, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var tt = Ui.Text(Fmt.Money(inv.GrandTotal, inv.Currency), 15, "TextBrush", FontWeights.SemiBold); tt.TextAlignment = TextAlignment.Right;
        totals.Children.Add(tt);
        if (inv.Kind == "Invoice" && inv.AmountPaid > 0 && inv.Balance > 0)
        {
            var bd = Ui.Text(Loc.T("Balance due") + " " + Fmt.Money(inv.Balance, inv.Currency), 11.5, "MutedBrush"); bd.TextAlignment = TextAlignment.Right;
            totals.Children.Add(bd);
        }
        Grid.SetColumn(totals, 1); g.Children.Add(totals);

        var pill = StatusPill(inv); pill.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(pill, 2); g.Children.Add(pill);

        var acts = new StackPanel { Orientation = Orientation.Horizontal };
        if (SafeOpen.IsAllowed(inv.FilePath, null, ".pdf") && File.Exists(inv.FilePath))
            acts.Children.Add(Ui.IconBtn(Icons.Doc, "Open PDF", () => { if (!SafeOpen.Open(inv.FilePath, null, ".pdf")) State.Toast("That file couldn't be read.", true); }));
        acts.Children.Add(Ui.IconBtn(Icons.Download, "Re-export PDF", () => ReExport(inv)));
        if (inv.Status != "Void")
            acts.Children.Add(Ui.IconBtn(Icons.Mail, inv.Kind == "Quote" ? "Email quote" : "Email invoice", () => Email(inv, false)));

        if (inv.Kind == "Invoice")
        {
            if (inv.Status == "Issued" && inv.Balance > 0 && inv.DueUtc < DateTime.UtcNow)
                acts.Children.Add(Ui.IconBtn(Icons.Bell, inv.RemindersSent > 0 ? Loc.F("Send another reminder (last sent {0})", Fmt.Date(State.ToLocal(inv.LastReminderUtc ?? DateTime.UtcNow))) : "Send payment reminder", () => Email(inv, true)));
            if (LateFeeDue(inv))
                acts.Children.Add(Ui.Btn(Loc.F("Apply late fee ({0}%)", Fmt.Dec(State.Data.Settings.LateFeePercent)), () => ApplyLateFee(inv), "GhostButton").WithMargin(0, 0, 4, 0));
            if (inv.Status == "Issued" && SecretStore.Has(State.Dir, IntegrationClient.Square))
            {
                var draftOnly = inv.SquareInvoiceId.Length == 0 || inv.SquareStatus == "DRAFT";
                if (inv.SquareInvoiceId.Length > 0 && inv.SquareSandbox != State.Data.Settings.SquareSandbox)
                {
                    const string other = "This invoice is linked to your other Square account (test or live). Connect that account again in Settings → Integrations to use it.";
                    acts.Children.Add(Ui.IconBtn(Icons.Sliders, other, () => State.Toast(Loc.T(other), true)));   // points to Settings, not "send"
                }
                else if (draftOnly && inv.AmountPaid == 0)
                {
                    var send = Ui.IconBtn(Icons.Upload, inv.SquareStatus == "DRAFT" ? "Review and send the Square draft" : "Send with Square", () => IntegrationsUi.SendWithSquare(inv, Build));
                    send.IsEnabled = !IntegrationClient.SquareBusy(inv);
                    acts.Children.Add(send);
                }
                else if (!draftOnly)
                    acts.Children.Add(Ui.IconBtn(Icons.Link, "Open Square's payment page", () => IntegrationsUi.OpenInSquare(inv)));
            }
            if (inv.Status == "Issued")
            {
                acts.Children.Add(Ui.IconBtn(Icons.Dollar, "Record payment", () => RecordPayment(inv)));
                acts.Children.Add(Ui.IconBtn(Icons.Check, "Mark as paid", () => MarkPaid(inv)));
            }
            else if (inv.Status == "Paid")
                acts.Children.Add(Ui.IconBtn(Icons.Edit, "Mark as unpaid", () => MarkUnpaid(inv)));
            if (inv.Status != "Void")
                acts.Children.Add(Ui.IconBtn(Icons.Ban, "Void invoice and release its entries", () =>
                {
                    var vmsg = Loc.F("Void {0}? Its {1} time entries become unbilled again so you can re-invoice them.", inv.Number, inv.SessionIds.Count);
                    var cancelSquare = CanCancelInSquare(inv);
                    if (cancelSquare) vmsg += "\n\n" + Loc.T("Tempo will cancel it in Square too, so your client can't pay it there.");
                    else if (IntegrationsUi.OpenInSquareAccount(inv)) vmsg += "\n\n" + Loc.T("It stays open in Square. Cancel it in your Square dashboard too, so your client isn't asked to pay.");
                    if (!Dialogs.Confirm("Void invoice", vmsg, "Void invoice", true)) return;
                    inv.Status = "Void";
                    Release(inv);
                    State.Notify(); State.Toast(Loc.F("{0} voided", inv.Number));
                    if (cancelSquare) IntegrationsUi.CancelInSquare(inv, Build);
                }, true));
        }
        else
        {
            if (inv.Status == "Sent")
            {
                acts.Children.Add(Ui.IconBtn(Icons.Check, "Mark accepted", () => { inv.Status = "Accepted"; State.Notify(); }));
                acts.Children.Add(Ui.IconBtn(Icons.Ban, "Mark declined", () => { inv.Status = "Declined"; State.Notify(); }, true));
            }
            if (inv.Status != "Declined" && !State.Data.Invoices.Any(x => x.ConvertedFrom == inv.Id))
                acts.Children.Add(Ui.Btn("Convert to invoice", () => ConvertQuote(inv), "GlassButton", Icons.Doc).WithMargin(0, 0, 4, 0));
        }
        acts.Children.Add(Ui.IconBtn(Icons.Trash, "Delete invoice", () => Delete(inv), true));
        Grid.SetColumn(acts, 3); g.Children.Add(acts);

        var b = new Border { Padding = new Thickness(12, 10, 8, 10), CornerRadius = new CornerRadius(12), Child = g, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }

    static void Release(Invoice inv)
    {
        foreach (var s in State.Data.Sessions.Where(s => s.InvoiceId == inv.Id)) s.InvoiceId = null;
        foreach (var e in State.Data.Expenses.Where(e => e.InvoiceId == inv.Id)) e.InvoiceId = null;
    }

    static void Delete(Invoice inv)
    {
        var held = State.Data.Sessions.Count(x => x.InvoiceId == inv.Id);
        var msg = Loc.F("Delete {0} ({1})?", inv.Number, Fmt.Money(inv.GrandTotal, inv.Currency));
        if (inv.Status == "Paid") msg += "\n\n" + Loc.T("It is marked as paid; deleting removes it from your records.");
        if (held > 0) msg += "\n\n" + Loc.N(held, "Its {0} time entry becomes unbilled again.", "Its {0} time entries become unbilled again.");
        var cancelSquare = CanCancelInSquare(inv);
        if (cancelSquare) msg += "\n\n" + Loc.T("Tempo will cancel it in Square too, so your client can't pay it there.");
        else if (IntegrationsUi.OpenInSquareAccount(inv)) msg += "\n\n" + Loc.T("It stays in Square. Cancel or delete it in your Square dashboard too.");
        msg += "\n\n" + Loc.T("You can restore it from Settings → Recently deleted. The PDF file you exported is not touched.");
        if (!Dialogs.Confirm("Delete invoice", msg, "Delete invoice", true)) return;
        var t = TrashBin.Add("Invoice", inv, inv.Number + " · " + inv.ClientName);
        Release(inv);
        State.Data.Invoices.Remove(inv);
        if (inv.Kind == "Invoice") State.Data.InvoiceNumber = State.NextInvoiceNumber();
        State.Notify();
        State.ToastUndo(Loc.F("{0} deleted", inv.Number), t);
        if (cancelSquare) IntegrationsUi.CancelInSquare(inv, () => { }, quietOnSuccess: true);   // keep the undo toast unless it failed
    }

    /// <summary>Square is connected (same test/live account) and the invoice is still payable there.</summary>
    static bool CanCancelInSquare(Invoice inv) =>
        IntegrationsUi.OpenInSquareAccount(inv) && SecretStore.Has(State.Dir, IntegrationClient.Square) && inv.SquareSandbox == State.Data.Settings.SquareSandbox;

    static void MarkPaid(Invoice inv)
    {
        if (inv.Balance > 0) inv.Payments.Add(new Payment { DateUtc = DateTime.UtcNow, Amount = inv.Balance });
        inv.Status = "Paid"; inv.PaidUtc = DateTime.UtcNow;
        State.Notify();
        State.Toast(IntegrationsUi.OpenInSquareAccount(inv)
            ? Loc.F("{0} marked as paid. It's still open in Square: cancel it there if your client paid another way.", inv.Number)
            : Loc.F("{0} marked as paid", inv.Number));
    }

    static void MarkUnpaid(Invoice inv)
    {
        inv.Payments.Clear(); inv.Status = "Issued"; inv.PaidUtc = null;
        State.Notify();
    }

    static void ApplyLateFee(Invoice inv)
    {
        var pct = State.Data.Settings.LateFeePercent;
        inv.LateFeeAmount = Math.Round(inv.Balance * pct / 100m, 2);
        State.Notify();
        State.Toast(Loc.F("Late fee of {0} added to {1}", Fmt.Money(inv.LateFeeAmount, inv.Currency), inv.Number));
    }

    static void RecordPayment(Invoice inv)
    {
        var amount = Ui.Input(Fmt.Dec(inv.Balance), "0.00");
        var date = Ui.Input(State.TodayLocal.ToString("yyyy-MM-dd"), "yyyy-mm-dd");
        var note = Ui.Input("", "");
        var err = Dialogs.ErrorLabel();
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        void Fill()
        {
            list.Children.Clear();
            if (inv.Payments.Count == 0) return;
            list.Children.Add(Ui.Text("Payments", 12, "MutedBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 6));
            foreach (var p in inv.Payments.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Ui.Text($"{Fmt.Date(State.ToLocal(p.DateUtc))}  ·  {Fmt.Money(p.Amount, inv.Currency)}{(string.IsNullOrWhiteSpace(p.Note) ? "" : "  ·  " + p.Note)}", 13, "TextBrush"));
                var rm = Ui.IconBtn(Icons.Trash, "Remove payment", () =>
                {
                    inv.Payments.Remove(p);
                    if (inv.Status == "Paid" && inv.Balance > 0) { inv.Status = "Issued"; inv.PaidUtc = null; }
                    State.Notify(); Fill();
                }, true);
                Grid.SetColumn(rm, 1); row.Children.Add(rm);
                list.Children.Add(row);
            }
        }
        Fill();

        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition()); two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); two.ColumnDefinitions.Add(new ColumnDefinition());
        var f1 = Ui.Field(Loc.T("AMOUNT") + " (" + inv.Currency + ")", amount); var f2 = Ui.Field("PAYMENT DATE", date); Grid.SetColumn(f2, 2);
        two.Children.Add(f1); two.Children.Add(f2);

        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.T("Balance due") + ": " + Fmt.Money(inv.Balance, inv.Currency), 14, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 12));
        body.Children.Add(list);
        body.Children.Add(two);
        body.Children.Add(Ui.Field("NOTE (OPTIONAL)", note));
        body.Children.Add(err);

        Dialogs.Show("Record payment", body, "Record payment", () =>
        {
            if (!Ui.TryDec(amount.Text, out var a) || a <= 0) { Dialogs.SetError(err, "Enter an amount greater than zero."); return false; }
            if (!Ui.TryDate(date.Text, out var dt)) { Dialogs.SetError(err, "Date should look like 2026-11-30."); return false; }
            inv.Payments.Add(new Payment { DateUtc = State.ToUtc(dt.Date.AddHours(12)), Amount = Math.Round(a, 2), Note = note.Text.Trim() });
            if (inv.Balance <= 0) { inv.Status = "Paid"; inv.PaidUtc = State.ToUtc(dt.Date.AddHours(12)); }
            State.Notify(); State.Toast("Payment recorded");
            return true;
        }, 480);
    }

    static void ConvertQuote(Invoice quote)
    {
        var d = State.Data; var s = d.Settings;
        var now = DateTime.UtcNow;
        var inv = new Invoice
        {
            Kind = "Invoice", Language = quote.Language, Number = d.InvoiceNumber, ProjectId = quote.ProjectId, ProjectName = quote.ProjectName, ClientId = quote.ClientId,
            ClientName = quote.ClientName, ClientEmail = quote.ClientEmail, ClientAddress = quote.ClientAddress, PoNumber = quote.PoNumber,
            IssuedUtc = now, DueUtc = now.AddDays(s.PaymentTermsDays), PeriodFrom = State.TodayLocal, PeriodTo = State.TodayLocal, TimeZoneId = State.Tz.Id, Currency = quote.Currency,
            Lines = quote.Lines.Select(l => new InvoiceLine { Description = l.Description, Hours = l.Hours, Rate = l.Rate, Amount = l.Amount, Kind = l.Kind }).ToList(),
            DiscountPercent = quote.DiscountPercent, TaxLabel = quote.TaxLabel, TaxPercent = quote.TaxPercent, Note = quote.Note, PaymentInstructions = d.Billing.PaymentInstructions,
            StudioName = quote.StudioName, StudioEmail = quote.StudioEmail, StudioAddress = quote.StudioAddress, TaxId = quote.TaxId, LogoPath = quote.LogoPath,
            Template = quote.Template, PayLink = SafeOpen.IsPayLink(s.DefaultPayLink) ? s.DefaultPayLink : "", ConvertedFrom = quote.Id
        };
        InvoiceService.ApplyTotals(inv);
        d.Invoices.Add(inv);
        d.InvoiceCounter = Math.Max(d.InvoiceCounter, 1) + 1;
        d.InvoiceNumber = State.NextInvoiceNumber();
        quote.Status = "Accepted";
        tab = "Invoices";
        State.Notify();
        State.Toast(Loc.F("Invoice {0} created from quote {1}", inv.Number, quote.Number));
    }

    /// <summary>Opens a ready-to-send email in the user's mail app with the PDF attached (they press Send).</summary>
    static void Email(Invoice inv, bool reminder)
    {
        // make sure there is an up-to-date PDF to attach
        // always attach a fresh copy written by Tempo into its own folder, never whatever path the record points at
        var dir = Path.Combine(State.Dir, "invoices");
        Directory.CreateDirectory(dir);
        var pdf = Path.Combine(dir, InvoiceService.SafeName($"{inv.Number} {inv.ClientName}") + ".pdf");
        try { InvoiceService.WritePdf(inv, pdf); }
        catch (Exception ex) { State.Toast(Loc.T("Couldn't create the PDF: ") + ex.Message, true); return; }
        if (string.IsNullOrWhiteSpace(inv.ClientEmail)) State.Toast("This client has no email address yet. Add it on the Clients page.", true);
        var (to, subject, body) = InvoiceEmail.Compose(inv, reminder, DateTime.UtcNow);
        if (!Mailer.Compose(to, subject, body, pdf))
        {
            Mailer.Fallback(to, subject, body, pdf);
            State.Toast("Your mail app didn't accept an attachment, so the PDF is shown in its folder. Drag it into the email.");
        }
        if (reminder) { inv.RemindersSent++; inv.LastReminderUtc = DateTime.UtcNow; }
        State.Notify();
    }

    static void ReExport(Invoice inv)
    {
        var dlg = new SaveFileDialog { Title = "Export invoice PDF", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = "pdf", FileName = InvoiceService.SafeName($"{inv.Number} {inv.ClientName}") + ".pdf" };
        if (dlg.ShowDialog() != true) return;
        try { InvoiceService.WritePdf(inv, dlg.FileName); inv.FilePath = dlg.FileName; State.Notify(); State.Toast("Invoice PDF saved"); }
        catch (Exception ex) { State.Toast(Loc.T("The invoice could not be saved: ") + ex.Message, true); }
    }

    // ---------- recurring ----------
    void BuildRecurring(StackPanel root)
    {
        var d = State.Data;
        var card = new StackPanel();
        card.Children.Add(Ui.Text("Recurring invoices", 18, "TextBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 10));
        if (d.Recurring.Count == 0)
            card.Children.Add(Ui.Empty(Icons.Calendar, "No recurring invoices yet", "Retainers and repeat work: Tempo creates each invoice on its due date.",
                Ui.Btn("New recurring invoice", () => EditRecurring(null), "PrimaryButton", Icons.Plus)));
        foreach (var r in d.Recurring.OrderBy(x => x.NextRun))
        {
            var tmp = new Invoice { Lines = r.Lines, TaxPercent = r.TaxPercent }; InvoiceService.ApplyTotals(tmp);
            var p = State.Proj(r.ProjectId);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new StackPanel();
            l.Children.Add(Ui.Text(r.Name, 14.5, "TextBrush", FontWeights.SemiBold));
            l.Children.Add(Ui.Text($"{Loc.T(r.Frequency)}  ·  {p?.Name ?? Loc.T("Deleted project")}  ·  {Loc.F("Next: {0}", Fmt.Date(r.NextRun))}", 12.5, "MutedBrush"));
            g.Children.Add(l);
            var amt = Ui.Text(Fmt.Money(tmp.Total), 15, "TextBrush", FontWeights.SemiBold); amt.Margin = new Thickness(12, 0, 14, 0); amt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(amt, 1); g.Children.Add(amt);
            var pill = r.Active ? Ui.Pill("Active", "GoodBrush", "GoodSoftBrush") : Ui.Pill("Paused", "FaintBrush", "FieldBrush"); pill.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(pill, 2); g.Children.Add(pill);
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            var rr = r;
            acts.Children.Add(Ui.Btn("Run now", () => { var inv = RecurringService.Create(rr, State.TodayLocal); rr.NextRun = RecurringService.Advance(rr.NextRun.Date <= State.TodayLocal ? rr.NextRun : State.TodayLocal, rr.Frequency); State.Notify(); State.Toast(Loc.F("Created {0}", inv.Number)); }, "GhostButton"));
            acts.Children.Add(Ui.IconBtn(Icons.Edit, "Edit recurring invoice", () => EditRecurring(rr)));
            acts.Children.Add(Ui.IconBtn(Icons.Trash, "Delete", () => { State.Data.Recurring.Remove(rr); State.Notify(); }, true));
            Grid.SetColumn(acts, 3); g.Children.Add(acts);
            var b = new Border { Padding = new Thickness(12, 10, 8, 10), CornerRadius = new CornerRadius(12), Child = g, Background = Brushes.Transparent };
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            card.Children.Add(b);
        }
        root.Children.Add(Ui.Card(card));
    }

    static void EditRecurring(RecurringInvoice? editing)
    {
        var d = State.Data; var s = d.Settings;
        var projects = State.ActiveProjects.ToList();
        if (projects.Count == 0) { State.Toast("Create a project first", true); return; }
        var name = Ui.Input(editing?.Name ?? "", "e.g. Monthly retainer");
        var pc = Ui.Combo(projects, State.Proj(editing?.ProjectId) ?? projects[0], "Label");
        var freq = Ui.Combo(new[] { "Weekly", "Monthly", "Quarterly" }, editing?.Frequency ?? "Monthly");
        var next = Ui.Input((editing?.NextRun ?? State.TodayLocal.AddMonths(1)).ToString("yyyy-MM-dd"), "yyyy-mm-dd");
        var due = Ui.Input((editing?.DueDays ?? s.PaymentTermsDays).ToString(), "14");
        var taxL = Ui.Input(editing?.TaxLabel ?? s.TaxLabel, "Tax");
        var taxP = Ui.Input(Fmt.Dec(editing?.TaxPercent ?? s.DefaultTaxPercent), "0");
        var po = Ui.Input(editing?.PoNumber ?? "", "Optional");
        var note = Ui.Input(editing?.Note ?? d.Billing.Note, "Thank you for your business.");
        var active = Ui.Check("Active", editing?.Active ?? true);
        var lines = editing != null ? editing.Lines.Select(l => new InvoiceLine { Description = l.Description, Hours = l.Hours, Rate = l.Rate, Amount = l.Amount, Kind = l.Kind }).ToList() : new List<InvoiceLine>();
        if (lines.Count == 0 && pc.SelectedItem is Project p0 && p0.BudgetType == "Retainer")
            lines.Add(new InvoiceLine { Description = $"{p0.Name} — {Loc.T("Retainer")}", Hours = 1, Rate = p0.BudgetValue, Amount = p0.BudgetValue, Kind = "Custom" });
        var err = Dialogs.ErrorLabel();

        Grid Two(FrameworkElement a, FrameworkElement b)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(b, 2); g.Children.Add(a); g.Children.Add(b);
            return g;
        }
        var body = new StackPanel();
        body.Children.Add(Ui.Field("NAME", name));
        body.Children.Add(Ui.Field("PROJECT", pc));
        body.Children.Add(Two(Ui.Field("FREQUENCY", freq), Ui.Field("NEXT INVOICE DATE", next)));
        body.Children.Add(Two(Ui.Field("DUE IN (DAYS)", due), Ui.Field("PO NUMBER", po)));
        body.Children.Add(Two(Ui.Field("TAX LABEL", taxL), Ui.Field("RATE %", taxP)));
        body.Children.Add(Ui.Text("LINE ITEMS", 12, "MutedBrush", FontWeights.SemiBold).WithMargin(0, 0, 0, 6));
        body.Children.Add(LinesEditor.Create(lines, () => { }).WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("NOTE TO CLIENT", note));
        body.Children.Add(active.WithMargin(0, 0, 0, 12));
        body.Children.Add(err);

        Dialogs.Show(editing == null ? "New recurring invoice" : "Edit recurring invoice", body, "Save changes", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { Dialogs.SetError(err, "Add a project name."); return false; }
            if (!Ui.TryDate(next.Text, out var nd)) { Dialogs.SetError(err, "Date should look like 2026-11-30."); return false; }
            if (!Ui.TryDec(taxP.Text.Length == 0 ? "0" : taxP.Text, out var tp) || tp is < 0 or > 100) { Dialogs.SetError(err, "Tax % should be between 0 and 100."); return false; }
            if (!int.TryParse(due.Text.Length == 0 ? "0" : due.Text, out var dd) || dd is < 0 or > 365) { Dialogs.SetError(err, "Due days should be a number from 0 to 365."); return false; }
            var real = lines.Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.Amount != 0).ToList();
            if (real.Count == 0) { Dialogs.SetError(err, "Add at least one line item."); return false; }
            var r = editing ?? new RecurringInvoice();
            r.Name = name.Text.Trim(); r.ProjectId = ((Project)pc.SelectedItem!).Id; r.Frequency = (string)freq.SelectedItem!; r.NextRun = nd.Date;
            r.DueDays = dd; r.TaxLabel = string.IsNullOrWhiteSpace(taxL.Text) ? "Tax" : taxL.Text.Trim(); r.TaxPercent = tp; r.PoNumber = po.Text.Trim(); r.Note = note.Text.Trim();
            r.Active = active.IsChecked == true; r.Lines = real;
            if (editing == null) State.Data.Recurring.Add(r);
            State.Notify(); State.Toast("Recurring invoice saved");
            return true;
        }, 560);
    }
}
