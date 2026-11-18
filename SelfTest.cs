using System.Text;

namespace Tempo;

/// <summary>
/// Developer check: exercises Tempo's data rules without any UI and writes PASS/FAIL lines to a file.
/// Run:  Tempo.exe --selftest out.txt   (needs TEMPO_DATA_DIR pointing at a scratch folder)
/// </summary>
public static class SelfTest
{
    static readonly StringBuilder Log = new();
    static int fails;
    public static List<(string Text, string File)> QrFiles = new();
    public static string OutDir = "";

    static string SaveQr(bool[,] m, int n)
    {
        int size = m.GetLength(0), scale = 8, quiet = 4, px = (size + quiet * 2) * scale;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, px, px));
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                if (m[y, x]) dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new System.Windows.Rect((x + quiet) * scale, (y + quiet) * scale, scale, scale));
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(px, px, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        var file = System.IO.Path.Combine(OutDir, $"qr-{n}.png");
        using var fs = System.IO.File.Create(file); enc.Save(fs);
        return file;
    }

    static void Check(string name, bool ok, string? detail = null)
    {
        Log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail == null ? "" : "  -> " + detail)}");
        if (!ok) fails++;
    }

    public static void Run(string path)
    {
        OutDir = System.IO.Path.GetDirectoryName(path) ?? "";
        try { Tests(); }
        catch (Exception ex) { Check("no exception", false, ex.ToString()); }
        Log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        System.IO.File.WriteAllText(path, Log.ToString());
        System.IO.File.WriteAllLines(System.IO.Path.Combine(OutDir, "qr-manifest.txt"), QrFiles.Select(q => q.File + "	" + q.Text));
    }

    static Session S(string pid, double hours, decimal rate, bool billable = true, int daysAgo = 0) => new()
    {
        ProjectId = pid, HourlyRate = rate, Billable = billable,
        StartedAtUtc = DateTime.UtcNow.AddDays(-daysAgo).AddHours(-hours), EndedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
        DurationMilliseconds = (long)(hours * 3600000)
    };

    static void Tests()
    {
        // ---------- client migration from old text-only data ----------
        State.Data = new Workspace();
        State.Data.Projects.Add(new Project { Id = "a", Name = "Logo", Client = "Hashmark", ClientEmail = "ap@h.example", HourlyRate = 100 });
        State.Data.Projects.Add(new Project { Id = "b", Name = "Site", Client = "  hashmark ", HourlyRate = 80 });
        State.Data.Projects.Add(new Project { Id = "c", Name = "Deck", Client = "Orbit", HourlyRate = 120 });
        State.Normalize();
        Check("migration merges the same client by name", State.Data.Clients.Count == 2, State.Data.Clients.Count.ToString());
        Check("both projects share one client id", State.Data.Projects[0].ClientId == State.Data.Projects[1].ClientId && State.Data.Projects[0].ClientId != "");
        Check("client picks up the project's email", State.Data.Clients.First(c => c.Name == "Hashmark").Email == "ap@h.example");
        State.Normalize();
        Check("migration is idempotent", State.Data.Clients.Count == 2);

        // ---------- billable vs non-billable ----------
        State.Data.Sessions.Add(S("a", 2, 100));
        State.Data.Sessions.Add(S("a", 1, 100, billable: false));
        Check("unbilled total ignores non-billable time", State.UnbilledFor("a") == 200m, State.UnbilledFor("a").ToString());
        Check("Billed() is zero for non-billable", State.Billed(State.Data.Sessions[1]) == 0m);

        // ---------- budgets ----------
        var pa = State.Data.Projects[0];
        pa.BudgetType = "Hours"; pa.BudgetValue = 4; pa.BudgetPeriod = "Total";
        var u = Budget.Usage(pa)!;
        Check("hours budget counts all tracked time (3h of 4h = 75%)", Math.Abs(u.Fraction - 0.75) < 0.001 && !u.Warn && !u.Over, u.Fraction.ToString());
        State.Data.Sessions.Add(S("a", 0.5, 100));
        u = Budget.Usage(pa)!;
        Check("budget warns at 80%+ (3.5h of 4h)", u.Warn && !u.Over, u.Fraction.ToString());
        State.Data.Sessions.Add(S("a", 1, 100));
        Check("budget flags over 100%", Budget.Usage(pa)!.Over);
        pa.BudgetType = "Amount"; pa.BudgetValue = 1000;
        Check("amount budget counts billable money only (3.5h billable x 100 = 350 of 1000)", Math.Abs(Budget.Usage(pa)!.Fraction - 0.35) < 0.001, Budget.Usage(pa)!.Fraction.ToString());
        pa.BudgetType = "Fixed"; pa.BudgetValue = 300; pa.HourlyRate = 100;   // 4.5h tracked => about 66/h, below the 100/h rate
        Check("fixed fee shows an effective rate and flags it when below the hourly rate", Budget.Usage(pa)!.Over);
        pa.BudgetType = "None";
        Check("no budget means no usage", Budget.Usage(pa) == null);

        // ---------- trash / undo ----------
        var before = State.Data.Sessions.Count;
        var sess = State.Data.Sessions[0];
        var t = TrashBin.Add("Session", sess, "test");
        State.Data.Sessions.Remove(sess);
        Check("deleted session is gone", State.Data.Sessions.Count == before - 1);
        Check("restore brings it back", TrashBin.Restore(t) && State.Data.Sessions.Count == before && State.Data.Trash.Count == 0);

        var bundle = new ProjectBundle { Project = State.Data.Projects[2], Sessions = State.Data.Sessions.Where(x => x.ProjectId == "c").ToList() };
        var tp = TrashBin.Add("Project", bundle, "Deck");
        State.Data.Projects.RemoveAll(p => p.Id == "c");
        Check("project restore returns the project", TrashBin.Restore(tp) && State.Data.Projects.Any(p => p.Id == "c"));

        var inv = new Invoice { Id = "inv1", Number = "INV-1", SessionIds = new() { State.Data.Sessions[0].Id }, Status = "Issued" };
        State.Data.Sessions[0].InvoiceId = "inv1";
        State.Data.Invoices.Add(inv);
        var ti = TrashBin.Add("Invoice", inv, "INV-1");
        State.Data.Invoices.Remove(inv); State.Data.Sessions[0].InvoiceId = null;
        Check("restoring an invoice re-links its entries", TrashBin.Restore(ti) && State.Data.Sessions[0].InvoiceId == "inv1");

        // ---------- invoice maths ----------
        var i2 = new Invoice { Total = 100m, LateFeeAmount = 10m, Payments = { new Payment { Amount = 40m } } };
        Check("balance = total + late fee - payments", i2.Balance == 70m, i2.Balance.ToString());
        i2.Status = "Void";
        Check("void invoices owe nothing", i2.Balance == 0m);

        // ---------- QR code ----------
        QrFiles = new List<(string Text, string File)>();
        foreach (var txt in new[] { "https://pay.example.com/i/1", "https://pay.example.com/invoice/INV-2026-003?amount=1129.05&ref=ABC123", "https://pay.example.com/checkout?invoice=INV-2026-003&client=Orbit+Labs&amount=1129.05&currency=USD&ref=ABC123XYZ", "https://pay.example.com/checkout?invoice=INV-2026-003&client=Orbit+Labs+Studio&project=Brand+identity+refresh&amount=1129.05&currency=USD&ref=ABC123XYZ&note=thank+you", "https://pay.example.com/checkout?invoice=INV-2026-003&client=Orbit+Labs+Studio&project=Brand+identity+refresh&amount=1129.05&currency=USD&ref=ABC123XYZ&n=thanks" })
        {
            var qm = QrCode.Encode(txt);
            Check($"QR encodes {txt.Length}-char text", qm != null && qm.GetLength(0) >= 21);
            if (qm != null) QrFiles.Add((txt, SaveQr(qm, QrFiles.Count)));
        }
        Check("QR refuses text that is too long", QrCode.Encode(new string('x', 300)) == null);

        // ---------- reports ----------
        State.Data = new Workspace();
        State.Data.Clients.Add(new Client { Id = "c1", Name = "Acme" });
        State.Data.Clients.Add(new Client { Id = "c2", Name = "Beta" });
        State.Data.Projects.Add(new Project { Id = "p1", Name = "Logo", ClientId = "c1", Client = "Acme", HourlyRate = 100 });
        State.Data.Projects.Add(new Project { Id = "p2", Name = "Site", ClientId = "c2", Client = "Beta", HourlyRate = 50 });
        var s1 = S("p1", 2, 100, true, 1); s1.Tags = new() { "design", "meeting" }; s1.Task = "Concepts";
        var s2 = S("p1", 1, 100, false, 1); s2.Task = "Concepts";
        var s3 = S("p2", 4, 50, true, 40); s3.Task = "Build";
        State.Data.Sessions.AddRange(new[] { s1, s2, s3 });
        State.Data.Expenses.Add(new Expense { ProjectId = "p1", Date = State.TodayLocal.AddDays(-1), Amount = 30m });
        State.Data.Expenses.Add(new Expense { ProjectId = "p2", Date = State.TodayLocal.AddDays(-40), Amount = 99m });

        var all = ReportEngine.Compute(new ReportFilter { GroupBy = "Project" });
        Check("report totals: 7h tracked, 6h billable, 400 earned", all.TotalHours == 7m && all.BillableHours == 6m && all.Earned == 400m, $"{all.TotalHours}/{all.BillableHours}/{all.Earned}");
        Check("report groups by project, biggest first", all.Rows.Count == 2 && all.Rows[0].Label == "Site" && all.Rows[0].Hours == 4m);
        var by30 = ReportEngine.Compute(new ReportFilter { From = State.TodayLocal.AddDays(-29), To = State.TodayLocal });
        Check("date range excludes older time and expenses", by30.TotalHours == 3m && by30.Expenses == 30m, $"{by30.TotalHours}/{by30.Expenses}");
        var nb = ReportEngine.Compute(new ReportFilter { Billable = "Non-billable" });
        Check("non-billable filter", nb.TotalHours == 1m && nb.Earned == 0m);
        var cl = ReportEngine.Compute(new ReportFilter { ClientId = "c2" });
        Check("client filter", cl.TotalHours == 4m && cl.Rows.Count == 1);
        var tg = ReportEngine.Compute(new ReportFilter { GroupBy = "Tag" });
        Check("tag grouping counts a multi-tag entry under each tag", tg.Rows.Any(r => r.Label == "design" && r.Hours == 2m) && tg.Rows.Any(r => r.Label == "meeting" && r.Hours == 2m));
        var dy = ReportEngine.Compute(new ReportFilter { GroupBy = "Day" });
        Check("day grouping is oldest first", dy.Rows.Count == 2 && string.CompareOrdinal(dy.Rows[0].Key, dy.Rows[1].Key) < 0);
        var csv = ReportEngine.Csv(all, new ReportFilter());
        Check("report CSV has a header, rows and a total", csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length == 4);

        // ---------- invoicing ----------
        var invP = State.Data.Projects[0];
        State.Data.Expenses.Clear();
        State.Data.Expenses.Add(new Expense { ProjectId = "p1", Date = State.TodayLocal.AddDays(-2), Amount = 40m, Billable = true });
        State.Data.Expenses.Add(new Expense { ProjectId = "p1", Date = State.TodayLocal.AddDays(-2), Amount = 15m, Billable = false });
        State.Data.Expenses.Add(new Expense { ProjectId = "p1", Date = State.TodayLocal.AddDays(-2), Amount = 25m, Billable = true, InvoiceId = "old" });
        var (el, es) = InvoiceService.ExpenseLines(invP, State.TodayLocal.AddDays(-30), State.TodayLocal);
        Check("expense lines take billable, un-invoiced expenses only", es.Count == 1 && el[0].Amount == 40m && el[0].Kind == "Expense", es.Count.ToString());
        var (tl, tsessions) = InvoiceService.BuildLines(invP, State.TodayLocal.AddDays(-30), State.TodayLocal, "Task", 0);
        Check("time lines skip non-billable time", tsessions.All(x => x.Billable) && tl.Sum(l => l.Hours) == 2m, tl.Sum(l => l.Hours).ToString());
        var mixed = new Invoice { TaxPercent = 10m };
        mixed.Lines.AddRange(tl); mixed.Lines.AddRange(el);
        mixed.Lines.Add(new InvoiceLine { Description = "Fee", Amount = 100m, Kind = "Fee" });
        mixed.Lines.Add(new InvoiceLine { Description = "Custom", Hours = 3, Rate = 20m, Amount = 60m, Kind = "Custom" });
        InvoiceService.ApplyTotals(mixed);
        Check("only time lines count toward total hours", mixed.TotalHours == 2m, mixed.TotalHours.ToString());
        Check("subtotal and tax add up across all line types", mixed.Subtotal == 400m && mixed.TaxAmount == 40m && mixed.Total == 440m, $"{mixed.Subtotal}/{mixed.TaxAmount}/{mixed.Total}");
        mixed.LateFeeAmount = 22m; mixed.Payments.Add(new Payment { Amount = 100m });
        Check("late fee and partial payment flow into the balance", mixed.GrandTotal == 462m && mixed.Balance == 362m, mixed.Balance.ToString());

        State.Data.InvoiceNumber = State.NextInvoiceNumber();
        State.Data.Recurring.Add(new RecurringInvoice { Name = "Retainer", ProjectId = "p1", Frequency = "Monthly", NextRun = State.TodayLocal.AddMonths(-2).AddDays(-1), Lines = { new InvoiceLine { Description = "Care plan", Hours = 1, Rate = 500m, Amount = 500m, Kind = "Custom" } } });
        var before2 = State.Data.Invoices.Count;
        var made = RecurringService.RunDue();
        Check("recurring invoices catch up on every missed period", made == 3 && State.Data.Invoices.Count == before2 + 3, made.ToString());
        Check("recurring schedule moves past today", State.Data.Recurring[0].NextRun > State.TodayLocal);
        Check("recurring invoices get unique numbers", State.Data.Invoices.Select(x => x.Number).Distinct().Count() == State.Data.Invoices.Count);
        Check("next dates advance by the chosen frequency", RecurringService.Advance(new DateTime(2026, 1, 31), "Monthly") == new DateTime(2026, 2, 28) && RecurringService.Advance(new DateTime(2026, 1, 1), "Weekly") == new DateTime(2026, 1, 8) && RecurringService.Advance(new DateTime(2026, 1, 1), "Quarterly") == new DateTime(2026, 4, 1));

        // ---------- timesheet grid ----------
        var proj = State.Data.Projects[1];
        var day = State.TodayLocal.AddDays(-3);
        Check("typing hours into an empty cell creates a timesheet entry", TimesheetView.CommitCell(proj, day, "2.5")
              && State.Data.Sessions.Any(x => x.Source == "grid" && x.ProjectId == "p2" && x.DurationMilliseconds == 9_000_000));
        Check("editing the cell updates it instead of adding another", TimesheetView.CommitCell(proj, day, "3") && State.Data.Sessions.Count(x => x.Source == "grid") == 1
              && State.Data.Sessions.First(x => x.Source == "grid").DurationMilliseconds == 10_800_000);
        Check("clearing the cell removes the timesheet entry", TimesheetView.CommitCell(proj, day, "") && State.Data.Sessions.All(x => x.Source != "grid"));
        var real = S("p2", 2, 50, true, 3); real.StartedAtUtc = State.ToUtc(day.AddHours(10)); real.EndedAtUtc = real.StartedAtUtc.AddHours(2);
        State.Data.Sessions.Add(real);
        Check("a cell can't go below time already tracked that day", !TimesheetView.CommitCell(proj, day, "1"));
        Check("a cell above tracked time adds the difference", TimesheetView.CommitCell(proj, day, "3.5") && State.Data.Sessions.First(x => x.Source == "grid").DurationMilliseconds == 5_400_000);
    }
}
