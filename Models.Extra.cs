namespace Tempo;

/// <summary>A person or brand you bill. Projects, invoices and rates inherit their details.</summary>
public class Client
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal? DefaultRate { get; set; }
    public string Currency { get; set; } = "";            // "" = use the app default
    public int? PaymentTermsDays { get; set; }
    public decimal? TaxPercent { get; set; }
    public bool ReverseCharge { get; set; }               // business customer abroad: invoices carry no tax
    public bool Archived { get; set; }
    public override string ToString() => Name;
}

public class Expense
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public DateTime Date { get; set; }                    // local calendar date
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Billable { get; set; } = true;
    public string ReceiptPath { get; set; } = "";
    public string? InvoiceId { get; set; }
}

public class Payment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime DateUtc { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Anything deleted waits here (as JSON) until the user empties the list, so deletes can be undone.</summary>
public class TrashItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";               // Session | Project | Invoice | Client | Expense
    public string Label { get; set; } = "";
    public DateTime DeletedUtc { get; set; }
    public string Json { get; set; } = "";
}

public class ProjectBundle
{
    public Project Project { get; set; } = new();
    public List<Session> Sessions { get; set; } = new();
    public List<Expense> Expenses { get; set; } = new();
}

/// <summary>A retainer or any invoice that repeats: Tempo creates the next one when it's due.</summary>
public class RecurringInvoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Frequency { get; set; } = "Monthly";   // Weekly | Monthly | Quarterly
    public DateTime NextRun { get; set; }                // local date
    public bool Active { get; set; } = true;
    public List<InvoiceLine> Lines { get; set; } = new();
    public string TaxLabel { get; set; } = "Tax";
    public decimal TaxPercent { get; set; }
    public int DueDays { get; set; } = 14;
    public string Note { get; set; } = "";
    public string PoNumber { get; set; } = "";
}
