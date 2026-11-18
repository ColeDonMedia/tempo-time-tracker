namespace Tempo;

/// <summary>Creates invoices from recurring templates when their date arrives (also catches up if Tempo wasn't opened for a while).</summary>
public static class RecurringService
{
    public static DateTime Advance(DateTime d, string frequency) => frequency switch
    {
        "Weekly" => d.AddDays(7),
        "Quarterly" => d.AddMonths(3),
        _ => d.AddMonths(1),
    };

    public static Invoice Create(RecurringInvoice r, DateTime issueDate)
    {
        var d = State.Data; var s = d.Settings; var b = d.Billing;
        var p = State.Proj(r.ProjectId);
        var client = State.ClientOf(p);
        var issued = State.ToUtc(issueDate.Date.AddHours(9));
        var inv = new Invoice
        {
            Language = Loc.Current, Kind = "Invoice", Number = d.InvoiceNumber, ProjectId = r.ProjectId, ProjectName = p?.Name ?? r.Name,
            ClientId = client?.Id ?? "", ClientName = client?.Name ?? p?.Client ?? "", ClientEmail = client?.Email ?? p?.ClientEmail ?? "", ClientAddress = client?.Address ?? p?.ClientAddress ?? "",
            PoNumber = r.PoNumber, IssuedUtc = issued, DueUtc = issued.AddDays(r.DueDays), PeriodFrom = issueDate.Date, PeriodTo = issueDate.Date,
            TimeZoneId = State.Tz.Id, Currency = string.IsNullOrEmpty(client?.Currency) ? s.CurrencyCode : client!.Currency,
            Lines = r.Lines.Select(l => new InvoiceLine { Description = l.Description, Hours = l.Hours, Rate = l.Rate, Amount = l.Amount, Kind = l.Kind }).ToList(),
            TaxLabel = r.TaxLabel, TaxPercent = r.TaxPercent, Note = r.Note, Template = s.InvoiceTemplate, PayLink = SafeOpen.IsPayLink(s.DefaultPayLink) ? s.DefaultPayLink : "",
            PaymentInstructions = b.PaymentInstructions, StudioName = b.Name, StudioEmail = b.Email, StudioAddress = b.Address, TaxId = b.TaxId, LogoPath = b.LogoPath,
            RecurringId = r.Id
        };
        InvoiceService.ApplyTotals(inv);
        d.Invoices.Add(inv);
        d.InvoiceCounter = Math.Max(d.InvoiceCounter, 1) + 1;
        d.InvoiceNumber = State.NextInvoiceNumber();
        return inv;
    }

    /// <summary>Returns how many invoices were created.</summary>
    public static int RunDue()
    {
        var d = State.Data; var today = State.TodayLocal; int made = 0;
        foreach (var r in d.Recurring.Where(x => x.Active && x.NextRun.Date <= today).ToList())
        {
            for (int guard = 0; guard < 24 && r.NextRun.Date <= today; guard++)
            {
                Create(r, r.NextRun);
                r.NextRun = Advance(r.NextRun, r.Frequency);
                made++;
            }
        }
        if (made > 0) State.Notify();
        return made;
    }
}
