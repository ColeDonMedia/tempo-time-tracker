using System.Text.Json;

namespace Tempo;

/// <summary>Soft delete: removed items are kept as JSON so they can be restored (undo toast or Settings → Recently deleted).</summary>
public static class TrashBin
{
    static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };

    public static TrashItem Add(string kind, object payload, string label)
    {
        var t = new TrashItem { Kind = kind, Label = label, DeletedUtc = DateTime.UtcNow, Json = JsonSerializer.Serialize(payload, payload.GetType(), J) };
        State.Data.Trash.Add(t);
        return t;
    }

    static T? Read<T>(TrashItem t) => JsonSerializer.Deserialize<T>(t.Json, J);

    public static bool Restore(TrashItem t)
    {
        try
        {
            var d = State.Data;
            switch (t.Kind)
            {
                case "Session":
                    if (Read<Session>(t) is { } s && d.Sessions.All(x => x.Id != s.Id)) d.Sessions.Add(s);
                    break;
                case "Expense":
                    if (Read<Expense>(t) is { } e && d.Expenses.All(x => x.Id != e.Id)) d.Expenses.Add(e);
                    break;
                case "Client":
                    if (Read<Client>(t) is { } c && d.Clients.All(x => x.Id != c.Id)) d.Clients.Add(c);
                    break;
                case "Project":
                    if (Read<ProjectBundle>(t) is { } b)
                    {
                        if (d.Projects.All(x => x.Id != b.Project.Id)) d.Projects.Add(b.Project);
                        foreach (var s2 in b.Sessions) if (d.Sessions.All(x => x.Id != s2.Id)) d.Sessions.Add(s2);
                        foreach (var e2 in b.Expenses) if (d.Expenses.All(x => x.Id != e2.Id)) d.Expenses.Add(e2);
                    }
                    break;
                case "Invoice":
                    if (Read<Invoice>(t) is { } inv && d.Invoices.All(x => x.Id != inv.Id))
                    {
                        d.Invoices.Add(inv);
                        if (inv.Status != "Void")
                        {
                            foreach (var sid in inv.SessionIds) if (d.Sessions.FirstOrDefault(x => x.Id == sid) is { InvoiceId: null } sess) sess.InvoiceId = inv.Id;
                            foreach (var eid in inv.ExpenseIds) if (d.Expenses.FirstOrDefault(x => x.Id == eid) is { InvoiceId: null } ex) ex.InvoiceId = inv.Id;
                        }
                    }
                    break;
                case "Recurring":
                    if (Read<RecurringInvoice>(t) is { } rr && d.Recurring.All(x => x.Id != rr.Id)) d.Recurring.Add(rr);
                    break;
                default: return false;
            }
            d.Trash.Remove(t);
            State.Normalize();
            State.Notify();
            return true;
        }
        catch { return false; }
    }

    public static void Purge(TrashItem t) { State.Data.Trash.Remove(t); State.Notify(); }
    public static void Empty() { State.Data.Trash.Clear(); State.Notify(); }
}
