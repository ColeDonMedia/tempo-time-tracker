using System.Globalization;

namespace Tempo;

public sealed record BudgetUsage(string Type, double Fraction, string Text, bool Over, bool Warn);

/// <summary>Project budgets: hours or amount caps (total or per month), fixed fees and retainers.</summary>
public static class Budget
{
    public static (DateTime From, DateTime To) Window(Project p)
    {
        if (p.BudgetPeriod == "Monthly" || p.BudgetType == "Retainer")
        {
            var t = State.TodayLocal;
            var from = new DateTime(t.Year, t.Month, 1);
            return (from, from.AddMonths(1));
        }
        return (DateTime.MinValue, DateTime.MaxValue);
    }

    static IEnumerable<Session> InWindow(Project p)
    {
        var (a, b) = Window(p);
        return State.Data.Sessions.Where(s => s.ProjectId == p.Id && State.ToLocal(s.StartedAtUtc) >= a && State.ToLocal(s.StartedAtUtc) < b);
    }

    public static BudgetUsage? Usage(Project p)
    {
        if (p.BudgetType is "None" or "" || p.BudgetValue <= 0) return null;
        var ss = InWindow(p).ToList();
        var hours = ss.Sum(s => s.DurationMilliseconds) / 3600000m;
        switch (p.BudgetType)
        {
            case "Hours":
                {
                    double f = (double)(hours / p.BudgetValue);
                    return new BudgetUsage("Hours", f, Loc.F("{0} of {1} h used", Fmt.Dec(Math.Round(hours, 1)), Fmt.Dec(p.BudgetValue)), f >= 1, f >= 0.8);
                }
            case "Amount":
                {
                    var used = ss.Where(s => s.Billable).Sum(State.Earned);
                    double f = (double)(used / p.BudgetValue);
                    return new BudgetUsage("Amount", f, Loc.F("{0} of {1} used", Fmt.Money(used), Fmt.Money(p.BudgetValue)), f >= 1, f >= 0.8);
                }
            default: // Fixed | Retainer: show what the work works out to per hour
                {
                    var eff = hours > 0 ? p.BudgetValue / hours : 0;
                    var text = hours > 0 ? Loc.F("Effective rate {0} / h", Fmt.Money(eff)) : Fmt.Money(p.BudgetValue);
                    return new BudgetUsage(p.BudgetType, 0, text, p.HourlyRate > 0 && hours > 0 && eff < p.HourlyRate, false);
                }
        }
    }

    /// <summary>Call after time is saved: tells the user once when a project passes 80% and once when it goes over.</summary>
    public static void Check(Project? p)
    {
        if (p == null) return;
        var u = Usage(p);
        if (u == null || u.Type is not ("Hours" or "Amount")) return;
        var key = p.BudgetPeriod == "Monthly" ? DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture) : "total";
        if (p.AlertPeriodKey != key) { p.AlertPeriodKey = key; p.AlertedWarn = p.AlertedOver = false; }
        if (u.Over && !p.AlertedOver) { p.AlertedOver = p.AlertedWarn = true; State.Toast(Loc.F("{0} is over budget", p.Name), true); State.Save(); }
        else if (u.Warn && !u.Over && !p.AlertedWarn) { p.AlertedWarn = true; State.Toast(Loc.F("{0} has used 80% of its budget", p.Name), true); State.Save(); }
    }
}
