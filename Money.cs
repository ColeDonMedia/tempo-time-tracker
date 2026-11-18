using System.Text;

namespace Tempo;

/// <summary>Currencies and costs: converting client currencies to your base currency, and what an hour costs you.</summary>
public static class Money
{
    public static string Base => State.Data.Settings.CurrencyCode;

    /// <summary>The currency a project bills in: its client's, else the app's base currency.</summary>
    public static string CurrencyOf(Project? p)
    {
        var c = State.ClientOf(p)?.Currency;
        return string.IsNullOrWhiteSpace(c) ? Base : c.ToUpperInvariant();
    }

    /// <summary>Value of one unit of <paramref name="currency"/> in the base currency; null when no rate is set.</summary>
    public static decimal? Rate(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Equals(Base, StringComparison.OrdinalIgnoreCase)) return 1m;
        var rates = State.Data.Settings.ExchangeRates;
        foreach (var (k, v) in rates) if (k.Equals(currency, StringComparison.OrdinalIgnoreCase) && v > 0) return v;
        return null;
    }

    /// <summary>Converts to the base currency. Without a rate the amount is counted 1:1 (reports flag that).</summary>
    public static decimal ToBase(decimal amount, string currency) => Math.Round(amount * (Rate(currency) ?? 1m), 2);

    /// <summary>What an hour of work on this project costs you, in the base currency.</summary>
    public static decimal CostRate(Project? p) => p is { CostRate: > 0 } ? p.CostRate : State.Data.Settings.DefaultCostRate;

    /// <summary>Reads the Settings box: one "EUR = 1.08" per line. Returns the rates and the lines it couldn't read.</summary>
    public static (Dictionary<string, decimal> Rates, List<string> Bad) ParseRates(string text)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase); var bad = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new[] { '=', ' ', '\t', ':' }, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length == 3 && parts[0].All(char.IsLetter)
                && decimal.TryParse(parts[1].TrimStart('='), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1_000_000)
                rates[parts[0].ToUpperInvariant()] = v;
            else bad.Add(line);
        }
        return (rates, bad);
    }

    public static string FormatRates(Dictionary<string, decimal> rates) =>
        string.Join(Environment.NewLine, rates.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key.ToUpperInvariant()} = {r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
}

public sealed record TaxRow(string Currency, string Label, decimal Percent, decimal Base, decimal Tax, int Invoices);

/// <summary>Invoice and reminder emails, written in the invoice's language.</summary>
public static class InvoiceEmail
{
    public static (string To, string Subject, string Body) Compose(Invoice inv, bool reminder, DateTime nowUtc)
    {
        var lang = string.IsNullOrEmpty(inv.Language) ? "en" : inv.Language;   // older invoices have no language: English
        string T(string en) => Loc.For(lang, en);
        string F(string en, params object?[] a) => Loc.FFor(lang, en, a);
        var from = string.IsNullOrWhiteSpace(inv.StudioName) ? "Tempo" : inv.StudioName;
        var balance = Fmt.Money(inv.Balance, inv.Currency);
        var due = Fmt.Date(State.ToLocal(inv.DueUtc), lang);
        var greeting = string.IsNullOrWhiteSpace(inv.ClientName) ? T("Hello,") : F("Hello {0},", inv.ClientName);

        var sb = new StringBuilder();
        sb.AppendLine(greeting).AppendLine();
        string subject;
        if (reminder)
        {
            var days = Math.Max(0, (int)(State.ToLocal(nowUtc).Date - State.ToLocal(inv.DueUtc).Date).TotalDays);
            subject = F("Reminder: invoice {0} from {1}", inv.Number, from);
            sb.AppendLine(F("This is a friendly reminder that invoice {0} was due on {1} and is now {2} days overdue.", inv.Number, due, days));
            sb.AppendLine(F("The balance due is {0}.", balance));
        }
        else
        {
            subject = F("Invoice {0} from {1}", inv.Number, from);
            sb.AppendLine(F("Please find attached invoice {0} for {1}, due on {2}.", inv.Number, balance, due));
        }
        if (!string.IsNullOrWhiteSpace(inv.PayLink)) sb.AppendLine().AppendLine(F("You can pay online here: {0}", StripeSync.WithReference(inv.PayLink, inv.Number)));
        else if (!string.IsNullOrWhiteSpace(inv.PaymentInstructions)) sb.AppendLine().AppendLine(inv.PaymentInstructions.Trim());
        sb.AppendLine().AppendLine(T("Thank you!")).AppendLine(from);
        return (inv.ClientEmail.Trim(), subject, sb.ToString());
    }
}
