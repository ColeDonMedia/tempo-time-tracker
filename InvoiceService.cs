using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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

    // ---------------- PDF ----------------
    const string Ink = "#1E2B34", Muted = "#5E6A74", Faint = "#8A949C", Line = "#DDE2E0", Coral = "#E8573C", Green = "#1B8F69";

    public static void WritePdf(Invoice inv, string path)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(inv.TimeZoneId); } catch { tz = TimeZoneInfo.Local; }
        DateTime L(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        string D(DateTime d) => d.ToString("MMM d, yyyy");
        string M(decimal v) => Fmt.Money(v, inv.Currency);
        byte[]? logo = null;
        if (!string.IsNullOrEmpty(inv.LogoPath) && File.Exists(inv.LogoPath)) { try { logo = File.ReadAllBytes(inv.LogoPath); } catch { } }

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(Fmt.UsLetter(inv.Currency) ? PageSizes.Letter : PageSizes.A4);
            page.Margin(44);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).FontFamily("Segoe UI"));

            page.Content().Column(col =>
            {
                col.Spacing(22);

                // header
                col.Item().Row(row =>
                {
                    row.RelativeItem().Row(left =>
                    {
                        if (logo != null) left.ConstantItem(54).PaddingRight(12).Height(54).Image(logo).FitArea();
                        left.RelativeItem().Column(c =>
                        {
                            c.Item().Text(string.IsNullOrWhiteSpace(inv.StudioName) ? "Your studio" : inv.StudioName).FontSize(17).SemiBold();
                            if (!string.IsNullOrWhiteSpace(inv.StudioEmail)) c.Item().Text(inv.StudioEmail).FontColor(Muted);
                        });
                    });
                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().Text("INVOICE").FontSize(26).SemiBold().FontColor(Coral);
                        c.Item().Text(inv.Number).FontSize(12).SemiBold();
                        if (inv.Status == "Paid") c.Item().Text("PAID" + (inv.PaidUtc is DateTime pd ? "  " + D(L(pd)) : "")).FontColor(Green).SemiBold();
                        if (inv.Status == "Void") c.Item().Text("VOID").FontColor(Faint).SemiBold();
                    });
                });

                col.Item().LineHorizontal(0.8f).LineColor(Line);

                // parties + details
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("BILL TO").FontSize(8).SemiBold().FontColor(Faint);
                        c.Item().PaddingTop(3).Text(inv.ClientName).SemiBold().FontSize(11);
                        c.Item().Text(inv.ProjectName).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(inv.ClientEmail)) c.Item().Text(inv.ClientEmail).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(inv.ClientAddress)) c.Item().Text(inv.ClientAddress).FontColor(Muted);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("FROM").FontSize(8).SemiBold().FontColor(Faint);
                        if (!string.IsNullOrWhiteSpace(inv.StudioAddress)) c.Item().PaddingTop(3).Text(inv.StudioAddress).FontColor(Muted);
                        if (!string.IsNullOrWhiteSpace(inv.TaxId)) c.Item().Text("Tax ID: " + inv.TaxId).FontColor(Muted);
                    });
                    row.ConstantItem(190).Column(c =>
                    {
                        void Kv(string k, string v) => c.Item().Row(r => { r.RelativeItem().Text(k).FontColor(Faint); r.AutoItem().Text(v).SemiBold(); });
                        Kv("Issued", D(L(inv.IssuedUtc)));
                        Kv("Due", D(L(inv.DueUtc)));
                        Kv("Period", $"{inv.PeriodFrom:MMM d} – {inv.PeriodTo:MMM d, yyyy}");
                        if (!string.IsNullOrWhiteSpace(inv.PoNumber)) Kv("PO number", inv.PoNumber);
                        Kv("Currency", inv.Currency);
                    });
                });

                // line items
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(6); c.RelativeColumn(1.2f); c.RelativeColumn(1.5f); c.RelativeColumn(1.7f);
                    });
                    static IContainer H(IContainer c) => c.BorderBottom(1).BorderColor("#BFC7C4").PaddingVertical(6).DefaultTextStyle(x => x.FontSize(8).SemiBold().FontColor("#8A949C"));
                    static IContainer B(IContainer c) => c.BorderBottom(0.6f).BorderColor("#E6EAE8").PaddingVertical(7);
                    t.Header(h =>
                    {
                        h.Cell().Element(H).Text("DESCRIPTION");
                        h.Cell().Element(H).AlignRight().Text("HOURS");
                        h.Cell().Element(H).AlignRight().Text("RATE");
                        h.Cell().Element(H).AlignRight().Text("AMOUNT");
                    });
                    foreach (var l in inv.Lines)
                    {
                        t.Cell().Element(B).Text(l.Description);
                        t.Cell().Element(B).AlignRight().Text(l.Hours.ToString("0.00"));
                        t.Cell().Element(B).AlignRight().Text(M(l.Rate));
                        t.Cell().Element(B).AlignRight().Text(M(l.Amount)).SemiBold();
                    }
                });

                // totals
                col.Item().AlignRight().Width(250).Column(c =>
                {
                    void Row(string k, string v, bool strong = false) => c.Item().PaddingVertical(2).Row(r =>
                    {
                        r.RelativeItem().Text(k).FontColor(strong ? Ink : Muted).FontSize(strong ? 12 : 10).SemiBold();
                        r.AutoItem().Text(v).FontSize(strong ? 15 : 10).SemiBold().FontColor(strong ? Coral : Ink);
                    });
                    Row("Total hours", inv.TotalHours.ToString("0.00"));
                    Row("Subtotal", M(inv.Subtotal));
                    if (inv.DiscountAmount != 0) Row($"Discount ({Fmt.Dec(inv.DiscountPercent)}%)", "-" + M(inv.DiscountAmount));
                    if (inv.TaxAmount != 0 || inv.TaxPercent != 0) Row($"{inv.TaxLabel} ({Fmt.Dec(inv.TaxPercent)}%)", M(inv.TaxAmount));
                    c.Item().PaddingTop(4).LineHorizontal(0.8f).LineColor(Line);
                    Row(inv.Status == "Paid" ? "TOTAL PAID" : "TOTAL DUE", M(inv.Total), true);
                });

                // payment + note
                col.Item().Column(c =>
                {
                    c.Spacing(10);
                    if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions))
                        c.Item().Column(x => { x.Item().Text("PAYMENT DETAILS").FontSize(8).SemiBold().FontColor(Faint); x.Item().PaddingTop(3).Text(inv.PaymentInstructions).FontColor(Muted); });
                    if (!string.IsNullOrWhiteSpace(inv.Note))
                        c.Item().Text(inv.Note).FontColor(Muted).Italic();
                });
            });

            page.Footer().Row(r =>
            {
                r.RelativeItem().Text($"Times recorded in {(tz.IsDaylightSavingTime(inv.IssuedUtc) ? tz.DaylightName : tz.StandardName)} (UTC{(tz.GetUtcOffset(inv.IssuedUtc) < TimeSpan.Zero ? "-" : "+")}{tz.GetUtcOffset(inv.IssuedUtc):hh\\:mm})").FontSize(8).FontColor(Faint);
                r.AutoItem().Text(t => { t.DefaultTextStyle(x => x.FontSize(8).FontColor(Faint)); t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages(); });
            });
        })).GeneratePdf(path);
    }
}
