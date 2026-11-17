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
            .Where(s => s.ProjectId == p.Id && s.InvoiceId == null && s.StartedAtUtc >= s0 && s.StartedAtUtc < s1)
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
            lines.Add(new InvoiceLine { Description = desc, Hours = hours, Rate = first.HourlyRate, Amount = Math.Round(hours * first.HourlyRate, 2) });
        }
        return (lines, ss);
    }

    public static void ApplyTotals(Invoice inv)
    {
        inv.TotalHours = inv.Lines.Sum(l => l.Hours);
        inv.Subtotal = inv.Lines.Sum(l => l.Amount);
        inv.DiscountAmount = Math.Round(inv.Subtotal * inv.DiscountPercent / 100m, 2);
        var taxable = inv.Subtotal - inv.DiscountAmount;
        inv.TaxAmount = Math.Round(taxable * inv.TaxPercent / 100m, 2);
        inv.Total = taxable + inv.TaxAmount;
    }

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Trim();
    }

    // ---------------- PDF (Tempo's own writer, see Pdf.cs) ----------------
    static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
    static readonly Color Ink = Hex("#1E2B34"), Muted = Hex("#5E6A74"), Faint = Hex("#8A949C"), Rule = Hex("#DDE2E0"),
                          RowRule = Hex("#E6EAE8"), HeadRule = Hex("#BFC7C4"), Coral = Hex("#0068F0"), Green = Hex("#008A5A");

    public static void WritePdf(Invoice inv, string path)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(inv.TimeZoneId); } catch { tz = TimeZoneInfo.Local; }
        DateTime L(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        string D(DateTime d) => Fmt.Date(d, inv.Language);
        string Tr(string s) => Loc.For(inv.Language, s);
        string Mo(decimal v) => Fmt.Money(v, inv.Currency);

        var w = new PdfWriter();
        bool letter = Fmt.UsLetter(inv.Currency);
        double PW = letter ? 612 : 595.28, PH = letter ? 792 : 841.89, M = 44, cw = PW - 2 * M, limit = PH - M - 24;
        var page = w.AddPage(PW, PH);
        double y = M;

        PdfWriter.Img? logo = !string.IsNullOrEmpty(inv.LogoPath) && File.Exists(inv.LogoPath) ? w.LoadImage(inv.LogoPath) : null;

        // wrapped paragraph helper: returns the y below the text
        double Para(PdfWriter.Page pg, double x, double top, double width, string text, double size, bool bold, Color c)
        {
            foreach (var line in w.Wrap(text, width, size, bold)) { pg.Text(x, top, line, size, bold, c); top += size * 1.35; }
            return top;
        }

        // ---- header ----
        double leftX = M;
        if (logo != null)
        {
            double k = Math.Min(54.0 / logo.Width, 54.0 / logo.Height);
            page.Image(logo, M, y, logo.Width * k, logo.Height * k);
            leftX += 66;
        }
        var name = string.IsNullOrWhiteSpace(inv.StudioName) ? Tr("Your studio") : inv.StudioName;
        double ny = Para(page, leftX, y, cw * 0.55 - (leftX - M), name, 17, true, Ink);
        if (!string.IsNullOrWhiteSpace(inv.StudioEmail)) ny = Para(page, leftX, ny + 1, cw * 0.55, inv.StudioEmail, 10, false, Muted);
        double right = M + cw;
        page.TextRight(right, y - 2, Tr("INVOICE"), 26, true, Coral);
        page.TextRight(right, y + 34, inv.Number, 12, true, Ink);
        if (inv.Status == "Paid") page.TextRight(right, y + 52, Tr("PAID") + (inv.PaidUtc is DateTime pd ? "  " + D(L(pd)) : ""), 10, true, Green);
        else if (inv.Status == "Void") page.TextRight(right, y + 52, Tr("VOID"), 10, true, Faint);
        y = Math.Max(ny, y + 70) + 12;
        page.Line(M, y, M + cw, y, 0.8, Rule);
        y += 18;

        // ---- parties + details ----
        double detW = 225, colW = (cw - detW - 24) / 2;
        double yb = y, yf = y, yd = y;
        page.Text(M, yb, Tr("BILL TO"), 8, true, Faint); yb += 13;
        yb = Para(page, M, yb, colW, inv.ClientName, 11, true, Ink);
        yb = Para(page, M, yb, colW, inv.ProjectName, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.ClientEmail)) yb = Para(page, M, yb, colW, inv.ClientEmail, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.ClientAddress)) yb = Para(page, M, yb, colW, inv.ClientAddress, 10, false, Muted);
        double fx = M + colW + 12;
        page.Text(fx, yf, Tr("FROM"), 8, true, Faint); yf += 13;
        if (!string.IsNullOrWhiteSpace(inv.StudioAddress)) yf = Para(page, fx, yf, colW, inv.StudioAddress, 10, false, Muted);
        if (!string.IsNullOrWhiteSpace(inv.TaxId)) yf = Para(page, fx, yf, colW, Tr("Tax ID: ") + inv.TaxId, 10, false, Muted);
        double dx = M + cw - detW;
        void Kv(string k, string v) { page.Text(dx, yd, k, 10, false, Faint); page.TextRight(dx + detW, yd, v, 10, true, Ink); yd += 14; }
        Kv(Tr("Issued date"), D(L(inv.IssuedUtc)));
        Kv(Tr("Due date"), D(L(inv.DueUtc)));
        Kv(Tr("Period"), D(inv.PeriodFrom) + " – " + D(inv.PeriodTo));
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
            if (y + rowH > limit)
            {
                page = w.AddPage(PW, PH); y = M;
                TableHeader();
            }
            double ty = y + 7;
            foreach (var ln in lines) { page.Text(M, ty, ln, 10, false, Ink); ty += 13.5; }
            page.TextRight(M + c0 + c1, y + 7, l.Hours.ToString("0.00"), 10, false, Ink);
            page.TextRight(x2, y + 7, Mo(l.Rate), 10, false, Ink);
            page.TextRight(x3, y + 7, Mo(l.Amount), 10, true, Ink);
            y += rowH;
            page.Line(M, y, M + cw, y, 0.6, RowRule);
        }

        // ---- totals ----
        var rows = new List<(string K, string V, bool Big)> { (Tr("Total hours"), inv.TotalHours.ToString("0.00"), false), (Tr("Subtotal"), Mo(inv.Subtotal), false) };
        if (inv.DiscountAmount != 0) rows.Add((Loc.FFor(inv.Language, "Discount ({0}%)", Fmt.Dec(inv.DiscountPercent)), "-" + Mo(inv.DiscountAmount), false));
        if (inv.TaxAmount != 0 || inv.TaxPercent != 0) rows.Add(($"{Tr(inv.TaxLabel)} ({Fmt.Dec(inv.TaxPercent)}%)", Mo(inv.TaxAmount), false));
        rows.Add((Tr(inv.Status == "Paid" ? "TOTAL PAID" : "TOTAL DUE"), Mo(inv.Total), true));
        double totH = rows.Count * 18 + 30;
        y += 18;
        if (y + totH > limit) { page = w.AddPage(PW, PH); y = M; }
        double tw = 250, tx = M + cw - tw;
        foreach (var (k, v, big) in rows)
        {
            if (big) { page.Line(tx, y + 2, tx + tw, y + 2, 0.8, Rule); y += 10; }
            page.Text(tx, y + (big ? 3 : 0), k, big ? 12 : 10, true, big ? Ink : Muted);
            page.TextRight(tx + tw, y, v, big ? 15 : 10, true, big ? Coral : Ink);
            y += big ? 26 : 18;
        }

        // ---- payment + note ----
        y += 14;
        if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions))
        {
            var pl = w.Wrap(inv.PaymentInstructions, cw, 10, false);
            if (y + 20 + pl.Count * 13.5 > limit) { page = w.AddPage(PW, PH); y = M; }
            page.Text(M, y, Tr("PAYMENT DETAILS"), 8, true, Faint); y += 13;
            y = Para(page, M, y, cw, inv.PaymentInstructions, 10, false, Muted) + 12;
        }
        if (!string.IsNullOrWhiteSpace(inv.Note))
        {
            var nl = w.Wrap(inv.Note, cw, 10, false);
            if (y + nl.Count * 13.5 > limit) { page = w.AddPage(PW, PH); y = M; }
            Para(page, M, y, cw, inv.Note, 10, false, Muted);
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
