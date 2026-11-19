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

        StringTables();
        NeverForget();
        Calendar();
        CsvImports();
        EntryToolsTests();
        Updates();
        Hardening();
        TrackingTests();
        MoneyTests();
        SyncTests();
        PolishTests();
        IntegrationTests();
        SquareTests();
    }

    // ---------- phase 12: Square invoices ----------
    static void SquareTests()
    {
        System.Text.Json.JsonElement J(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;
        Invoice Sq(string cur = "USD")
        {
            var i = new Invoice { Id = "0123456789abcdef0123456789abcdef", Number = "INV-2026-041", Currency = cur, Status = "Issued", ClientName = "Hashmark Studio", ClientEmail = "ap@hashmark.example",
                                  TimeZoneId = "UTC", IssuedUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), DueUtc = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc) };
            i.Lines.Add(new InvoiceLine { Description = "Design \"review\" & fixes", Hours = 2.5m, Rate = 80m, Amount = 200m, Kind = "Time" });
            i.Lines.Add(new InvoiceLine { Description = "Stock photo", Amount = 33.33m, Kind = "Expense" });
            i.DiscountPercent = 10m;
            i.Taxes.Add(new TaxLine { Label = "GST", Percent = 5m });
            i.Taxes.Add(new TaxLine { Label = "PST", Percent = 7m });
            InvoiceService.ApplyTotals(i);
            return i;
        }

        Check("square: production and sandbox addresses", SquareSync.Api(false) == "https://connect.squareup.com" && SquareSync.Api(true) == "https://connect.squareupsandbox.com");
        Check("square: amounts in the smallest currency unit", SquareSync.ToMinor(1234.56m, "USD") == 123456 && SquareSync.ToMinor(5000m, "JPY") == 5000 && SquareSync.ToMinor(1.5m, "KWD") == 1500);

        var ok = Sq();
        Check("square: an open invoice with an email can be sent", SquareSync.CannotSend(ok, "USD") == null, SquareSync.CannotSend(ok, "USD"));
        Check("square: refuses a currency the Square location doesn't use", SquareSync.CannotSend(ok, "CAD") != null);
        var noMail = Sq(); noMail.ClientEmail = "";
        var badMail = Sq(); badMail.ClientEmail = "not an email";
        Check("square: needs the client's email (Square emails the invoice)", SquareSync.CannotSend(noMail, "USD") != null && SquareSync.CannotSend(badMail, "USD") != null);
        var paidInv = Sq(); paidInv.Status = "Paid";
        var quote = Sq(); quote.Kind = "Quote"; quote.Status = "Sent";
        var partly = Sq(); partly.Payments.Add(new Payment { Amount = 10m });
        var neg = Sq(); neg.Lines.Add(new InvoiceLine { Description = "Credit", Amount = -20m, Kind = "Custom" }); InvoiceService.ApplyTotals(neg);
        Check("square: only open, unpaid invoices without negative lines", SquareSync.CannotSend(paidInv, "USD") != null && SquareSync.CannotSend(quote, "USD") != null
            && SquareSync.CannotSend(partly, "USD") != null && SquareSync.CannotSend(neg, "USD") != null);

        var fp = SquareSync.Fingerprint(ok);
        var edited = Sq(); edited.Lines[0].Amount = 240m; InvoiceService.ApplyTotals(edited);
        var withPay = Sq(); withPay.Payments.Add(new Payment { Amount = 5m });
        Check("square: fingerprint is stable, changes when the bill changes", fp.Length == 16 && fp.All(Uri.IsHexDigit) && fp == SquareSync.Fingerprint(Sq())
            && fp != SquareSync.Fingerprint(edited) && fp == SquareSync.Fingerprint(withPay));

        var retry = Sq(); retry.SquareAttempt = 1;
        var ck = SquareSync.CustomerKey("AP@Hashmark.example ");
        Check("square: request keys fit Square's limits and never repeat after a draft is replaced",
            SquareSync.OrderKey(ok, fp, false).Length <= 192 && SquareSync.InvoiceKey(ok, fp).Length <= 128 && SquareSync.PublishKey(ok, fp).Length <= 128 && ck.Length <= 45
            && SquareSync.OrderKey(ok, fp, false) != SquareSync.OrderKey(ok, fp, true) && SquareSync.InvoiceKey(ok, fp) != SquareSync.InvoiceKey(retry, fp)
            && SquareSync.InvoiceKey(ok, fp) != SquareSync.PublishKey(ok, fp) && SquareSync.OrderKey(ok, fp, false).Length > 0);
        Check("square: customer key is the same for any spelling of the email and doesn't contain it", ck == SquareSync.CustomerKey("ap@hashmark.example") && !ck.Contains("hashmark"));

        // order: Tempo's lines, discount and taxes apply to the work lines only; a late fee is never discounted or taxed
        var lf = Sq(); lf.LateFeeAmount = 15m;
        var o = J(SquareSync.OrderBody(lf, "L1", "C1", false, "key-1"));
        var order = o.GetProperty("order");
        var li = order.GetProperty("line_items");
        Check("square order: location, customer, key and one line per Tempo line plus the late fee",
            o.GetProperty("idempotency_key").GetString() == "key-1" && order.GetProperty("location_id").GetString() == "L1" && order.GetProperty("customer_id").GetString() == "C1"
            && li.GetArrayLength() == 3 && li[0].GetProperty("quantity").GetString() == "1" && li[0].GetProperty("base_price_money").GetProperty("amount").GetInt64() == 20000
            && li[0].GetProperty("base_price_money").GetProperty("currency").GetString() == "USD" && li[0].GetProperty("name").GetString() == "Design \"review\" & fixes"
            && li[1].GetProperty("base_price_money").GetProperty("amount").GetInt64() == 3333 && li[2].GetProperty("base_price_money").GetProperty("amount").GetInt64() == 1500);
        var taxes = order.GetProperty("taxes");
        Check("square order: each tax by name and rate, applied to the work lines only",
            taxes.GetArrayLength() == 2 && taxes[0].GetProperty("name").GetString() == "GST" && taxes[0].GetProperty("percentage").GetString() == "5" && taxes[1].GetProperty("percentage").GetString() == "7"
            && taxes[0].GetProperty("scope").GetString() == "LINE_ITEM" && li[0].GetProperty("applied_taxes").GetArrayLength() == 2 && li[1].GetProperty("applied_taxes").GetArrayLength() == 2
            && !li[2].TryGetProperty("applied_taxes", out _));
        var disc = order.GetProperty("discounts");
        Check("square order: the discount as a fixed amount on the work lines only",
            disc.GetArrayLength() == 1 && disc[0].GetProperty("amount_money").GetProperty("amount").GetInt64() == SquareSync.ToMinor(lf.DiscountAmount, "USD") && disc[0].GetProperty("scope").GetString() == "LINE_ITEM"
            && li[0].GetProperty("applied_discounts").GetArrayLength() == 1 && !li[2].TryGetProperty("applied_discounts", out _));
        var flat = J(SquareSync.OrderBody(lf, "L1", "C1", true, "key-2")).GetProperty("order");
        var flatLines = flat.GetProperty("line_items");
        long flatSum = 0; foreach (var l in flatLines.EnumerateArray()) flatSum += l.GetProperty("base_price_money").GetProperty("amount").GetInt64();
        Check("square order: taxes as fixed lines add up to exactly Tempo's total",
            !flat.TryGetProperty("taxes", out _) && flatLines.GetArrayLength() == 5 && flatSum - SquareSync.ToMinor(lf.DiscountAmount, "USD") == SquareSync.ToMinor(lf.GrandTotal, "USD"), $"{flatSum} vs {lf.GrandTotal}");
        var rc = Sq(); rc.ReverseCharge = true; InvoiceService.ApplyTotals(rc);
        Check("square order: reverse charge sends no tax", !J(SquareSync.OrderBody(rc, "L1", "C1", false, "k")).GetProperty("order").TryGetProperty("taxes", out _));
        var longName = Sq(); longName.Lines[0].Description = new string('x', 2000); longName.Lines[1].Description = "";
        var ln = J(SquareSync.OrderBody(longName, "L1", "C1", false, "k")).GetProperty("order").GetProperty("line_items");
        Check("square order: line names are trimmed to Square's limit and never empty", ln[0].GetProperty("name").GetString()!.Length <= 512 && ln[1].GetProperty("name").GetString()!.Length > 0);

        // invoice
        var ib = J(SquareSync.InvoiceBody(ok, "L1", "O1", "C1", "ik", new DateTime(2026, 11, 18)));
        var iv = ib.GetProperty("invoice");
        var pr = iv.GetProperty("payment_requests")[0];
        Check("square invoice: order, customer, number, emailed, balance due on the due date",
            ib.GetProperty("idempotency_key").GetString() == "ik" && iv.GetProperty("location_id").GetString() == "L1" && iv.GetProperty("order_id").GetString() == "O1"
            && iv.GetProperty("primary_recipient").GetProperty("customer_id").GetString() == "C1" && iv.GetProperty("invoice_number").GetString() == "INV-2026-041"
            && iv.GetProperty("delivery_method").GetString() == "EMAIL" && pr.GetProperty("request_type").GetString() == "BALANCE" && pr.GetProperty("due_date").GetString() == "2026-12-01");
        var late = J(SquareSync.InvoiceBody(ok, "L1", "O1", "C1", "ik", new DateTime(2026, 12, 20))).GetProperty("invoice").GetProperty("payment_requests")[0];
        Check("square invoice: a due date already past becomes today", late.GetProperty("due_date").GetString() == "2026-12-20");
        var gbp = J(SquareSync.InvoiceBody(Sq("GBP"), "L1", "O1", "C1", "ik", new DateTime(2026, 11, 18))).GetProperty("invoice").GetProperty("accepted_payment_methods");
        var usd = iv.GetProperty("accepted_payment_methods");
        Check("square invoice: card payments on, bank transfer only where Square offers it (US)",
            usd.GetProperty("card").GetBoolean() && usd.GetProperty("bank_account").GetBoolean() && gbp.GetProperty("card").GetBoolean() && !gbp.GetProperty("bank_account").GetBoolean());

        // customers
        Check("square customer: search by exact email", J(SquareSync.CustomerSearchBody(" ap@hashmark.example ")).GetProperty("query").GetProperty("filter").GetProperty("email_address").GetProperty("exact").GetString() == "ap@hashmark.example");
        var cb = J(SquareSync.CustomerBody(ok, "ck"));
        Check("square customer: created with email, company name and key", cb.GetProperty("email_address").GetString() == "ap@hashmark.example" && cb.GetProperty("company_name").GetString() == "Hashmark Studio" && cb.GetProperty("idempotency_key").GetString() == "ck");
        Check("square customer: id from a search or a create, null otherwise",
            SquareSync.CustomerId("{\"customers\":[{\"id\":\"C1\"}]}") == "C1" && SquareSync.CustomerId("{\"customer\":{\"id\":\"C2\"}}") == "C2" && SquareSync.CustomerId("{}") == null && SquareSync.CustomerId("[") == null);

        // responses
        var po = SquareSync.ParseOrder("{\"order\":{\"id\":\"O1\",\"total_money\":{\"amount\":26040,\"currency\":\"USD\"}}}");
        Check("square: reads the order's id and total", po is { Id: "O1", TotalMinor: 26040, Currency: "USD" } && SquareSync.ParseOrder("{}") == null);
        var loc = SquareSync.ParseLocation("{\"location\":{\"id\":\"L1\",\"currency\":\"USD\",\"status\":\"ACTIVE\"}}");
        Check("square: reads the main location", loc is { Id: "L1", Currency: "USD", Status: "ACTIVE" } && SquareSync.ParseLocation("nope") == null);
        var invJson = "{\"invoice\":{\"id\":\"inv:0-abc\",\"version\":3,\"status\":\"PARTIALLY_PAID\",\"public_url\":\"https://squareup.com/pay-invoice/inv:0-abc\",\"payment_requests\":["
                    + "{\"computed_amount_money\":{\"amount\":10000,\"currency\":\"USD\"},\"total_completed_amount_money\":{\"amount\":10000,\"currency\":\"USD\"}},"
                    + "{\"computed_amount_money\":{\"amount\":16040,\"currency\":\"USD\"},\"total_completed_amount_money\":{\"amount\":2500,\"currency\":\"USD\"}}]}}";
        var st = SquareSync.ParseInvoice(invJson);
        Check("square: reads an invoice's status, version, link and paid amount", st is { Id: "inv:0-abc", Version: 3, Status: "PARTIALLY_PAID", PaidMinor: 12500, DueMinor: 26040, Currency: "USD" }
            && st.PublicUrl == "https://squareup.com/pay-invoice/inv:0-abc" && SquareSync.ParseInvoice("{\"invoice\":{}}") == null);

        // payments back into Tempo
        var linked = Sq(); linked.SquareInvoiceId = "inv:0-abc";
        var t1 = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var s1 = new SquareInvoiceState("inv:0-abc", "PARTIALLY_PAID", 3, 10000, SquareSync.ToMinor(linked.GrandTotal, "USD"), "USD", "", "INV-2026-041", "L1", "ap@hashmark.example");
        Check("square sync: a partial payment is recorded once", SquareSync.Apply(linked, s1, t1) && linked.AmountPaid == 100m && linked.Status == "Issued" && linked.SquareStatus == "PARTIALLY_PAID"
            && !SquareSync.Apply(linked, s1, t1) && linked.Payments.Count == 1 && linked.Payments[0].Note == "Square");
        var s2 = s1 with { Status = "PAID", Version = 4, PaidMinor = SquareSync.ToMinor(linked.GrandTotal, "USD") };
        Check("square sync: the rest is recorded and the invoice marked paid", SquareSync.Apply(linked, s2, t1.AddDays(1)) && linked.Status == "Paid" && linked.Balance == 0 && linked.Payments.Count == 2
            && linked.PaidUtc == t1.AddDays(1) && linked.SquareVersion == 4);
        var other = Sq(); other.SquareInvoiceId = "inv:other";
        var eur = Sq(); eur.SquareInvoiceId = "inv:0-abc"; eur.Currency = "EUR";
        var voided = Sq(); voided.SquareInvoiceId = "inv:0-abc"; voided.Status = "Void";
        Check("square sync: never applies another invoice's, another currency's or a voided invoice's payments",
            !SquareSync.Apply(other, s1, t1) && other.Payments.Count == 0 && !SquareSync.Apply(eur, s1, t1) && eur.Payments.Count == 0 && !SquareSync.Apply(voided, s1, t1) && voided.Payments.Count == 0);
        var refunded = Sq(); refunded.SquareInvoiceId = "inv:0-abc";
        SquareSync.Apply(refunded, s1, t1);
        Check("square sync: a smaller paid amount later (refund) never removes payments", !SquareSync.Apply(refunded, s1 with { Status = "REFUNDED", PaidMinor = 0 }, t1) && refunded.AmountPaid == 100m && refunded.SquareStatus == "REFUNDED");

        // review hardening: a Square invoice must really be this invoice before Tempo trusts it
        var stray = Sq(); stray.SquareInvoiceId = "inv:0-abc"; stray.Number = "INV-2026-999";
        Check("square sync: an invoice whose number doesn't match Square's is never paid from it", !SquareSync.Apply(stray, s1, t1) && stray.Payments.Count == 0);
        var noCur = Sq(); noCur.SquareInvoiceId = "inv:0-abc";
        Check("square sync: a Square answer without a currency is ignored", !SquareSync.Apply(noCur, s1 with { Currency = "" }, t1) && noCur.Payments.Count == 0);
        Check("square: belongs only with the same number and (when known) the same location",
            SquareSync.Belongs(Sq(), s1, "L1") && SquareSync.Belongs(Sq(), s1, null) && !SquareSync.Belongs(Sq(), s1, "L2") && !SquareSync.Belongs(stray, s1, "L1"));
        var manual = Sq(); manual.SquareInvoiceId = "inv:0-abc"; manual.Payments.Add(new Payment { Amount = 50m, Note = "cash" });
        SquareSync.Apply(manual, s1 with { Status = "PAID", PaidMinor = SquareSync.ToMinor(manual.GrandTotal, "USD") }, t1);
        Check("square sync: never records more than the balance (paid by hand and on Square)", manual.AmountPaid == manual.GrandTotal && manual.Status == "Paid", manual.AmountPaid.ToString());
        var full = SquareSync.ParseInvoice("{\"invoice\":{\"id\":\"inv:1\",\"version\":1,\"status\":\"UNPAID\",\"invoice_number\":\"INV-7\",\"location_id\":\"L1\",\"primary_recipient\":{\"customer_id\":\"C1\",\"email_address\":\"x@y.example\"},\"payment_requests\":[]}}");
        Check("square: reads the invoice's number, location and recipient", full is { Number: "INV-7", LocationId: "L1", Email: "x@y.example" });
        var many = SquareSync.ParseInvoiceList("{\"invoices\":[{\"id\":\"inv:1\",\"status\":\"UNPAID\",\"invoice_number\":\"INV-7\"},{\"id\":\"inv:2\",\"status\":\"DRAFT\",\"invoice_number\":\"INV-8\"},{\"bad\":1}]}");
        Check("square: reads a list of invoices, skipping broken ones", many.Count == 2 && many[1].Status == "DRAFT" && SquareSync.ParseInvoiceList("x").Count == 0);
        var isb = J(SquareSync.InvoiceSearchBody("L1", "C1")).GetProperty("query").GetProperty("filter");
        Check("square: searches the location's invoices for this customer", isb.GetProperty("location_ids")[0].GetString() == "L1" && isb.GetProperty("customer_ids")[0].GetString() == "C1");
        Check("square: only plain Square ids are used in addresses",
            SquareSync.IsSafeId("inv:0-ChAbc_12") && !SquareSync.IsSafeId("") && !SquareSync.IsSafeId("../v2/x") && !SquareSync.IsSafeId("a?b") && !SquareSync.IsSafeId(new string('a', 300)));
        Check("square: the invoice request key changes with the due date it sends", SquareSync.InvoiceKey(ok, fp, "2026-12-01") != SquareSync.InvoiceKey(ok, fp, "2026-12-20") && SquareSync.InvoiceKey(ok, fp, "2026-12-01").Length <= 128);
        var mine = Sq(); mine.SquareInvoiceId = "inv:0-abc"; mine.SquareStatus = "UNPAID"; mine.SquareVersion = 2; mine.SquareFingerprint = fp; mine.SquareAttempt = 1;
        var theirs = Sq(); theirs.Note = "edited on the laptop";
        SquareSync.KeepLink(mine, theirs);
        var theirs2 = Sq(); theirs2.SquareInvoiceId = "inv:new"; SquareSync.KeepLink(mine, theirs2);
        Check("sync: an edit from another PC never drops this invoice's Square link", theirs.SquareInvoiceId == "inv:0-abc" && theirs.SquareStatus == "UNPAID" && theirs.SquareVersion == 2 && theirs.SquareAttempt == 1
            && theirs2.SquareInvoiceId == "inv:new");
        var forged = Sq(); forged.SquareInvoiceId = "../../v2/payments"; forged.SquareStatus = "UNPAID"; SquareSync.SanitizeLink(forged);
        var fine = Sq(); fine.SquareInvoiceId = "inv:0-abc"; SquareSync.SanitizeLink(fine);
        var nulls = Sq(); nulls.SquareInvoiceId = null!; nulls.SquareStatus = null!; nulls.SquareFingerprint = null!;
        bool nullOk; try { SquareSync.SanitizeLink(nulls); nullOk = nulls.SquareInvoiceId == "" && nulls.SquareStatus == "" && nulls.SquareFingerprint == ""; } catch { nullOk = false; }
        Check("sync: a synced invoice with empty (null) Square fields is cleaned, not a crash", nullOk);
        Check("sync: a synced Square link that isn't a plain id is dropped", forged.SquareInvoiceId == "" && forged.SquareStatus == "" && fine.SquareInvoiceId == "inv:0-abc");

        Check("square: only Square's own https pages open from Tempo",
            SquareSync.IsSquareUrl("https://squareup.com/pay-invoice/inv:0-abc") && SquareSync.IsSquareUrl("https://app.squareupsandbox.com/pay-invoice/x")
            && !SquareSync.IsSquareUrl("http://squareup.com/pay-invoice/x") && !SquareSync.IsSquareUrl("https://squareup.com.evil.example/x")
            && !SquareSync.IsSquareUrl("https://evilsquareup.com/x") && !SquareSync.IsSquareUrl("javascript:alert(1)"));
    }

    // ---------- phase 11: bring-your-own-key integrations ----------
    static void IntegrationTests()
    {
        // keys: encrypted, separate file, never in the data file
        var sdir = System.IO.Path.Combine(OutDir, "secrets-test");
        if (System.IO.Directory.Exists(sdir)) System.IO.Directory.Delete(sdir, true);
        System.IO.Directory.CreateDirectory(sdir);
        SecretStore.Set(sdir, "stripe", "rk_test_51AbCdEfSECRET");
        SecretStore.Set(sdir, "github", "ghp_SECRET123");
        Check("keys: saved and read back", SecretStore.Get(sdir, "stripe") == "rk_test_51AbCdEfSECRET" && SecretStore.Get(sdir, "github") == "ghp_SECRET123" && SecretStore.Get(sdir, "jira") == null);
        var raw = System.IO.File.ReadAllBytes(SecretStore.PathIn(sdir));
        Check("keys: encrypted on disk (no key text in the file)", !System.Text.Encoding.UTF8.GetString(raw).Contains("SECRET") && !System.Text.Encoding.Unicode.GetString(raw).Contains("SECRET"));
        SecretStore.Set(sdir, "github", null);
        Check("keys: removing one keeps the others", SecretStore.Get(sdir, "github") == null && SecretStore.Get(sdir, "stripe") == "rk_test_51AbCdEfSECRET");
        System.IO.File.WriteAllText(SecretStore.PathIn(sdir), "garbage");
        Check("keys: a damaged file reads as 'no keys', no crash", SecretStore.Get(sdir, "stripe") == null);
        Check("keys: never part of the data file", !System.Text.Json.JsonSerializer.Serialize(State.Data).Contains("rk_test_"));

        // Stripe payment links
        Check("stripe: recognises payment links only", StripeSync.IsPaymentLink("https://buy.stripe.com/test_abc123") && !StripeSync.IsPaymentLink("https://pay.example.com/x") && !StripeSync.IsPaymentLink("http://buy.stripe.com/x"));
        Check("stripe: invoice number becomes a safe, Tempo-only reference", StripeSync.ReferenceFor("INV-2026/003 (A)") == "tempo_INV-2026-003--A-");
        Check("stripe: link gets the invoice reference, replacing any old one",
            StripeSync.WithReference("https://buy.stripe.com/test_abc", "INV-1") == "https://buy.stripe.com/test_abc?client_reference_id=tempo_INV-1"
            && StripeSync.WithReference("https://buy.stripe.com/test_abc?prefilled_email=a%40b.c&client_reference_id=OLD", "INV-2") == "https://buy.stripe.com/test_abc?prefilled_email=a%40b.c&client_reference_id=tempo_INV-2"
            && StripeSync.WithReference("https://pay.example.com/x", "INV-1") == "https://pay.example.com/x");
        Check("stripe: amounts in cents, whole units for zero-decimal and thousandths for three-decimal currencies",
            StripeSync.FromMinor(123456, "usd") == 1234.56m && StripeSync.FromMinor(5000, "JPY") == 5000m && StripeSync.FromMinor(1000, "KWD") == 1.000m && StripeSync.FromMinor(2500, "bhd") == 2.5m);

        var t0s = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        string Sess(string id, string? reference, long amount, string cur, bool paidOk = true, bool link = true, long at = 0) =>
            $"{{\"id\":\"{id}\",\"client_reference_id\":{(reference == null ? "null" : "\"" + reference + "\"")},\"amount_total\":{amount},\"currency\":\"{cur}\",\"payment_status\":\"{(paidOk ? "paid" : "unpaid")}\",\"status\":\"{(paidOk ? "complete" : "open")}\",\"payment_link\":{(link ? "\"plink_1\"" : "null")},\"created\":{(at == 0 ? t0s : at)}}}";
        var sessions = "{\"data\":[" + string.Join(",",
            Sess("cs_1", "tempo_INV-1", 50000, "usd"),
            Sess("cs_2", "tempo_INV-2", 10000, "usd"),
            Sess("cs_3", "tempo_INV-2", 99900, "usd", paidOk: false),
            Sess("cs_4", null, 7000, "usd"),
            Sess("cs_5", "tempo_INV-3", 2000, "eur"),
            Sess("cs_6", "INV-1", 50000, "usd"),                      // someone else's checkout using a bare number
            Sess("cs_7", "tempo_INV-2", 5000, "usd", link: false),    // not from a payment link (e.g. a website checkout)
            Sess("cs_8", "tempo_INV-2", 5000, "usd", at: t0s - 30 * 86400)) + "]}";   // paid long before the invoice existed
        var paid = StripeSync.ParseSessions(sessions);
        Check("stripe: only paid payment-link checkouts with a Tempo reference count", paid.Select(p => p.SessionId).OrderBy(x => x).SequenceEqual(new[] { "cs_1", "cs_2", "cs_5", "cs_8" }) && paid.First(p => p.SessionId == "cs_1").Amount == 500m && paid.First().PaidUtc == DateTimeOffset.FromUnixTimeSeconds(t0s).UtcDateTime, string.Join(",", paid.Select(p => p.SessionId)));
        Check("stripe: broken JSON gives no payments", StripeSync.ParseSessions("{nope").Count == 0);

        var issued = DateTimeOffset.FromUnixTimeSeconds(t0s).UtcDateTime.AddDays(-2);
        Invoice Inv(string n, decimal total, string cur = "USD") { var i = new Invoice { Number = n, Currency = cur, Status = "Issued", IssuedUtc = issued, PayLink = "https://buy.stripe.com/test_abc" }; i.Lines.Add(new InvoiceLine { Amount = total }); InvoiceService.ApplyTotals(i); return i; }
        var invs = new List<Invoice> { Inv("INV-1", 500m), Inv("INV-2", 300m), Inv("INV-3", 20m, "USD") };
        var changed = StripeSync.Apply(paid, invs);
        Check("stripe: full payment marks the invoice paid", invs[0].Status == "Paid" && invs[0].Balance == 0 && invs[0].Payments.Single().Reference == "cs_1" && invs[0].PaidUtc != null);
        Check("stripe: partial payment is recorded, invoice stays open", invs[1].Status == "Issued" && invs[1].AmountPaid == 100m);
        Check("stripe: a payment in another currency is not applied", invs[2].Payments.Count == 0);
        Check("stripe: the same checkout is never recorded twice", StripeSync.Apply(paid, invs).Count == 0 && invs[1].Payments.Count == 1 && changed.Count == 2);
        Check("stripe: payments from before the invoice was issued are ignored", invs[1].Payments.All(p => p.Reference != "cs_8"));
        var twins = new List<Invoice> { Inv("INV/9", 50m), Inv("INV-9", 50m) };
        var twinPay = new[] { new StripePayment("cs_9", "tempo_INV-9", 50m, "USD", issued.AddDays(1)) };
        Check("stripe: when two invoices share a reference, neither is touched", StripeSync.Apply(twinPay, twins).Count == 0 && twins.All(i => i.Payments.Count == 0));
        var draft = new List<Invoice> { Inv("INV-5", 50m) }; draft[0].Status = "Paid";
        var noLink = new List<Invoice> { Inv("INV-6", 50m) }; noLink[0].PayLink = "https://pay.example.com/6";
        Check("stripe: only open invoices that use a Stripe link are matched",
            StripeSync.Apply(new[] { new StripePayment("cs_10", "tempo_INV-5", 50m, "USD", issued.AddDays(1)) }, draft).Count == 0
            && StripeSync.Apply(new[] { new StripePayment("cs_11", "tempo_INV-6", 50m, "USD", issued.AddDays(1)) }, noLink).Count == 0);
        var longTitle = "[{\"number\":1,\"title\":\"=HYPERLINK(\\\"x\\\")\\nline2\\u0007" + new string('a', 1000) + "\",\"html_url\":\"https://github.com/a/b/issues/1\",\"repository\":{\"full_name\":\"a/b\"}}]";
        var cleaned = WorkItems.ParseGitHub(longTitle).Single().Title;
        Check("tasks: titles from other people are cleaned and shortened", cleaned.Length <= 300 && !cleaned.Contains('\n') && !cleaned.Contains('\a'), cleaned.Length.ToString());

        // your tasks from GitHub / Jira / Asana
        var gh = "[{\"number\":42,\"title\":\"Fix login on Safari\",\"html_url\":\"https://github.com/acme/web/issues/42\",\"repository\":{\"full_name\":\"acme/web\"}}," +
                 "{\"number\":7,\"title\":\"Add dark mode\",\"html_url\":\"https://github.com/acme/app/pull/7\",\"pull_request\":{},\"repository\":{\"full_name\":\"acme/app\"}}]";
        var g = WorkItems.ParseGitHub(gh);
        Check("github: assigned issues and PRs become tasks", g.Count == 2 && g[0].Key == "acme/web#42" && g[0].Title == "Fix login on Safari" && g[0].Source == "GitHub" && g[0].Container == "acme/web");
        var jira = "{\"issues\":[{\"key\":\"WEB-12\",\"fields\":{\"summary\":\"Checkout page copy\",\"project\":{\"name\":\"Website\"}}}]}";
        var j = WorkItems.ParseJira(jira, "https://acme.atlassian.net");
        Check("jira: issues with key, summary and link", j.Count == 1 && j[0].Key == "WEB-12" && j[0].Url == "https://acme.atlassian.net/browse/WEB-12" && j[0].Container == "Website");
        var asana = "{\"data\":[{\"gid\":\"120\",\"name\":\"Draft newsletter\",\"permalink_url\":\"https://app.asana.com/0/1/120\",\"projects\":[{\"name\":\"Marketing\"}]}]}";
        var a = WorkItems.ParseAsana(asana);
        Check("asana: tasks with project name", a.Count == 1 && a[0].Title == "Draft newsletter" && a[0].Container == "Marketing" && a[0].Source == "Asana");
        Check("task parsers: junk gives an empty list", WorkItems.ParseGitHub("{}").Count == 0 && WorkItems.ParseJira("[", "https://a.atlassian.net").Count == 0 && WorkItems.ParseAsana("null").Count == 0);
        Check("jira site: https Atlassian or company sites only, no local network",
            WorkItems.NormalizeJiraSite("acme.atlassian.net") == "https://acme.atlassian.net" && WorkItems.NormalizeJiraSite("https://jira.acme.com/") == "https://jira.acme.com"
            && WorkItems.NormalizeJiraSite("http://acme.atlassian.net") == null && WorkItems.NormalizeJiraSite("https://192.168.0.5") == null);
        Check("task description: key and title", WorkItems.Describe(g[0]) == "acme/web#42 Fix login on Safari" && WorkItems.Describe(j[0]) == "WEB-12 Checkout page copy" && WorkItems.Describe(a[0]) == "Draft newsletter");
    }

    // ---------- phase 10: live calendar, one-click update, problem reports ----------
    static void PolishTests()
    {
        Check("calendar url: https and webcal are accepted, webcal becomes https",
            CalendarFeeds.Normalize("https://calendar.google.com/calendar/ical/x/private-abc/basic.ics") == "https://calendar.google.com/calendar/ical/x/private-abc/basic.ics"
            && CalendarFeeds.Normalize(" webcal://p01-caldav.icloud.com/published/2/abc ") == "https://p01-caldav.icloud.com/published/2/abc");
        Check("calendar url: anything else is refused",
            CalendarFeeds.Normalize("http://example.com/cal.ics") == null && CalendarFeeds.Normalize("file:///C:/x.ics") == null && CalendarFeeds.Normalize("not a url") == null && CalendarFeeds.Normalize("https://localhost/cal.ics") == null);
        Check("calendar url: private and local network addresses are refused",
            new[] { "https://192.168.1.10/c.ics", "https://10.0.0.5/c.ics", "https://172.16.0.1/c.ics", "https://169.254.169.254/latest", "https://127.0.0.1/c.ics", "https://[::1]/c.ics", "https://[fe80::1]/c.ics" }.All(u => CalendarFeeds.Normalize(u) == null)
            && CalendarFeeds.Normalize("https://8.8.8.8/c.ics") != null);
        var f1 = new CalendarFeed { Id = "abc123", Url = "https://a.example/x.ics" };
        Check("calendar cache: one file per feed, inside the data folder", CalendarFeeds.CacheFile(@"C:\data", f1) == @"C:\data\calendars\abc123.ics");

        var rel = "{\"tag_name\":\"v2.7.0\",\"assets\":[" +
                  "{\"name\":\"Tempo.exe\",\"browser_download_url\":\"https://github.com/ColeDonMedia/tempo-time-tracker/releases/download/v2.7.0/Tempo.exe\"}," +
                  "{\"name\":\"Tempo-Setup-2.7.0.exe\",\"browser_download_url\":\"https://github.com/ColeDonMedia/tempo-time-tracker/releases/download/v2.7.0/Tempo-Setup-2.7.0.exe\"}," +
                  "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/ColeDonMedia/tempo-time-tracker/releases/download/v2.7.0/SHA256SUMS.txt\"}]}";
        var a = UpdateService.ParseAssets(rel);
        Check("update: finds the installer and the checksum file", a.InstallerName == "Tempo-Setup-2.7.0.exe" && a.InstallerUrl!.EndsWith("/Tempo-Setup-2.7.0.exe") && a.SumsUrl!.EndsWith("/SHA256SUMS.txt"));
        Check("update: download links from anywhere else are ignored",
            UpdateService.ParseAssets(rel.Replace("https://github.com/ColeDonMedia/tempo-time-tracker/releases/download/v2.7.0/Tempo-Setup", "https://evil.example/Tempo-Setup")).InstallerUrl == null
            && !UpdateService.IsTrustedDownload("https://github.com/ColeDonMedia/tempo-time-tracker/releases/tag/v2.7.0")
            && UpdateService.IsTrustedDownload("https://github.com/ColeDonMedia/tempo-time-tracker/releases/download/v2.7.0/Tempo-Setup-2.7.0.exe"));
        var unsigned = System.IO.Path.Combine(OutDir, "unsigned.exe");
        System.IO.File.WriteAllBytes(unsigned, new byte[] { 0x4D, 0x5A, 0, 0 });
        Check("update: an unsigned download never counts as validly signed", !Authenticode.IsValid(unsigned));
        var sums = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  Tempo.exe\n0000000000000000000000000000000000000000000000000000000000000001  Tempo-Setup-2.7.0.exe\n";
        Check("update: reads the expected hash for a file", UpdateService.ExpectedHash(sums, "Tempo-Setup-2.7.0.exe") == "0000000000000000000000000000000000000000000000000000000000000001" && UpdateService.ExpectedHash(sums, "Other.exe") == null);

        // the log grows at the end, so the newest error is last
        var log = new string('x', 20000) + "\n11/18/2026 10:00:00\nSystem.IO.IOException: The file C:\\Users\\sam\\AppData\\Local\\TempoStudio\\tempo-data.json is locked\n";
        var url = Problem.IssueUrl("2.7.0", "Windows 11 (26100)", log, @"C:\Users\sam");
        var body = Uri.UnescapeDataString(url[(url.IndexOf("body=") + 5)..]);
        Check("problem report: pre-filled GitHub issue with version and Windows build", url.StartsWith("https://github.com/ColeDonMedia/tempo-time-tracker/issues/new?") && body.Contains("2.7.0") && body.Contains("26100"));
        var url2 = Problem.IssueUrl("2.7.0", "Windows", "error on SAMS-LAPTOP for sam in D:\\Sync\\Tempo", @"C:\Users\sam", "sam", "SAMS-LAPTOP", @"D:\Sync\Tempo");
        var body2 = Uri.UnescapeDataString(url2[(url2.IndexOf("body=") + 5)..]);
        Check("problem report: user name, PC name and sync folder are hidden too", !body2.Contains("SAMS-LAPTOP") && !body2.Contains(@"D:\Sync\Tempo") && !body2.Contains(" sam ") && body2.Contains("%COMPUTERNAME%"), body2);
        Check("problem report: your user folder is hidden and the link stays short", !body.Contains(@"C:\Users\sam") && body.Contains(@"%USERPROFILE%\AppData") && url.Length < 7000, url.Length.ToString());
    }

    // ---------- phase 9: folder sync between devices / team members ----------
    static void SyncTests()
    {
        var t = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        Workspace NewWs() { var w = new Workspace(); w.Projects.Add(new Project { Id = "p1", Name = "Shared", HourlyRate = 100 }); return w; }
        var a = NewWs(); var b = NewWs();
        var sa = new SyncState(); var sb = new SyncState();

        // A logs an entry, publishes; B picks it up
        a.Sessions.Add(new Session { Id = "s1", ProjectId = "p1", Task = "From A", DurationMilliseconds = 3_600_000, StartedAtUtc = t, EndedAtUtc = t.AddHours(1), Member = "Ana" });
        var fa = Sync.Step(a, sa, Array.Empty<SyncFile>(), "devA", "Ana", t).Own;
        var rb = Sync.Step(b, sb, new[] { fa }, "devB", "Ben", t.AddMinutes(1));
        Check("sync: a new entry on one device appears on the other", b.Sessions.Any(s => s.Id == "s1" && s.Task == "From A" && s.Member == "Ana") && rb.Received >= 1, $"received {rb.Received}");
        Check("sync: records both devices already share aren't duplicated", b.Projects.Count == 1);

        // B edits it later; A gets the edit
        b.Sessions.First(s => s.Id == "s1").Task = "Edited by B";
        var fb = Sync.Step(b, sb, new[] { fa }, "devB", "Ben", t.AddMinutes(5)).Own;
        Sync.Step(a, sa, new[] { fb }, "devA", "Ana", t.AddMinutes(6));
        Check("sync: the newest edit wins", a.Sessions.Single(s => s.Id == "s1").Task == "Edited by B");

        // A changes it again after that: A's newer edit must survive B's older copy
        a.Sessions.First(s => s.Id == "s1").Task = "A again";
        fa = Sync.Step(a, sa, new[] { fb }, "devA", "Ana", t.AddMinutes(10)).Own;
        Check("sync: an older remote copy never overwrites a newer local edit", a.Sessions.Single(s => s.Id == "s1").Task == "A again");
        Sync.Step(b, sb, new[] { fa }, "devB", "Ben", t.AddMinutes(11));
        Check("sync: …and it reaches the other device", b.Sessions.Single(s => s.Id == "s1").Task == "A again");

        // B deletes it; the deletion reaches A and isn't undone by A's old copy
        b.Sessions.RemoveAll(s => s.Id == "s1");
        fb = Sync.Step(b, sb, new[] { fa }, "devB", "Ben", t.AddMinutes(20)).Own;
        Check("sync: a deletion isn't brought back by the other device's older copy", b.Sessions.All(s => s.Id != "s1"));
        var ra = Sync.Step(a, sa, new[] { fb }, "devA", "Ana", t.AddMinutes(21));
        Check("sync: deletions travel too", a.Sessions.All(s => s.Id != "s1") && ra.Removed == 1);

        // invoice numbers never go backwards
        a.InvoiceCounter = 3; b.InvoiceCounter = 7;
        fb = Sync.Step(b, sb, new[] { fa }, "devB", "Ben", t.AddMinutes(30)).Own;
        Sync.Step(a, sa, new[] { fb }, "devA", "Ana", t.AddMinutes(31));
        Check("sync: invoice counters take the highest, so numbers aren't reused", a.InvoiceCounter == 7);

        // a stray file from this same device is ignored
        var self = new SyncFile { DeviceId = "devA", Items = { new SyncItem { Kind = "Session", Id = "zz", StampUtc = t.AddDays(1), Data = System.Text.Json.JsonSerializer.SerializeToElement(new Session { Id = "zz" }) } } };
        Sync.Step(a, sa, new[] { self }, "devA", "Ana", t.AddMinutes(40));
        Check("sync: this device's own file isn't read back as someone else's", a.Sessions.All(s => s.Id != "zz"));

        // ---- hardening: other devices' files are untrusted ----
        var h = NewWs(); var sh = new SyncState();
        var evilInv = new Invoice { Id = "i1", Number = "INV-1", FilePath = @"\\attacker\share\x.pdf", LogoPath = @"\\attacker\share\logo.png", PayLink = "javascript:alert(1)" };
        var evilExp = new Expense { Id = "e1", ProjectId = "p1", ReceiptPath = @"C:\Users\me\Documents\secret.pdf" };
        SyncItem It(string kind, object o, string id, DateTime stamp) => new() { Kind = kind, Id = id, StampUtc = stamp, Data = System.Text.Json.JsonSerializer.SerializeToElement(o, o.GetType()) };
        var evil = new SyncFile { DeviceId = "devX", InvoiceCounter = int.MaxValue, Items = { It("Invoice", evilInv, "i1", t), It("Expense", evilExp, "e1", t),
            It("Session", new Session { Id = "future", ProjectId = "p1" }, "future", t.AddYears(50)) } };
        Sync.Step(h, sh, new[] { evil }, "devH", "Hal", t.AddMinutes(1));
        var gotInv = h.Invoices.Single(); var gotExp = h.Expenses.Single();
        Check("sync hardening: file paths from other devices are dropped", gotInv.FilePath == "" && gotInv.LogoPath == "" && gotExp.ReceiptPath == "");
        Check("sync hardening: unsafe pay links are dropped", gotInv.PayLink == "");
        Check("sync hardening: stamps from the future are refused", h.Sessions.All(s => s.Id != "future"));
        Check("sync hardening: absurd invoice counters are capped", h.InvoiceCounter <= Sync.MaxCounter);

        var m = NewWs(); var sm = new SyncState();
        for (int i = 0; i < 40; i++) m.Sessions.Add(new Session { Id = "m" + i, ProjectId = "p1", StartedAtUtc = t, EndedAtUtc = t.AddMinutes(1), DurationMilliseconds = 60_000 });
        Sync.Step(m, sm, Array.Empty<SyncFile>(), "devM", "Mo", t);
        var wipe = new SyncFile { DeviceId = "devW", Deleted = m.Sessions.Select(s => new Tombstone { Key = "Session:" + s.Id, StampUtc = t.AddMinutes(5) }).ToList() };
        var trashed = new List<string>();
        var mr = Sync.Step(m, sm, new[] { wipe }, "devM", "Mo", t.AddMinutes(6), (k, o) => trashed.Add(k), allowMassDelete: false);
        Check("sync hardening: a mass delete from another device is held back for you to confirm", m.Sessions.Count == 40 && mr.BlockedRemovals == 40 && mr.Removed == 0);
        mr = Sync.Step(m, sm, new[] { wipe }, "devM", "Mo", t.AddMinutes(7), (k, o) => trashed.Add(k), allowMassDelete: true);
        Check("sync hardening: confirmed sync deletions go to Recently deleted", m.Sessions.Count == 0 && mr.Removed == 40 && trashed.Count == 40 && trashed.All(k => k == "Session"));

        // the real thing: two devices, one shared folder, state saved to disk between runs
        var shared = System.IO.Path.Combine(OutDir, "sync-shared");
        if (System.IO.Directory.Exists(shared)) System.IO.Directory.Delete(shared, true);
        System.IO.Directory.CreateDirectory(shared);
        string stA = System.IO.Path.Combine(OutDir, "sync-state-a.json"), stB = System.IO.Path.Combine(OutDir, "sync-state-b.json");
        foreach (var f in new[] { stA, stB }) if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
        var da = NewWs(); var db = NewWs();
        da.Sessions.Add(new Session { Id = "d1", ProjectId = "p1", Task = "Disk A", DurationMilliseconds = 60_000, StartedAtUtc = t, EndedAtUtc = t.AddMinutes(1) });
        SyncService.RunWith(da, shared, stA, "devA", "Ana", t);
        SyncService.RunWith(db, shared, stB, "devB", "Ben", t.AddMinutes(1));
        Check("sync folder: one file per device, entries arrive", System.IO.Directory.GetFiles(shared, "tempo-sync-*.json").Length == 2 && db.Sessions.Any(s => s.Id == "d1"));
        SyncService.RunWith(da, shared, stA, "devA", "Ana", t.AddMinutes(2));   // nothing changed on A
        db.Sessions.First(s => s.Id == "d1").Task = "Disk B edit";
        SyncService.RunWith(db, shared, stB, "devB", "Ben", t.AddMinutes(3));
        SyncService.RunWith(da, shared, stA, "devA", "Ana", t.AddMinutes(4));
        Check("sync folder: saved state keeps old stamps, so the other device's later edit wins", da.Sessions.Single(s => s.Id == "d1").Task == "Disk B edit");
        System.IO.File.WriteAllText(System.IO.Path.Combine(shared, "tempo-sync-junk.json"), "{ not json");
        Check("sync folder: a broken file from another device doesn't stop the sync", SyncService.RunWith(da, shared, stA, "devA", "Ana", t.AddMinutes(5)) != null);

        // team: reports group by member
        State.Data = new Workspace(); State.Data.Projects.Add(new Project { Id = "p1", Name = "P", HourlyRate = 10 });
        State.Data.Sessions.Add(new Session { ProjectId = "p1", Member = "Ana", DurationMilliseconds = 3_600_000, StartedAtUtc = t, EndedAtUtc = t.AddHours(1), HourlyRate = 10 });
        State.Data.Sessions.Add(new Session { ProjectId = "p1", Member = "Ben", DurationMilliseconds = 7_200_000, StartedAtUtc = t, EndedAtUtc = t.AddHours(2), HourlyRate = 10 });
        State.Data.Sessions.Add(new Session { ProjectId = "p1", Member = "", DurationMilliseconds = 1_800_000, StartedAtUtc = t, EndedAtUtc = t.AddHours(0.5), HourlyRate = 10 });
        var byMember = ReportEngine.Compute(new ReportFilter { GroupBy = "Member" });
        Check("team: reports group hours by member", byMember.Rows.Count == 3 && byMember.Rows[0].Label == "Ben" && byMember.Rows[0].Hours == 2m, string.Join(",", byMember.Rows.Select(r => r.Label + "=" + r.Hours)));
        State.Data.Settings.MemberName = "Ana";
        State.Data.Timer = new TimerSnapshot { ProjectId = "p1", StartedAtUtc = DateTime.UtcNow.AddMinutes(-5), AccumulatedMilliseconds = 300_000 };
        var stopped = State.StopTimer();
        Check("team: entries are stamped with this person's name", stopped?.Member == "Ana");
    }

    // ---------- phase 8: profit, currencies, taxes, email ----------
    static void MoneyTests()
    {
        State.Data = new Workspace(); State.Normalize();
        var st = State.Data.Settings;
        st.CurrencyCode = "USD"; st.DefaultCostRate = 30m; st.ExchangeRates["EUR"] = 1.10m;
        var eu = new Client { Name = "Berlin GmbH", Currency = "EUR", TaxId = "DE123456789", ReverseCharge = true };
        var us = new Client { Name = "Austin Co" };
        var jp = new Client { Name = "Tokyo KK", Currency = "JPY" };
        State.Data.Clients.AddRange(new[] { eu, us, jp });
        var pe = new Project { Id = "pe", Name = "EU work", ClientId = eu.Id, HourlyRate = 100 };           // EUR, default cost 30
        var pu = new Project { Id = "pu", Name = "US work", ClientId = us.Id, HourlyRate = 80, CostRate = 50 };
        var pj = new Project { Id = "pj", Name = "JP work", ClientId = jp.Id, HourlyRate = 10000 };
        State.Data.Projects.AddRange(new[] { pe, pu, pj });

        Check("currency of a project comes from its client, else the base", Money.CurrencyOf(pe) == "EUR" && Money.CurrencyOf(pu) == "USD" && Money.CurrencyOf(null) == "USD");
        Check("conversion uses the rate table, base is 1:1, unknown is null", Money.ToBase(100m, "EUR") == 110m && Money.ToBase(100m, "USD") == 100m && Money.Rate("JPY") == null && Money.Rate("usd") == 1m);
        var (rates, bad) = Money.ParseRates("EUR = 1.08\n gbp=1.27 \n\nJPY 0.0067\nXX = 2\nCAD = -1\nchf = abc");
        Check("rate box: reads 'EUR = 1.08' lines, upper-cases codes, reports the bad ones",
            rates.Count == 3 && rates["EUR"] == 1.08m && rates["GBP"] == 1.27m && rates["JPY"] == 0.0067m && bad.Count == 3, $"{rates.Count} rates, bad: {string.Join(" / ", bad)}");
        Check("rate box: formats back in a stable order", Money.FormatRates(rates) == "EUR = 1.08" + Environment.NewLine + "GBP = 1.27" + Environment.NewLine + "JPY = 0.0067");
        Check("cost rate: project's own, else the default", Money.CostRate(pu) == 50m && Money.CostRate(pe) == 30m);

        State.Data.Sessions.Add(S("pe", 2, 100));
        State.Data.Sessions.Add(S("pu", 1, 80));
        State.Data.Sessions.Add(S("pu", 1, 80, billable: false));
        State.Data.Sessions.Add(S("pj", 1, 10000));
        State.Data.Expenses.Add(new Expense { ProjectId = "pu", Date = State.TodayLocal, Amount = 20m, Billable = false });
        State.Data.Expenses.Add(new Expense { ProjectId = "pu", Date = State.TodayLocal, Amount = 99m, Billable = true });
        var r = ReportEngine.Compute(new ReportFilter());
        var eRow = r.Rows.First(x => x.Key == "pe"); var uRow = r.Rows.First(x => x.Key == "pu");
        Check("report earnings are converted to the base currency", eRow.Earned == 220m && uRow.Earned == 80m, $"{eRow.Earned}/{uRow.Earned}");
        Check("cost counts every hour (billable or not) at the cost rate", eRow.Cost == 60m && uRow.Cost == 100m && eRow.Profit == 160m && uRow.Profit == -20m, $"{eRow.Cost}/{uRow.Cost}");
        Check("profit subtracts non-billable expenses only", r.CostExpenses == 20m && r.Profit == r.Earned - r.Cost - 20m);
        Check("currencies without a rate are flagged", r.MissingRates.SetEquals(new[] { "JPY" }));

        var inv = new Invoice { Currency = "CAD" };
        inv.Lines.Add(new InvoiceLine { Description = "Work", Amount = 1000m });
        inv.DiscountPercent = 10m;
        inv.Taxes.Add(new TaxLine { Label = "GST", Percent = 5m });
        inv.Taxes.Add(new TaxLine { Label = "PST", Percent = 7m });
        InvoiceService.ApplyTotals(inv);
        Check("several taxes add up on the discounted amount", inv.TaxAmount == 108m && inv.Total == 1008m && inv.TaxPercent == 12m, $"{inv.TaxAmount}/{inv.Total}");
        inv.ReverseCharge = true; InvoiceService.ApplyTotals(inv);
        Check("reverse charge: no tax at all", inv.TaxAmount == 0m && inv.Total == 900m);
        var single = new Invoice { TaxPercent = 8.5m }; single.Lines.Add(new InvoiceLine { Amount = 200m }); InvoiceService.ApplyTotals(single);
        Check("old single-tax invoices still work", single.TaxAmount == 17m && single.Total == 217m);

        var day = State.TodayLocal;
        Invoice Issued(string cur, decimal sub, List<TaxLine> taxes, bool rc = false, string status = "Issued", string kind = "Invoice", int daysAgo = 1)
        {
            var i = new Invoice { Currency = cur, Status = status, Kind = kind, IssuedUtc = State.ToUtc(day.AddDays(-daysAgo).AddHours(12)), ReverseCharge = rc, Taxes = taxes };
            i.Lines.Add(new InvoiceLine { Amount = sub }); InvoiceService.ApplyTotals(i); State.Data.Invoices.Add(i); return i;
        }
        Issued("CAD", 1000m, new() { new() { Label = "GST", Percent = 5m }, new() { Label = "PST", Percent = 7m } });
        Issued("CAD", 500m, new() { new() { Label = "GST", Percent = 5m } });
        Issued("EUR", 800m, new(), rc: true);
        Issued("CAD", 999m, new() { new() { Label = "GST", Percent = 5m } }, status: "Void");
        Issued("CAD", 999m, new() { new() { Label = "GST", Percent = 5m } }, kind: "Quote");
        Issued("CAD", 999m, new() { new() { Label = "GST", Percent = 5m } }, daysAgo: 400);
        var tax = ReportEngine.TaxReport(day.AddDays(-30), day);
        var gst = tax.FirstOrDefault(t => t.Label == "GST"); var pst = tax.FirstOrDefault(t => t.Label == "PST"); var rcRow = tax.FirstOrDefault(t => t.Label == "Reverse charge");
        Check("tax report: totals per tax, skipping void, quotes and other dates",
            gst != null && gst.Base == 1500m && gst.Tax == 75m && gst.Invoices == 2 && pst != null && pst.Base == 1000m && pst.Tax == 70m && rcRow != null && rcRow.Base == 800m && rcRow.Tax == 0m && rcRow.Currency == "EUR" && tax.Count == 3,
            string.Join(" | ", tax));

        var due = new Invoice { Number = "INV-9", ClientName = "Austin Co", ClientEmail = "ap@austin.example", Currency = "USD", StudioName = "Sam's Studio", PayLink = "https://pay.example.com/9",
                                IssuedUtc = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), DueUtc = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc) };
        due.Lines.Add(new InvoiceLine { Amount = 500m }); InvoiceService.ApplyTotals(due);
        due.Payments.Add(new Payment { Amount = 100m });
        var (to, subj, body) = InvoiceEmail.Compose(due, false, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        Check("invoice email: addressed to the client with number, balance, due date and pay link",
            to == "ap@austin.example" && subj.Contains("INV-9") && subj.Contains("Sam's Studio") && body.Contains(Fmt.Money(400m, "USD")) && body.Contains("https://pay.example.com/9") && body.Contains(Fmt.Date(State.ToLocal(due.DueUtc))), (subj + " // " + body + " // want " + Fmt.Money(400m, "USD") + " " + Fmt.Date(State.ToLocal(due.DueUtc))).Replace("\r", "").Replace("\n", " ¶ "));
        var (_, rs, rb) = InvoiceEmail.Compose(due, true, new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
        Check("reminder email says how overdue it is", rs.Contains("INV-9") && rb.Contains("10"), rs + " // " + rb);
    }

    // ---------- phase 7: timeline, focus, activity ----------
    static void TrackingTests()
    {
        var t0 = new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);
        Session At(double fromH, double toH, string pid = "p1") => new() { ProjectId = pid, StartedAtUtc = t0.AddHours(fromH), EndedAtUtc = t0.AddHours(toH), DurationMilliseconds = (long)((toH - fromH) * 3_600_000) };
        var a = At(0, 2); var b = At(1.5, 3); var c = At(3, 4); var d = At(6, 7);
        var ov = Timeline.Overlaps(new[] { c, a, d, b });
        Check("overlaps: finds entries that share time, not ones that just touch", ov.Count == 1 && ov[0].A == a && ov[0].B == b && Timeline.OverlappingIds(new[] { a, b, c, d }).SetEquals(new[] { a.Id, b.Id }));
        var gaps = Timeline.Gaps(new[] { a, b, c, d }, t0, t0.AddHours(8), 15);
        Check("gaps: untracked stretches inside the window", gaps.Count == 2 && gaps[0].StartUtc == t0.AddHours(4) && gaps[0].EndUtc == t0.AddHours(6) && gaps[1].StartUtc == t0.AddHours(7) && gaps[1].EndUtc == t0.AddHours(8), string.Join(",", gaps.Select(g => $"{g.StartUtc:HH:mm}-{g.EndUtc:HH:mm}")));
        Check("gaps: shorter than the minimum are ignored", Timeline.Gaps(new[] { At(0, 1), At(1.1, 2) }, t0, t0.AddHours(2), 15).Count == 0);

        State.Data = new Workspace();
        var proj = new Project { Id = "p1", Name = "One", HourlyRate = 80, Billable = false };
        State.Data.Projects.Add(proj);
        var filled = Timeline.FillGap(gaps[0], proj, "Catch-up");
        Check("fill gap adds an entry for exactly that stretch", filled != null && State.Data.Sessions.Contains(filled) && filled.DurationMilliseconds == 7_200_000 && filled.HourlyRate == 80 && !filled.Billable && filled.Task == "Catch-up" && filled.Source == "manual");

        var fs = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
        var f1 = Focus.At(fs, fs.AddMinutes(10), 25, 5);
        var f2 = Focus.At(fs, fs.AddMinutes(27), 25, 5);
        var f3 = Focus.At(fs, fs.AddMinutes(31), 25, 5);
        Check("focus: rounds of work then break",
            f1 == (FocusPhase.Focus, TimeSpan.FromMinutes(15), 1) && f2 == (FocusPhase.Break, TimeSpan.FromMinutes(3), 1) && f3 == (FocusPhase.Focus, TimeSpan.FromMinutes(24), 2), $"{f1} {f2} {f3}");

        var s0 = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        var samples = new List<ActivitySample>();
        for (int i = 0; i < 80; i++) samples.Add(new(s0.AddSeconds(30 * i), "Figma", i % 3 == 0 ? "Logo v2" : "Brand board"));      // 15:00 - 15:40
        for (int i = 0; i < 4; i++) samples.Add(new(s0.AddMinutes(40).AddSeconds(30 * i), "Slack", "general"));                    // 2 min of Slack
        for (int i = 0; i < 60; i++) samples.Add(new(s0.AddMinutes(60).AddSeconds(30 * i), "Code", "Tempo"));                      // 16:00 - 16:30, already tracked
        var tracked = new Session { ProjectId = "p1", StartedAtUtc = s0.AddMinutes(55), EndedAtUtc = s0.AddMinutes(95), DurationMilliseconds = 2_400_000 };
        var sug = Activity.Suggest(samples, new[] { tracked }, TimeSpan.FromSeconds(30), 10);
        var adir = System.IO.Path.Combine(OutDir, "activity-test");
        if (System.IO.Directory.Exists(adir)) System.IO.Directory.Delete(adir, true);
        ActivityStore.Append(adir, new(s0, "Figma", "Logo \"v2\" | done"));
        ActivityStore.Append(adir, new(s0.AddDays(1), "Code", "Tempo"));
        var loaded = ActivityStore.Load(adir, s0.AddMinutes(-1), s0.AddMinutes(1));
        Check("activity store: saves and reads back samples in a time range", loaded.Count == 1 && loaded[0] == new ActivitySample(s0, "Figma", "Logo \"v2\" | done"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(adir, "notes.txt"), "keep");
        var pruned = ActivityStore.Prune(adir, 0, s0.AddDays(1).AddHours(6));   // 19 Nov, 21:00 UTC
        Check("activity store: old days are deleted, other files kept", pruned == 1 && System.IO.Directory.GetFiles(adir).Length == 2 && ActivityStore.Load(adir, s0.AddDays(1).AddMinutes(-1), s0.AddDays(1).AddMinutes(1)).Count == 1,
            $"pruned {pruned}, files {string.Join(",", System.IO.Directory.GetFiles(adir).Select(System.IO.Path.GetFileName))}");
        ActivityStore.Append(adir, new(s0.AddDays(1).AddMinutes(1), "Word", "Divorce papers.docx"));
        ActivityStore.StripTitles(adir);
        var stripped = ActivityStore.Load(adir, s0.AddDays(1).AddMinutes(-1), s0.AddDays(1).AddMinutes(5));
        Check("activity store: turning titles off erases the ones already saved", stripped.Count == 2 && stripped.All(x => x.Title == "") && stripped.Any(x => x.App == "Word"));
        Check("activity: long untracked app stretches become suggestions", sug.Count == 1 && sug[0].App == "Figma" && sug[0].Title == "Brand board" && sug[0].StartUtc == s0 && sug[0].EndUtc == s0.AddMinutes(40), string.Join(" | ", sug));
    }

    // ---------- security review fixes ----------
    static void Hardening()
    {
        // 1. absurd CSV durations / dates are skipped, never thrown at Apply
        var bad = "Date,Client,Project,Project Code,Task,Notes,Hours,Hours Rounded,Billable?\n" +
                  "2026-11-04,Acme,Big,,Design,ok,2,2,Yes\n" +
                  "2026-11-04,Acme,Big,,Design,huge,99999999,0,Yes\n" +
                  "1066-10-14,Acme,Big,,Design,ancient,1,1,Yes\n";
        var (br, bskip) = CsvImport.Map(CsvImport.ReadCsv(bad), "Harvest");
        Check("csv: absurd durations and dates are skipped", br.Count == 1 && bskip == 2, $"{br.Count}/{bskip}");
        var tog = "User,Email,Client,Project,Task,Description,Billable,Start date,Start time,End date,End time,Duration,Tags\n" +
                  "a,b,,P,,x,Yes,2026-11-03,09:00:00,,,2000000000:00:00,\n";
        Check("csv: overflowing clock durations are skipped", CsvImport.Map(CsvImport.ReadCsv(tog), "Toggl").Rows.Count == 0);

        // 2 + 4. hostile / malformed calendars: no exceptions, bounded output
        var tz = TimeZoneInfo.Utc;
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc); var to = from.AddDays(30);
        string Ev(string extra) => $"BEGIN:VEVENT\r\nUID:x\r\nSUMMARY:s\r\nDTSTART:20000101T090000Z\r\nDTEND:20000101T091500Z\r\n{extra}\r\nEND:VEVENT\r\n";
        bool NoThrow(string body, out List<CalEvent> r) { try { r = IcsImport.Parse("BEGIN:VCALENDAR\r\n" + body + "END:VCALENDAR", tz, from, to); return true; } catch { r = new(); return false; } }
        Check("ics: duplicate RRULE keys don't throw", NoThrow(Ev("RRULE:FREQ=DAILY;FREQ=WEEKLY"), out _));
        Check("ics: huge INTERVAL / COUNT / DURATION / UNTIL don't throw",
            NoThrow(Ev("RRULE:FREQ=DAILY;INTERVAL=2147483647"), out _) && NoThrow(Ev("RRULE:FREQ=MONTHLY;COUNT=99999999999"), out _)
            && NoThrow("BEGIN:VEVENT\r\nUID:y\r\nDTSTART:20261110T090000Z\r\nDURATION:P99999999999W\r\nEND:VEVENT\r\n", out _) && NoThrow(Ev("RRULE:FREQ=DAILY;UNTIL=99991231"), out _));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        NoThrow(Ev("RRULE:FREQ=DAILY"), out var daily);
        Check("ics: a daily series from 2000 still reaches today quickly", daily.Count == 30 && sw.ElapsedMilliseconds < 2000, $"{daily.Count} in {sw.ElapsedMilliseconds}ms");
        var many = new System.Text.StringBuilder();
        for (int i = 0; i < 1500; i++) many.Append($"BEGIN:VEVENT\r\nUID:m{i}\r\nSUMMARY:s\r\nDTSTART:20261101T0{i % 10}0000Z\r\nDTEND:20261101T0{i % 10}1500Z\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\n");
        sw.Restart();
        NoThrow(many.ToString(), out var flood);
        Check("ics: total expanded events are capped", flood.Count <= IcsImport.MaxResults && sw.ElapsedMilliseconds < 5000, $"{flood.Count} in {sw.ElapsedMilliseconds}ms");

        // 5. only real documents inside the expected folder are opened
        var rdir = System.IO.Path.Combine(State.Dir, "receipts");
        Check("open: receipt inside the receipts folder is allowed", SafeOpen.IsAllowed(System.IO.Path.Combine(rdir, "r.pdf"), rdir, ".pdf", ".png"));
        Check("open: executables, shortcuts, other folders and UNC paths are refused",
            !SafeOpen.IsAllowed(System.IO.Path.Combine(rdir, "r.exe"), rdir, ".pdf") && !SafeOpen.IsAllowed(System.IO.Path.Combine(rdir, "r.pdf.lnk"), rdir, ".pdf")
            && !SafeOpen.IsAllowed(System.IO.Path.Combine(rdir, "..", "x.pdf"), rdir, ".pdf") && !SafeOpen.IsAllowed(@"\\server\share\x.pdf", null, ".pdf")
            && !SafeOpen.IsAllowed(@"C:\Windows\x.pdf", rdir, ".pdf") && SafeOpen.IsAllowed(@"C:\Users\me\Documents\INV-1.pdf", null, ".pdf"));

        // 7. pay links must be https
        Check("pay link: https or empty only", SafeOpen.IsPayLink("") && SafeOpen.IsPayLink("https://pay.example.com/i/1") && !SafeOpen.IsPayLink("http://pay.example.com") && !SafeOpen.IsPayLink("javascript:alert(1)") && !SafeOpen.IsPayLink("pay.example.com"));
    }

    // ---------- phase 6: update check ----------
    static void Updates()
    {
        Check("update: newer tag is detected (with or without v)", UpdateService.IsNewer("2.5.0", "v2.6.0") && UpdateService.IsNewer("2.5.0", "2.5.1") && UpdateService.IsNewer("2.9.0", "v2.10.0"));
        Check("update: same or older tag is not an update", !UpdateService.IsNewer("2.6.0", "v2.6.0") && !UpdateService.IsNewer("2.6.0", "v2.5.9") && !UpdateService.IsNewer("2.6.0", "2.6"));
        Check("update: junk tags are ignored", !UpdateService.IsNewer("2.6.0", "latest") && !UpdateService.IsNewer("2.6.0", "") && !UpdateService.IsNewer("2.6.0", "v3.0.0-beta.1"));
        var json = "{\"tag_name\":\"v2.7.0\",\"html_url\":\"https://github.com/ColeDonMedia/tempo-time-tracker/releases/tag/v2.7.0\",\"draft\":false,\"prerelease\":false}";
        var parsed = UpdateService.ParseLatest(json);
        Check("update: release JSON is read", parsed is { Tag: "v2.7.0" } p && p.Url.EndsWith("/v2.7.0"));
        Check("update: drafts, pre-releases and broken JSON are skipped", UpdateService.ParseLatest(json.Replace("\"prerelease\":false", "\"prerelease\":true")) == null && UpdateService.ParseLatest("{oops") == null);
        Check("update: only this repo's GitHub release pages are opened",
            UpdateService.IsTrustedUrl("https://github.com/ColeDonMedia/tempo-time-tracker/releases/tag/v2.7.0")
            && !UpdateService.IsTrustedUrl("http://github.com/ColeDonMedia/tempo-time-tracker/releases/tag/v2.7.0")
            && !UpdateService.IsTrustedUrl("https://github.com.evil.example/ColeDonMedia/tempo-time-tracker/releases/x")
            && !UpdateService.IsTrustedUrl("https://github.com/someone-else/tempo-time-tracker/releases/x")
            && !UpdateService.IsTrustedUrl("file:///C:/Windows/System32/calc.exe"));
    }

    // ---------- phase 5: calendar import ----------
    static void Calendar()
    {
        var ny = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var ics = string.Join("\r\n",
            "BEGIN:VCALENDAR", "VERSION:2.0",
            "BEGIN:VEVENT", "UID:a1", "SUMMARY:Kickoff wi", " th Orbit Labs", "DTSTART:20261110T150000Z", "DTEND:20261110T160000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:a2", "SUMMARY:Design review\\, round 2", "DTSTART;TZID=America/New_York:20261111T090000", "DTEND;TZID=America/New_York:20261111T103000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:a3", "SUMMARY:Holiday", "DTSTART;VALUE=DATE:20261112", "DTEND;VALUE=DATE:20261113", "END:VEVENT",
            "BEGIN:VEVENT", "UID:a4", "SUMMARY:Standup", "DTSTART:20261102T140000Z", "DURATION:PT15M", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4", "EXDATE:20261104T140000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:a5", "SUMMARY:Way out of range", "DTSTART:20250101T100000Z", "DTEND:20250101T110000Z", "END:VEVENT",
            "END:VCALENDAR");
        var ev = IcsImport.Parse(ics, ny, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        Check("ics: folded lines are joined", ev.Any(e => e.Summary == "Kickoff with Orbit Labs" && e.StartUtc == new DateTime(2026, 11, 10, 15, 0, 0) && e.EndUtc == new DateTime(2026, 11, 10, 16, 0, 0)), string.Join(" / ", ev.Select(e => e.Summary)));
        Check("ics: TZID times convert to UTC and text escapes are undone", ev.Any(e => e.Summary == "Design review, round 2" && e.StartUtc == new DateTime(2026, 11, 11, 14, 0, 0) && (e.EndUtc - e.StartUtc).TotalMinutes == 90));
        Check("ics: all-day events are marked", ev.Any(e => e.Summary == "Holiday" && e.AllDay));
        var stand = ev.Where(e => e.Summary == "Standup").OrderBy(e => e.StartUtc).ToList();
        Check("ics: weekly RRULE expands with COUNT and EXDATE, DURATION sets the end",
            stand.Count == 3 && stand[0].StartUtc == new DateTime(2026, 11, 2, 14, 0, 0) && stand[1].StartUtc == new DateTime(2026, 11, 9, 14, 0, 0) && stand[2].StartUtc == new DateTime(2026, 11, 11, 14, 0, 0) && stand.All(e => (e.EndUtc - e.StartUtc).TotalMinutes == 15),
            string.Join(",", stand.Select(e => e.StartUtc.ToString("MM-dd HH:mm"))));
        Check("ics: events outside the range are left out", ev.All(e => e.Summary != "Way out of range"));
        var calP = State.Data.Projects[0];
        var before = State.Data.Sessions.Count;
        var timed = ev.Where(e => !e.AllDay).ToList();
        var (ca, cd) = IcsImport.Apply(timed, calP);
        Check("ics apply adds calendar entries on the project", ca == timed.Count && cd == 0 && State.Data.Sessions.Count == before + timed.Count
              && State.Data.Sessions.Any(x => x.Source == "calendar" && x.Task == "Design review, round 2" && x.DurationMilliseconds == 5_400_000 && x.ProjectId == calP.Id && x.HourlyRate == calP.HourlyRate));
        var (ca2, cd2) = IcsImport.Apply(timed, calP);
        Check("ics apply skips events already imported", ca2 == 0 && cd2 == timed.Count);
        Check("ics: garbage input gives no events and no crash", IcsImport.Parse("not a calendar\nBEGIN:VEVENT\nDTSTART:nope\nEND:VEVENT", ny, DateTime.MinValue, DateTime.MaxValue).Count == 0);
    }

    // ---------- phase 5: CSV imports ----------
    static void CsvImports()
    {
        var t = CsvImport.ReadCsv("a,b,c\r\n\"x, y\",\"say \"\"hi\"\"\",\"line1\nline2\"\r\n1,,3\n");
        Check("csv reader handles quotes, commas, escaped quotes and newlines", t.Count == 3 && t[1][0] == "x, y" && t[1][1] == "say \"hi\"" && t[1][2] == "line1\nline2" && t[2][1] == "", t.Count.ToString());

        var toggl = "User,Email,Client,Project,Task,Description,Billable,Start date,Start time,End date,End time,Duration,Tags,Amount (USD)\n" +
                    "Sam,sam@x.example,Hashmark,Logo refresh,,Sketches,Yes,2026-11-03,09:00:00,2026-11-03,10:30:00,01:30:00,\"design, print\",150.00\n" +
                    "Sam,sam@x.example,,Internal,,Admin,No,2026-11-03,11:00:00,2026-11-03,11:15:00,00:15:00,,\n";
        var tt = CsvImport.ReadCsv(toggl);
        Check("detects Toggl exports", CsvImport.Detect(tt[0]) == "Toggl", CsvImport.Detect(tt[0]) ?? "null");
        var (tr, tsk) = CsvImport.Map(tt, "Toggl");
        Check("maps Toggl rows (duration, billable, tags, client)", tr.Count == 2 && tsk == 0 && tr[0].DurationMs == 5_400_000 && tr[0].Billable && tr[0].Tags.SequenceEqual(new[] { "design", "print" }) && tr[0].Client == "Hashmark" && tr[0].StartLocal == new DateTime(2026, 11, 3, 9, 0, 0) && !tr[1].Billable);

        var harvest = "Date,Client,Project,Project Code,Task,Notes,Hours,Hours Rounded,Billable?,Invoiced?,Approved?,First Name,Last Name,Roles,Employee?,Billable Rate,Billable Amount,Cost Rate,Cost Amount,Currency,External Reference URL\n" +
                      "2026-11-04,Orbit Labs,Pitch deck,,Design,Slides 1-8,2.25,2.25,Yes,No,No,Sam,Lee,,Yes,120,270,0,0,US Dollar - USD,\n";
        var ht = CsvImport.ReadCsv(harvest);
        Check("detects Harvest exports", CsvImport.Detect(ht[0]) == "Harvest");
        var (hr, _) = CsvImport.Map(ht, "Harvest");
        Check("maps Harvest rows (decimal hours, rate, task + notes)", hr.Count == 1 && hr[0].DurationMs == 8_100_000 && hr[0].Rate == 120m && hr[0].Task == "Design: Slides 1-8" && hr[0].StartLocal.Date == new DateTime(2026, 11, 4));

        var clock = "Project,Client,Description,Task,User,Group,Email,Tags,Billable,Start Date,Start Time,End Date,End Time,Duration (h),Duration (decimal),Billable Rate (USD),Billable Amount (USD)\n" +
                    "Website,Hashmark,Homepage build,,Sam,,sam@x.example,dev,Yes,11/05/2026,01:15:00 PM,11/05/2026,03:45:00 PM,02:30:00,2.50,95.00,237.50\n" +
                    "Website,Hashmark,Broken row,,Sam,,sam@x.example,,Yes,not a date,xx,,,,,,\n";
        var ct = CsvImport.ReadCsv(clock);
        Check("detects Clockify exports", CsvImport.Detect(ct[0]) == "Clockify");
        var (cr, cskip) = CsvImport.Map(ct, "Clockify");
        Check("maps Clockify rows (US dates, 12-hour times) and skips broken rows", cr.Count == 1 && cskip == 1 && cr[0].StartLocal == new DateTime(2026, 11, 5, 13, 15, 0) && cr[0].DurationMs == 9_000_000 && cr[0].Rate == 95m);
        Check("unknown CSVs aren't guessed", CsvImport.Detect(new[] { "foo", "bar" }) == null);

        State.Data = new Workspace(); State.Normalize();
        var r1 = CsvImport.Apply(tr.Concat(hr).ToList(), 80m);
        Check("import creates missing clients and projects", r1.Added == 3 && r1.NewProjects == 3 && r1.NewClients == 2 && State.Data.Projects.Any(p => p.Name == "Pitch deck" && p.HourlyRate == 120m), $"{r1}");
        Check("imported entries are tagged as imports and keep billable", State.Data.Sessions.All(x => x.Source == "import") && State.Data.Sessions.Count(x => !x.Billable) == 1);
        var r2 = CsvImport.Apply(tr, 80m);
        Check("importing the same file twice adds nothing", r2.Added == 0 && r2.Duplicates == 2 && State.Data.Sessions.Count == 3, $"{r2}");
    }

    // ---------- phase 5: entry tools ----------
    static void EntryToolsTests()
    {
        State.Data = new Workspace();
        State.Data.Projects.Add(new Project { Id = "p1", Name = "One", HourlyRate = 100 });
        State.Data.Projects.Add(new Project { Id = "p2", Name = "Two", HourlyRate = 50 });
        var st = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);
        var s = new Session { ProjectId = "p1", Task = "Build", StartedAtUtc = st, EndedAtUtc = st.AddHours(2), DurationMilliseconds = 7_200_000, HourlyRate = 100, Tags = { "dev" } };
        State.Data.Sessions.Add(s);
        var sp = EntryTools.Split(s, st.AddMinutes(30));
        Check("split makes two entries that add up", sp != null && State.Data.Sessions.Count == 2 && sp.Value.First.DurationMilliseconds == 1_800_000 && sp.Value.Second.DurationMilliseconds == 5_400_000
              && sp.Value.First.EndedAtUtc == sp.Value.Second.StartedAtUtc && sp.Value.Second.Tags.SequenceEqual(new[] { "dev" }));
        Check("split outside the entry is refused", EntryTools.Split(State.Data.Sessions[0], st.AddHours(-1)) == null && State.Data.Sessions.Count == 2);

        var merged = EntryTools.Merge(State.Data.Sessions.ToList());
        Check("merge joins entries of one project back together", merged != null && State.Data.Sessions.Count == 1 && merged.DurationMilliseconds == 7_200_000 && merged.StartedAtUtc == st && merged.EndedAtUtc == st.AddHours(2) && merged.Task == "Build");
        var other = new Session { ProjectId = "p2", StartedAtUtc = st.AddHours(3), EndedAtUtc = st.AddHours(4), DurationMilliseconds = 3_600_000 };
        State.Data.Sessions.Add(other);
        Check("merge refuses entries from different projects", EntryTools.Merge(State.Data.Sessions.ToList()) == null && State.Data.Sessions.Count == 2);
        var billed = new Session { ProjectId = "p2", StartedAtUtc = st.AddHours(5), EndedAtUtc = st.AddHours(6), DurationMilliseconds = 3_600_000, InvoiceId = "inv" };
        State.Data.Sessions.Add(billed);
        Check("merge refuses invoiced entries", EntryTools.Merge(new[] { other, billed }) == null);

        var dup = EntryTools.Duplicate(merged!, new DateTime(2026, 11, 18));
        Check("duplicate copies to the chosen day at the same time", dup.Id != merged!.Id && State.Data.Sessions.Contains(dup) && dup.DurationMilliseconds == merged.DurationMilliseconds
              && State.ToLocal(dup.StartedAtUtc).Date == new DateTime(2026, 11, 18) && State.ToLocal(dup.StartedAtUtc).TimeOfDay == State.ToLocal(merged.StartedAtUtc).TimeOfDay && dup.InvoiceId == null && dup.Tags.SequenceEqual(merged.Tags) && !ReferenceEquals(dup.Tags, merged.Tags));

        var n = EntryTools.BulkEdit(new[] { merged, other, billed }, "p2", false, "review");
        Check("bulk edit changes project, billable and tags but skips invoiced entries", n == 2 && merged.ProjectId == "p2" && merged.HourlyRate == 50 && !merged.Billable && merged.Tags.Contains("review") && other.Tags.Contains("review") && billed.Tags.Count == 0);
        EntryTools.BulkEdit(new[] { merged }, null, null, "review");
        Check("bulk edit doesn't add the same tag twice", merged.Tags.Count(x => x == "review") == 1);

        State.Data.Timer = new TimerSnapshot();
        State.StartTimerAgo("p1", "Late start", 20);
        Check("start timer 'X minutes ago' counts that time", State.Running && State.ElapsedMs >= 1_199_000 && State.ElapsedMs < 1_210_000 && State.Data.Timer.StartedAtUtc <= DateTime.UtcNow.AddMinutes(-19.9), State.ElapsedMs.ToString());
        State.DiscardTimer();
    }

    // ---------- translation tables (was scripts/validate_strings.py) ----------
    static void StringTables()
    {
        var keys = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var bad = new List<string>();
        static string Ph(string x) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(x, @"\{\d\}").Select(m => m.Value).OrderBy(v => v));
        foreach (var row in Loc.AllRows())
        {
            var parts = row.Split('|');
            if (parts.Length != Loc.Languages.Length) { bad.Add($"{parts.Length} fields: {parts[0]}"); continue; }
            for (int i = 1; i < parts.Length; i++)
                if (Ph(parts[i]) != Ph(parts[0]) && !parts[0].EndsWith("entry ({1}).")) bad.Add($"placeholders [{Loc.Languages[i].Code}]: {parts[0]}");
            if (keys.TryGetValue(parts[0], out var prev) && !prev.SequenceEqual(parts[1..])) bad.Add($"conflicting duplicate: {parts[0]}");
            keys[parts[0]] = parts[1..];
        }
        Check($"translation rows are well-formed ({keys.Count} keys)", bad.Count == 0, string.Join("; ", bad.Take(5)));
    }

    // ---------- phase 4: never forget to track ----------
    static void NeverForget()
    {
        var now = new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
        var idle0 = TimeSpan.Zero;
        Check("reminder: off when every-minutes is 0", !Reminders.ShouldRemind(false, idle0, now.AddHours(-2), null, 0, now));
        Check("reminder: never while the timer runs", !Reminders.ShouldRemind(true, idle0, now.AddHours(-2), null, 15, now));
        Check("reminder: not while the person is away", !Reminders.ShouldRemind(false, TimeSpan.FromMinutes(10), now.AddHours(-2), null, 15, now));
        Check("reminder: waits the full interval", !Reminders.ShouldRemind(false, idle0, now.AddMinutes(-10), null, 15, now) && Reminders.ShouldRemind(false, idle0, now.AddMinutes(-16), null, 15, now));
        Check("reminder: doesn't repeat inside the interval", !Reminders.ShouldRemind(false, idle0, now.AddHours(-1), now.AddMinutes(-5), 15, now) && Reminders.ShouldRemind(false, idle0, now.AddHours(-1), now.AddMinutes(-15), 15, now));
        var local = new DateTime(2026, 11, 18, 18, 30, 0);
        Check("end-of-day: warns after the time when a timer runs", Reminders.ShouldWarnEndOfDay(true, "18:00", local, null));
        Check("end-of-day: not before the time, not without a timer, not twice a day",
            !Reminders.ShouldWarnEndOfDay(true, "19:00", local, null) && !Reminders.ShouldWarnEndOfDay(false, "18:00", local, null) && !Reminders.ShouldWarnEndOfDay(true, "18:00", local, local.Date));
        Check("end-of-day: blank or garbage time is off", !Reminders.ShouldWarnEndOfDay(true, "", local, null) && !Reminders.ShouldWarnEndOfDay(true, "soon", local, null));

        Check("hotkey parse", HotkeyService.Parse("Ctrl+Alt+Space") == (3u, 0x20u) && HotkeyService.Parse("Ctrl+Shift+Space") == (6u, 0x20u) && HotkeyService.Parse("Off").Key == 0);

        var bdir = System.IO.Path.Combine(OutDir, "backup-test");
        if (System.IO.Directory.Exists(bdir)) System.IO.Directory.Delete(bdir, true);
        System.IO.Directory.CreateDirectory(bdir);
        for (int i = 0; i < 6; i++) System.IO.File.WriteAllText(System.IO.Path.Combine(bdir, BackupService.FileNameFor(new DateTime(2026, 11, 1).AddDays(i))), "{}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(bdir, "notes.txt"), "keep me");
        var removed = BackupService.Prune(bdir, 4);
        var left = System.IO.Directory.GetFiles(bdir).Select(System.IO.Path.GetFileName).ToList();
        Check("backup prune keeps the newest N and nothing else is touched", removed == 2 && left.Count == 5 && left.Contains("notes.txt") && left.Contains("tempo-backup-2026-11-06.json") && !left.Contains("tempo-backup-2026-11-01.json"), string.Join(",", left));
        State.Data.Settings.BackupFolder = bdir;
        var made = BackupService.RunNow();
        Check("backup now writes today's copy", made != null && System.IO.File.Exists(made));

        // switching project mid-timer
        var a = State.Data.Projects[0]; var b = State.Data.Projects[1];
        State.Data.Timer = new TimerSnapshot { ProjectId = a.Id, StartedAtUtc = DateTime.UtcNow.AddMinutes(-30), AccumulatedMilliseconds = 1_800_000, RunSinceUtc = DateTime.UtcNow };
        var n0 = State.Data.Sessions.Count(x => x.ProjectId == a.Id);
        State.SwitchProject(b);
        Check("switch project saves the old project's time", State.Data.Sessions.Count(x => x.ProjectId == a.Id) == n0 + 1);
        Check("switch project keeps running on the new project", State.Running && State.Data.Timer.ProjectId == b.Id && State.ElapsedMs < 5000);
        State.PauseTimer(); State.StopTimer();
        State.Data.Timer = new TimerSnapshot { ProjectId = a.Id };
        State.SwitchProject(b);
        Check("switch project with no timer just selects it", !State.HasTimer && State.Data.Timer.ProjectId == b.Id);
    }
}
