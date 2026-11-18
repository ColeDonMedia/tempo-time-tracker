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

    static void Check(string name, bool ok, string? detail = null)
    {
        Log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail == null ? "" : "  -> " + detail)}");
        if (!ok) fails++;
    }

    public static void Run(string path)
    {
        try { Tests(); }
        catch (Exception ex) { Check("no exception", false, ex.ToString()); }
        Log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        System.IO.File.WriteAllText(path, Log.ToString());
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
    }
}
