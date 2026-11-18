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
