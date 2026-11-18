using System.Text.Json.Serialization;

namespace Tempo;

// Property names intentionally match the original Tempo tempo-data.json so existing data loads unchanged.
public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public decimal HourlyRate { get; set; }
    public string Color { get; set; } = "blue";
    public bool Archived { get; set; }
    public string ClientId { get; set; } = "";
    public bool Billable { get; set; } = true;            // new time on this project is billable by default
    public string BudgetType { get; set; } = "None";      // None | Hours | Amount | Fixed | Retainer
    public decimal BudgetValue { get; set; }
    public string BudgetPeriod { get; set; } = "Total";   // Total | Monthly (Hours / Amount budgets)
    public bool AlertedWarn { get; set; }
    public bool AlertedOver { get; set; }
    public string AlertPeriodKey { get; set; } = "";
    public decimal CostRate { get; set; }                  // what an hour costs you (0 = use the default cost rate)

    public override string ToString() => Label;
    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(Client) ? Name : $"{Name}  ·  {Client}";}

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Task { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public long DurationMilliseconds { get; set; }
    public decimal HourlyRate { get; set; }
    /// <summary>Set once the session is billed on an invoice, so it can never be billed twice.</summary>
    public string? InvoiceId { get; set; }
    public bool Billable { get; set; } = true;
    public List<string> Tags { get; set; } = new();
    public string Source { get; set; } = "";              // timer | manual | calendar | import | grid
    public string Member { get; set; } = "";              // who tracked it (team sync); "" = just you
}

public class TimerSnapshot
{
    public string? ProjectId { get; set; }
    public string Task { get; set; } = "";
    public long AccumulatedMilliseconds { get; set; }
    public DateTime? RunSinceUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
}

public class BillingProfile
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string Note { get; set; } = "Thank you for your business.";
    public string TaxId { get; set; } = "";
    public string PaymentInstructions { get; set; } = "";
    public string LogoPath { get; set; } = "";
}

public class AppSettings
{
    public string Theme { get; set; } = "System";        // System | Light | Dark
    public string Backdrop { get; set; } = "Mica";       // Mica | Acrylic | Solid
    public string TimeZoneId { get; set; } = "";
    public bool Use24Hour { get; set; }
    public string WeekStart { get; set; } = "Monday";
    public double WeeklyGoalHours { get; set; } = 30;
    public string CurrencyCode { get; set; } = "USD";
    public int RoundingMinutes { get; set; }              // 0 = exact
    public int PaymentTermsDays { get; set; } = 14;
    public string TaxLabel { get; set; } = "Tax";
    public decimal DefaultTaxPercent { get; set; }
    public string InvoiceGrouping { get; set; } = "Task"; // Task | Day | Entry
    public string Language { get; set; } = "";            // "" = pick from Windows on first run
    public double? MiniLeft { get; set; }
    public double? MiniTop { get; set; }
    public bool MiniPinned { get; set; } = true;
    public bool MainPinned { get; set; }
    public int IdleMinutes { get; set; } = 10;            // 0 = off
    public bool CloseToTray { get; set; }
    public string InvoiceTemplate { get; set; } = "Classic";   // Classic | Minimal | Bold
    public decimal LateFeePercent { get; set; }
    public int LateFeeGraceDays { get; set; }
    public string DefaultPayLink { get; set; } = "";
    public string Hotkey { get; set; } = "Ctrl+Alt+Space"; // Off | Ctrl+Alt+Space | Ctrl+Alt+T | Ctrl+Shift+Space
    public int ReminderMinutes { get; set; }               // 0 = off
    public string EndOfDayTime { get; set; } = "";         // "" = off, otherwise HH:mm
    public string BackupFolder { get; set; } = "";
    public int BackupKeep { get; set; } = 14;
    public bool CheckUpdates { get; set; }
    public double DailyGoalHours { get; set; }              // 0 = no daily goal
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool TrackActivity { get; set; }                // opt-in: sample the app in front every 30 s
    public bool ActivityTitles { get; set; } = true;       // also keep window titles (else app names only)
    public int ActivityKeepDays { get; set; } = 14;
    public decimal DefaultCostRate { get; set; }            // per hour, in the base currency
    /// <summary>How much one unit of a currency is worth in the base currency (CurrencyCode), e.g. "EUR" → 1.08.</summary>
    public Dictionary<string, decimal> ExchangeRates { get; set; } = new();
    public string SyncFolder { get; set; } = "";             // "" = sync off
    public string DeviceId { get; set; } = "";
    public string MemberName { get; set; } = "";             // your name on shared entries
    public List<CalendarFeed> CalendarFeeds { get; set; } = new();
}

public class TaxLine
{
    public string Label { get; set; } = "Tax";
    public decimal Percent { get; set; }
}

public class InvoiceLine
{
    public string Description { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public string Kind { get; set; } = "Time";            // Time | Expense | Custom | Fee
}

public class Invoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientAddress { get; set; } = "";
    public string PoNumber { get; set; } = "";
    public DateTime IssuedUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string TimeZoneId { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public List<InvoiceLine> Lines { get; set; } = new();
    public decimal TotalHours { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public string TaxLabel { get; set; } = "Tax";
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Note { get; set; } = "";
    public string PaymentInstructions { get; set; } = "";
    public string StudioName { get; set; } = "";
    public string StudioEmail { get; set; } = "";
    public string StudioAddress { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string Language { get; set; } = "";            // language the invoice was issued in ("" = English)
    public string Status { get; set; } = "Issued";        // Issued | Paid | Void
    public DateTime? PaidUtc { get; set; }
    public List<string> SessionIds { get; set; } = new();
    public string FilePath { get; set; } = "";
    public string Kind { get; set; } = "Invoice";         // Invoice | Quote  (quote statuses: Sent | Accepted | Declined)
    public string ClientId { get; set; } = "";
    public List<Payment> Payments { get; set; } = new();
    public decimal LateFeeAmount { get; set; }
    public string PayLink { get; set; } = "";
    public string Template { get; set; } = "Classic";
    public bool IncludeTimesheet { get; set; }
    public List<InvoiceLine> Timesheet { get; set; } = new();
    public List<string> ExpenseIds { get; set; } = new();
    public string RecurringId { get; set; } = "";
    public string ConvertedFrom { get; set; } = "";
    /// <summary>Several taxes (e.g. GST + PST). When empty, the single TaxLabel / TaxPercent applies.</summary>
    public List<TaxLine> Taxes { get; set; } = new();
    /// <summary>EU-style reverse charge: no tax is added and the invoice says the customer accounts for it.</summary>
    public bool ReverseCharge { get; set; }
    public string ClientTaxId { get; set; } = "";
    public DateTime? LastReminderUtc { get; set; }
    public int RemindersSent { get; set; }

    [JsonIgnore] public decimal GrandTotal => Total + LateFeeAmount;
    [JsonIgnore] public decimal AmountPaid => Payments.Sum(p => p.Amount);
    [JsonIgnore] public decimal Balance => Status == "Void" ? 0 : Math.Max(0, GrandTotal - AmountPaid);
}

public class Workspace
{
    public List<Project> Projects { get; set; } = new();
    public List<Session> Sessions { get; set; } = new();
    public TimerSnapshot Timer { get; set; } = new();
    public BillingProfile Billing { get; set; } = new();
    public int InvoiceCounter { get; set; } = 1;
    public string InvoiceNumber { get; set; } = "";
    public AppSettings Settings { get; set; } = new();
    public List<Invoice> Invoices { get; set; } = new();
    public List<Client> Clients { get; set; } = new();
    public List<Expense> Expenses { get; set; } = new();
    public List<TrashItem> Trash { get; set; } = new();
    public List<RecurringInvoice> Recurring { get; set; } = new();
    public int QuoteCounter { get; set; } = 1;
}


