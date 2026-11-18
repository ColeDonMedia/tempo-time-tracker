namespace Tempo;

/// <summary>Developer-only sample data, used with `Tempo.exe --demo` and TEMPO_DATA_DIR pointing at a scratch folder.</summary>
public static class DemoData
{
    public static void Seed()
    {
        var d = State.Data;
        if (d.Projects.Count > 0) return;
        var rnd = new Random(7);
        d.Billing = new BillingProfile { Name = "Northlight Studio", Email = "hello@northlight.example", Address = "12 Harbour Street\nPortland, OR 97201", PaymentInstructions = "Bank transfer: Northlight Studio · Acct 0000 1234 · Ref: invoice number", Note = "Thank you for your business." };
        d.Projects.AddRange(new[]
        {
            new Project { Id = "p1", Name = "Brand identity refresh", Client = "Hashmark", HourlyRate = 95, Color = "blue", ClientEmail = "ap@hashmark.example", BudgetType = "Hours", BudgetValue = 10 },
            new Project { Id = "p2", Name = "Website redesign", Client = "Lohman Scholars", HourlyRate = 82.5m, Color = "mint", BudgetType = "Fixed", BudgetValue = 4500 },
            new Project { Id = "p3", Name = "Packaging illustrations", Client = "Fernweh Tea", HourlyRate = 70, Color = "teal", BudgetType = "Amount", BudgetValue = 900 },
            new Project { Id = "p4", Name = "Pitch deck", Client = "Orbit Labs", HourlyRate = 110, Color = "violet" },
        });
        string[] tasks = { "Concept sketches", "Client call", "Revisions round 2", "Moodboard", "Layout exploration", "Final export", "Typography tests", "Asset cleanup" };
        var today = State.TodayLocal;
        for (int day = 0; day < 16; day++)
        {
            if (today.AddDays(-day).DayOfWeek is DayOfWeek.Sunday) continue;
            int n = rnd.Next(1, 4);
            var t = today.AddDays(-day).AddHours(9 + rnd.Next(0, 2));
            for (int i = 0; i < n; i++)
            {
                var p = d.Projects[rnd.Next(d.Projects.Count)];
                long ms = (long)(rnd.Next(25, 150) * 60_000);
                var start = State.ToUtc(t);
                if (start > DateTime.UtcNow.AddMinutes(-ms / 60000 - 5)) { t = t.AddHours(1); continue; }
                d.Sessions.Add(new Session { ProjectId = p.Id, Task = tasks[rnd.Next(tasks.Length)], StartedAtUtc = start, EndedAtUtc = start.AddMilliseconds(ms), DurationMilliseconds = ms, HourlyRate = p.HourlyRate });
                t = t.AddMinutes(ms / 60000 + 20 + rnd.Next(0, 40));
            }
        }
        // one paid + one outstanding historic invoice
        foreach (var (num, pid, status, daysAgo) in new[] { ("INV-2026-001", "p1", "Paid", 38), ("INV-2026-002", "p3", "Issued", 21) })
        {
            var p = d.Projects.First(x => x.Id == pid);
            var inv = new Invoice
            {
                Number = num, ProjectId = p.Id, ProjectName = p.Name, ClientName = p.Client, Status = status, IssuedUtc = DateTime.UtcNow.AddDays(-daysAgo), DueUtc = DateTime.UtcNow.AddDays(-daysAgo + 14),
                PeriodFrom = DateTime.Today.AddDays(-daysAgo - 30), PeriodTo = DateTime.Today.AddDays(-daysAgo), TimeZoneId = State.Tz.Id, Currency = d.Settings.CurrencyCode,
                Lines = { new InvoiceLine { Description = "Design & revisions", Hours = 14.5m, Rate = p.HourlyRate, Amount = Math.Round(14.5m * p.HourlyRate, 2) } },
                StudioName = d.Billing.Name, StudioEmail = d.Billing.Email, Note = d.Billing.Note, PaidUtc = status == "Paid" ? DateTime.UtcNow.AddDays(-daysAgo + 9) : null
            };
            InvoiceService.ApplyTotals(inv);
            d.Invoices.Add(inv);
        }
        d.Expenses.Add(new Expense { ProjectId = "p1", Date = today.AddDays(-4), Description = "Stock photo licence", Amount = 49m });
        d.Expenses.Add(new Expense { ProjectId = "p4", Date = today.AddDays(-2), Description = "Printing: proof set", Amount = 86.50m });
        // today: one overlap and a gap, so the Day view has something to show
        Session Today(string pid, string task, double fromH, double toH, decimal rate) => new()
        {
            ProjectId = pid, Task = task, StartedAtUtc = State.ToUtc(today.AddHours(fromH)), EndedAtUtc = State.ToUtc(today.AddHours(toH)),
            DurationMilliseconds = (long)((toH - fromH) * 3_600_000), HourlyRate = rate, Source = "manual",
        };
        d.Sessions.Add(Today("p2", "Client call", 10.25, 11, 95));
        d.Sessions.Add(Today("p1", "Logo refinements", 13, 14.5, 120));
        foreach (var s in d.Sessions.Where(x => x.Task == "Client call")) s.Billable = false;
        d.Invoices[1].Payments.Add(new Payment { DateUtc = DateTime.UtcNow.AddDays(-10), Amount = 300m, Note = "Deposit" });
        d.Invoices[1].PayLink = "https://pay.example.com/invoice/INV-2026-002";
        d.Invoices.Add(new Invoice
        {
            Kind = "Quote", Number = "QUO-2026-001", ProjectId = "p4", ProjectName = "Pitch deck", ClientName = "Orbit Labs", Status = "Sent", IssuedUtc = DateTime.UtcNow.AddDays(-3), DueUtc = DateTime.UtcNow.AddDays(27),
            TimeZoneId = State.Tz.Id, Currency = "USD", StudioName = d.Billing.Name, StudioEmail = d.Billing.Email,
            Lines = { new InvoiceLine { Description = "Investor deck design (20 slides)", Hours = 1, Rate = 2400, Amount = 2400, Kind = "Custom" } }
        });
        InvoiceService.ApplyTotals(d.Invoices[^1]);
        d.Recurring.Add(new RecurringInvoice { Name = "Monthly retainer", ProjectId = "p2", Frequency = "Monthly", NextRun = today.AddDays(12), Lines = { new InvoiceLine { Description = "Website care plan", Hours = 1, Rate = 450, Amount = 450, Kind = "Custom" } } });
        State.Normalize();
        d.InvoiceCounter = 3;
        d.InvoiceNumber = State.NextInvoiceNumber();
        State.Save();
    }
}
