using System.IO;
using System.Windows.Media;

namespace Tempo;

public static class InvoiceService
{
    public static (List<InvoiceLine> Lines, List<Session> Sessions) BuildLines(Project p, DateTime fromLocal, DateTime toLocal, string grouping, int roundMinutes)
    {
        var s0 = State.ToUtc(fromLocal.Date);
        var s1 = State.ToUtc(toLocal.Date.AddDays(1));
        var ss = State.Data.Sessions
            .Where(s => s.ProjectId == p.Id && s.InvoiceId == null && s.Billable && s.StartedAtUtc >= s0 && s.StartedAtUtc < s1)
            .OrderBy(s => s.StartedAtUtc).ToList();

        string Key(Session s) => grouping switch
        {
            "Day" => State.ToLocal(s.StartedAtUtc).ToString("yyyy-MM-dd") + "|" + s.HourlyRate,
            "Entry" => s.Id,
            _ => (string.IsNullOrWhiteSpace(s.Task) ? "Work session" : s.Task.Trim()) + "|" + s.HourlyRate,
        };

        var lines = new List<InvoiceLine>();
        foreach (var g in ss.GroupBy(Key))
        {
            var first = g.First();
            var ms = g.Sum(s => s.DurationMilliseconds);
            double minutes = ms / 60000.0;
            if (roundMinutes > 0) minutes = Math.Ceiling(minutes / roundMinutes) * roundMinutes;
            var hours = Math.Max(0.01m, Math.Round((decimal)minutes / 60m, 2));
            var local = State.ToLocal(first.StartedAtUtc);
            string desc = grouping switch
            {
                "Day" => Fmt.Date(local) + " — " + string.Join(", ", g.Select(s => string.IsNullOrWhiteSpace(s.Task) ? "Work session" : s.Task.Trim()).Distinct().Take(3)),
                "Entry" => local.ToString("MMM d") + " — " + (string.IsNullOrWhiteSpace(first.Task) ? "Work session" : first.Task.Trim()),
                _ => string.IsNullOrWhiteSpace(first.Task) ? "Work session" : first.Task.Trim(),
            };
            lines.Add(new InvoiceLine { Description = desc, Hours = hours, Rate = first.HourlyRate, Amount = Math.Round(hours * first.HourlyRate, 2), Kind = "Time" });
        }
        return (lines, ss);
    }

    /// <summary>Billable, not-yet-invoiced expenses for a project in a date range, as invoice lines.</summary>
    public static (List<InvoiceLine> Lines, List<Expense> Expenses) ExpenseLines(Project p, DateTime fromLocal, DateTime toLocal)
    {
        var f = fromLocal.Date; var t = toLocal.Date;
        var es = State.Data.Expenses
            .Where(e => e.ProjectId == p.Id && e.InvoiceId == null && e.Billable && e.Date.Date >= f && e.Date.Date <= t)
            .OrderBy(e => e.Date).ToList();
        var lines = es.Select(e => new InvoiceLine
        {
            Description = Loc.F("Expense: {0}", string.IsNullOrWhiteSpace(e.Description) ? Loc.T("Expense") : e.Description) + " (" + Fmt.Date(e.Date) + ")",
            Amount = e.Amount, Kind = "Expense"
        }).ToList();
        return (lines, es);
    }

    /// <summary>A short list of the tracked time behind an invoice, attached as its last page.</summary>
    public static List<InvoiceLine> TimesheetLines(IEnumerable<Session> sessions) =>
        sessions.OrderBy(s => s.StartedAtUtc).Select(s => new InvoiceLine
        {
            Description = Fmt.Date(State.ToLocal(s.StartedAtUtc)) + "   " + (string.IsNullOrWhiteSpace(s.Task) ? Loc.T("Work session") : s.Task.Trim()),
            Hours = Math.Round(s.DurationMilliseconds / 3600000m, 2), Kind = "Time"
        }).ToList();

    public static void ApplyTotals(Invoice inv)
    {
        inv.TotalHours = inv.Lines.Where(l => l.Kind is "Time" or "").Sum(l => l.Hours);
        inv.Subtotal = inv.Lines.Sum(l => l.Amount);
        inv.DiscountAmount = Math.Round(inv.Subtotal * inv.DiscountPercent / 100m, 2);
        var taxable = inv.Subtotal - inv.DiscountAmount;
        if (inv.ReverseCharge) inv.TaxAmount = 0m;
        else if (inv.Taxes.Count > 0)
        {
            inv.TaxPercent = inv.Taxes.Sum(t => t.Percent);   // kept for older views that show one rate
            inv.TaxAmount = inv.Taxes.Sum(t => TaxFor(taxable, t.Percent));
        }
        else inv.TaxAmount = TaxFor(taxable, inv.TaxPercent);
        inv.Total = taxable + inv.TaxAmount;
    }

    public static decimal TaxFor(decimal taxable, decimal percent) => Math.Round(taxable * percent / 100m, 2);

    /// <summary>The tax lines an invoice shows: its own list, or the single legacy tax.</summary>
    public static List<TaxLine> TaxLines(Invoice inv) =>
        inv.ReverseCharge ? new() : inv.Taxes.Count > 0 ? inv.Taxes : inv.TaxPercent != 0 ? new() { new TaxLine { Label = inv.TaxLabel, Percent = inv.TaxPercent } } : new();

    public static string NextQuoteNumber()
    {
        var n = Math.Max(1, State.Data.QuoteCounter);
        string Make(int i) => $"QUO-{DateTime.Now.Year}-{i:000}";
        while (State.Data.Invoices.Any(i => i.Number == Make(n))) n++;
        return Make(n);
    }

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Trim();
    }

    // ---------------- PDF (Tempo's own writer, see Pdf.cs) ----------------
    static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    static readonly Color Ink = Hex("#1E2B34"), Muted = Hex("#5E6A74"), Faint = Hex("#8A949C"), Rule = Hex("#DDE2E0"),
                          RowRule = Hex("#E6EAE8"), HeadRule = Hex("#BFC7C4"), Blue = Hex("#0068F0"), Green = Hex("#008A5A"), White = Hex("#FFFFFF");

    public static void WritePdf(Invoice inv, string path)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(inv.TimeZoneId); } catch { tz = TimeZoneInfo.Local; }
        DateTime L(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        string D(DateTime d) => Fmt.Date(d, inv.Language);
        string Tr(string s) => Loc.For(inv.Language, s);
        string Mo(decimal v) => Fmt.Money(v, inv.Currency);

        bool quote = inv.Kind == "Quote";
        bool bold = inv.Template == "Bold", minimal = inv.Template == "Minimal";
        Color accent = minimal ? Ink : Blue;

        var w = new PdfWriter();
        bool letter = Fmt.UsLetter(inv.Currency);
        double PW = letter ? 612 : 595.28, PH = letter ? 792 : 841.89, M = 44, cw = PW - 2 * M, limit = PH - M - 24;
        var page = w.AddPage(PW, PH);
        double y = M;

        PdfWriter.Img? logo = !string.IsNullOrEmpty(inv.LogoPath) && File.Exists(inv.LogoPath) ? w.LoadImage(inv.LogoPath) : null;

        double Para(PdfWriter.Page pg, double x, double top, double width, string text, double size, bool b, Color c)
        {
            foreach (var line in w.Wrap(text, width, size, b)) { pg.Text(x, top, line, size, b, c); top += size * 1.35; }
            return top;
        }

        // ---- header ----
        Color nameColor = bold ? White : Ink, subColor = bold ? Hex("#D6E6FF") : Muted, titleColor = bold ? White : accent;
        if (bold) { page.Rect(0, 0, PW, 108, Blue); y = 28; }
        double leftX = M;
        if (logo != null)
        {
            double k = Math.Min(54.0 / logo.Width, 54.0 / logo.Height);
            page.Image(logo, M, y, logo.Width * k, logo.Height * k);
            leftX += 66;
        }
        var name = string.IsNullOrWhiteSpace(inv.StudioName) ? Tr("Your studio") : inv.StudioName;
        double ny = Para(page, leftX, y, cw * 0.55 - (leftX - M), name, 17, true, nameColor);
        if (!string.IsNullOrWhiteSpace(inv.StudioEmail)) ny = Para(page, leftX, ny + 1, cw * 0.55, inv.StudioEmail, 10, false, subColor);
        double right = M + cw;
        page.TextRight(right, y - 2, Tr(quote ? "QUOTE" : "INVOICE"), minimal ? 22 : 26, true, titleColor);
        page.TextRight(right, y + 34, inv.Number, 12, true, bold ? White : Ink);
        string? stamp = null; Color stampColor = Green;
        if (quote) { if (inv.Status == "Accepted") stamp = Tr("Accepted").ToUpperInvariant(); else if (inv.Status == "Declined") { stamp = Tr("Declined").ToUpperInvariant(); stampColor = Faint; } }
        else if (inv.Status == "Paid") stamp = Tr("PAID") + (inv.PaidUtc is DateTime pd ? "  " + D(L(pd)) : "");
        else if (inv.Status == "Void") { stamp = Tr("VOID"); stampColor = Faint; }
        if (stamp != null) page.TextRight(right, y + 52, stamp, 10, true, bold ? White : stampColor);
        if (bold) y = 108 + 26;
        else
        {
            y = Math.Max(ny, y + 70) + 12;
            if (!minimal) page.Line(M, y, M + cw, y, 0.8, Rule);
            y += minimal ? 6 : 18;
        }

        // ---- parties + details ----
        double detW = 225, colW = (cw - detW - 24) / 2;
        double yb = y, yf = y, yd = y;
        page.Text(M, yb, Tr("BILL TO"), 8, true, Faint); yb += 13;
        yb = Para(page, M, yb, colW, inv.ClientName, 11, true, Ink);
        if (!string.IsNullOrWhiteSpace(inv.ProjectName)) yb = Para(page, M, yb, colW, inv.ProjectName, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.ClientEmail)) yb = Para(page, M, yb, colW, inv.ClientEmail, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.ClientAddress)) yb = Para(page, M, yb, colW, inv.ClientAddress, 10, false, Muted);
        double fx = M + colW + 12;
        page.Text(fx, yf, Tr("FROM"), 8, true, Faint); yf += 13;
        if (!string.IsNullOrWhiteSpace(inv.StudioAddress)) yf = Para(page, fx, yf, colW, inv.StudioAddress, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.TaxId)) yf = Para(page, fx, yf, colW, Tr("Tax ID: ") + inv.TaxId, 10, false, Muted);
        double dx = M + cw - detW;
        void Kv(string k, string v) { page.Text(dx, yd, k, 10, false, Faint); page.TextRight(dx + detW, yd, v, 10, true, Ink); yd += 14; }
        Kv(Tr("Issued date"), D(L(inv.IssuedUtc)));
        Kv(Tr(quote ? "Valid until" : "Due date"), D(L(inv.DueUtc)));
        if (!quote && inv.Lines.Any(l => l.Kind is "Time" or "")) Kv(Tr("Period"), D(inv.PeriodFrom) + " – " + D(inv.PeriodTo));
        if (!string.IsNullOrWhiteSpace(inv.PoNumber)) Kv(Tr("PO number"), inv.PoNumber);
        Kv(Tr("Currency"), inv.Currency);
        y = Math.Max(yb, Math.Max(yf, yd)) + 24;

        // ---- line items ----
        double c0 = cw * 6 / 10.4, c1 = cw * 1.2 / 10.4, c2 = cw * 1.5 / 10.4, c3 = cw * 1.7 / 10.4;
        double x1 = M + c0 + c1, x2 = x1 + c2, x3 = x2 + c3;
        void TableHeader()
        {
            page.Text(M, y, Tr("DESCRIPTION"), 8, true, Faint);
            page.TextRight(M + c0 + c1, y, Tr("HOURS"), 8, true, Faint);
            page.TextRight(x2, y, Tr("RATE"), 8, true, Faint);
            page.TextRight(x3, y, Tr("AMOUNT"), 8, true, Faint);
            y += 16;
            page.Line(M, y, M + cw, y, 1, HeadRule);
        }
        TableHeader();
        foreach (var l in inv.Lines)
        {
            var lines = w.Wrap(l.Description, c0 - 14, 10, false);
            double rowH = lines.Count * 13.5 + 14;
            if (y + rowH > limit) { page = w.AddPage(PW, PH); y = M; TableHeader(); }
            double ty = y + 7;
            foreach (var ln in lines) { page.Text(M, ty, ln, 10, false, Ink); ty += 13.5; }
            bool time = l.Kind is "Time" or "";
            bool custom = l.Kind == "Custom";
            if (time) page.TextRight(M + c0 + c1, y + 7, l.Hours.ToString("0.00"), 10, false, Ink);
            else if (custom) page.TextRight(M + c0 + c1, y + 7, l.Hours.ToString("0.##"), 10, false, Ink);
            if (time || custom) page.TextRight(x2, y + 7, Mo(l.Rate), 10, false, Ink);
            page.TextRight(x3, y + 7, Mo(l.Amount), 10, true, Ink);
            y += rowH;
            if (!minimal) page.Line(M, y, M + cw, y, 0.6, RowRule);
        }

        // ---- totals ----
        var rows = new List<(string K, string V, bool Big)>();
        if (inv.TotalHours > 0) rows.Add((Tr("Total hours"), inv.TotalHours.ToString("0.00"), false));
        rows.Add((Tr("Subtotal"), Mo(inv.Subtotal), false));
        if (inv.DiscountAmount != 0) rows.Add((Loc.FFor(inv.Language, "Discount ({0}%)", Fmt.Dec(inv.DiscountPercent)), "-" + Mo(inv.DiscountAmount), false));
        var taxableAmt = inv.Subtotal - inv.DiscountAmount;
        foreach (var tl in TaxLines(inv))
            rows.Add(($"{Tr(tl.Label)} ({Fmt.Dec(tl.Percent)}%)", Mo(TaxFor(taxableAmt, tl.Percent)), false));
        if (inv.ReverseCharge) rows.Add((Tr("VAT reverse charge"), Mo(0), false));
        if (inv.LateFeeAmount != 0) rows.Add((Tr("Late fee"), Mo(inv.LateFeeAmount), false));
        if (quote) rows.Add((Tr("Total").ToUpperInvariant(), Mo(inv.GrandTotal), true));
        else if (inv.Status == "Paid") rows.Add((Tr("TOTAL PAID"), Mo(inv.GrandTotal), true));
        else if (inv.AmountPaid > 0)
        {
            rows.Add((Tr("TOTAL DUE"), Mo(inv.GrandTotal), false));
            rows.Add((Tr("Paid to date"), "-" + Mo(inv.AmountPaid), false));
            rows.Add((Tr("BALANCE DUE"), Mo(inv.Balance), true));
        }
        else rows.Add((Tr("TOTAL DUE"), Mo(inv.GrandTotal), true));
        double totH = rows.Count * 18 + 30;
        y += 18;
        if (y + totH > limit) { page = w.AddPage(PW, PH); y = M; }
        double tw = 250, tx = M + cw - tw;
        foreach (var (k, v, big) in rows)
        {
            if (big) { page.Line(tx, y + 2, tx + tw, y + 2, 0.8, Rule); y += 10; }
            page.Text(tx, y + (big ? 3 : 0), k, big ? 12 : 10, true, big ? Ink : Muted);
            page.TextRight(tx + tw, y, v, big ? 15 : 10, true, big ? accent : Ink);
            y += big ? 26 : 18;
        }

        // ---- reverse-charge statement (required wording on EU B2B invoices) ----
        if (inv.ReverseCharge)
        {
            y += 6;
            var rc = Tr("Reverse charge: VAT to be accounted for by the customer.") + (string.IsNullOrWhiteSpace(inv.ClientTaxId) ? "" : "  " + Tr("Customer VAT ID: ") + inv.ClientTaxId);
            foreach (var ln in w.Wrap(rc, cw, 9.5, true))
            {
                if (y + 14 > limit) { page = w.AddPage(PW, PH); y = M; }
                page.Text(M, y, ln, 9.5, true, Muted); y += 13;
            }
        }

        // ---- payment details, pay link + QR, note ----
        y += 14;
        bool hasPay = !quote && !string.IsNullOrWhiteSpace(inv.PayLink);
        double qrSize = 84;
        if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions) || hasPay)
        {
            var pl = w.Wrap(inv.PaymentInstructions ?? "", cw - (hasPay ? qrSize + 20 : 0), 10, false);
            double need = 20 + pl.Count * 13.5 + (hasPay ? Math.Max(qrSize, 52) : 0);
            if (y + need > limit) { page = w.AddPage(PW, PH); y = M; }
            double top = y;
            double textW = cw - (hasPay ? qrSize + 20 : 0);
            if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions))
            {
                page.Text(M, y, Tr("PAYMENT DETAILS"), 8, true, Faint); y += 13;
                y = Para(page, M, y, textW, inv.PaymentInstructions, 10, false, Muted) + 8;
            }
            if (hasPay)
            {
                page.Text(M, y, Tr("PAY ONLINE"), 8, true, Faint); y += 13;
                var ll = w.Wrap(inv.PayLink, textW, 10, true);
                double ly = y;
                foreach (var ln in ll) { page.Text(M, y, ln, 10, true, Blue); y += 13.5; }
                page.Link(M, ly, Math.Min(textW, w.Measure(ll[0], 10, true)), Math.Max(13.5, y - ly), inv.PayLink);
                var qr = QrCode.Encode(inv.PayLink);
                if (qr != null)
                {
                    int n = qr.GetLength(0);
                    double cell = qrSize / n, qx = M + cw - qrSize, qy = top;
                    for (int r = 0; r < n; r++)
                        for (int c = 0; c < n; c++)
                            if (qr[r, c]) page.Rect(qx + c * cell, qy + r * cell, cell + 0.15, cell + 0.15, Ink);
                    page.TextRight(M + cw, qy + qrSize + 3, Tr("Scan to pay"), 8, false, Faint);
                    y = Math.Max(y, qy + qrSize + 18);
                }
            }
            y += 10;
        }
        if (!string.IsNullOrWhiteSpace(inv.Note))
        {
            var nl = w.Wrap(inv.Note, cw, 10, false);
            if (y + nl.Count * 13.5 > limit) { page = w.AddPage(PW, PH); y = M; }
            Para(page, M, y, cw, inv.Note, 10, false, Muted);
        }

        // ---- timesheet appendix ----
        if (!quote && inv.IncludeTimesheet && inv.Timesheet.Count > 0)
        {
            page = w.AddPage(PW, PH); y = M;
            page.Text(M, y, Tr("TIMESHEET"), 22, true, accent);
            page.TextRight(M + cw, y + 6, inv.Number, 12, true, Ink);
            y += 40;
            double h0 = cw * 0.84;
            void TsHeader()
            {
                page.Text(M, y, Tr("DESCRIPTION"), 8, true, Faint);
                page.TextRight(M + cw, y, Tr("HOURS"), 8, true, Faint);
                y += 16; page.Line(M, y, M + cw, y, 1, HeadRule);
            }
            TsHeader();
            foreach (var l in inv.Timesheet)
            {
                var lines = w.Wrap(l.Description, h0 - 10, 10, false);
                double rowH = lines.Count * 13.5 + 10;
                if (y + rowH > limit) { page = w.AddPage(PW, PH); y = M; TsHeader(); }
                double ty = y + 5;
                foreach (var ln in lines) { page.Text(M, ty, ln, 10, false, Ink); ty += 13.5; }
                page.TextRight(M + cw, y + 5, l.Hours.ToString("0.00"), 10, false, Ink);
                y += rowH;
                if (!minimal) page.Line(M, y, M + cw, y, 0.5, RowRule);
            }
            if (y + 28 > limit) { page = w.AddPage(PW, PH); y = M; }
            y += 10;
            page.Text(M, y, Tr("Total hours"), 11, true, Ink);
            page.TextRight(M + cw, y, inv.Timesheet.Sum(l => l.Hours).ToString("0.00"), 11, true, Ink);
        }

        // ---- footers (need the final page count) ----
        var zone = tz.IsDaylightSavingTime(inv.IssuedUtc) ? tz.DaylightName : tz.StandardName;
        var off = tz.GetUtcOffset(inv.IssuedUtc);
        var tzNote = Loc.FFor(inv.Language, "Times recorded in {0} (UTC{1})", zone, (off < TimeSpan.Zero ? "-" : "+") + off.ToString("hh\\:mm"));
        for (int i = 0; i < w.PageCount; i++)
        {
            var pg = w.PageAt(i);
            pg.Text(M, PH - M + 6, tzNote, 8, false, Faint);
            pg.TextRight(M + cw, PH - M + 6, Loc.FFor(inv.Language, "Page {0} of {1}", i + 1, w.PageCount), 8, false, Faint);
        }

        w.Save(path, Loc.FFor(inv.Language, "Invoice {0}", inv.Number));
    }
}
