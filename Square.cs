using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tempo;

public sealed record SquareInvoiceState(string Id, string Status, int Version, long PaidMinor, long DueMinor, string Currency, string PublicUrl, string Number = "", string LocationId = "", string Email = "");
public sealed record SquareOrder(string Id, long TotalMinor, string Currency);
public sealed record SquareLocation(string Id, string Currency, string Status);

/// <summary>
/// Square Invoices with the user's own access token: a Tempo invoice becomes a Square draft (customer + order + invoice),
/// the user confirms before Square emails it, and payments Square collects come back as Tempo payments.
/// Everything here is UI-free and network-free; IntegrationClient does the requests.
/// </summary>
public static class SquareSync
{
    public const string ProdApi = "https://connect.squareup.com", SandboxApi = "https://connect.squareupsandbox.com";
    public static string Api(bool sandbox) => sandbox ? SandboxApi : ProdApi;

    /// <summary>The inverse of StripeSync.FromMinor (same currency tables, so the two never drift).</summary>
    public static long ToMinor(decimal amount, string currency) => (long)Math.Round(amount / StripeSync.FromMinor(1, currency), MidpointRounding.AwayFromZero);

    static bool IsEmail(string s) => System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>Why this invoice can't go to Square (translated), or null when it can.</summary>
    public static string? CannotSend(Invoice inv, string locationCurrency)
    {
        if (inv.Kind != "Invoice" || inv.Status != "Issued") return Loc.T("Only open invoices can be sent with Square.");
        if (inv.AmountPaid > 0) return Loc.T("This invoice already has payments recorded. Square would bill the full amount, so send it another way.");
        if (!IsEmail(inv.ClientEmail)) return Loc.T("Add the client's email address first. Square emails the invoice to it.");
        if (!inv.Currency.Equals(locationCurrency, StringComparison.OrdinalIgnoreCase)) return Loc.F("Your Square location uses {0}, but this invoice is in {1}.", locationCurrency.ToUpperInvariant(), inv.Currency);
        if (inv.Lines.Any(l => l.Amount < 0) || inv.LateFeeAmount < 0) return Loc.T("Square can't take negative lines. Use a discount instead.");
        if (inv.Lines.Count == 0 || inv.GrandTotal <= 0) return Loc.T("There's nothing to bill on this invoice.");
        if (inv.Lines.Count > 400) return Loc.T("This invoice has too many lines for one Square invoice.");
        if (inv.Number.Trim().Length is 0 or > 191) return Loc.T("This invoice needs a number of at most 191 characters.");
        return null;
    }

    static string Inv(decimal d) => d.ToString("0.####", CultureInfo.InvariantCulture);

    public static string DueDate(Invoice inv, DateTime todayLocal)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(inv.TimeZoneId); } catch { tz = TimeZoneInfo.Local; }
        var due = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(inv.DueUtc, DateTimeKind.Utc), tz).Date;
        if (due < todayLocal.Date) due = todayLocal.Date;   // Square won't take a due date in the past
        return due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Short hash of everything that ends up on the Square invoice (not payments), so an edited invoice gets a fresh draft.</summary>
    public static string Fingerprint(Invoice inv)
    {
        var sb = new StringBuilder();
        sb.Append(inv.Number).Append('|').Append(inv.Currency.ToUpperInvariant()).Append('|').Append(inv.ClientEmail.Trim().ToLowerInvariant()).Append('|').Append(inv.ClientName)
          .Append('|').Append(inv.DueUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(inv.TimeZoneId).Append('|').Append(inv.Note)
          .Append('|').Append(Inv(inv.DiscountAmount)).Append('|').Append(Inv(inv.LateFeeAmount)).Append('|').Append(inv.ReverseCharge).Append('|').Append(inv.Language);
        foreach (var l in inv.Lines) sb.Append("|L").Append(l.Description).Append('\u001f').Append(Inv(l.Amount));
        foreach (var t in InvoiceService.TaxLines(inv)) sb.Append("|T").Append(t.Label).Append('\u001f').Append(Inv(t.Percent));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    static string SafeId(Invoice inv)
    {
        var s = new string(inv.Id.Where(char.IsAsciiLetterOrDigit).ToArray());
        return s.Length > 40 ? s[..40] : s;
    }

    // Square remembers request keys, so a retry after a network failure never makes a second order or invoice.
    public static string OrderKey(Invoice inv, string fp, bool taxesAsLines) => $"tempo-o{inv.SquareAttempt}-{SafeId(inv)}-{fp}" + (taxesAsLines ? "-t" : "");
    // the due date is part of the key: a past due date becomes "today", so a retry on another day sends a different body
    public static string InvoiceKey(Invoice inv, string fp, string due = "") =>
        $"tempo-i{inv.SquareAttempt}-{SafeId(inv)}-{fp}" + (due.Length > 0 ? "-" + new string(due.Where(char.IsAsciiDigit).ToArray()) : "");
    public static string PublishKey(Invoice inv, string fp) => $"tempo-p{inv.SquareAttempt}-{SafeId(inv)}-{fp}";
    public static string CustomerKey(string email) =>
        "tempo-c-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))[..24].ToLowerInvariant();

    static string Cap(string s, int max) { s = new string(s.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim(); return s.Length > max ? s[..max] : s; }
    static Dictionary<string, object> Money(decimal amount, string cur) => new() { ["amount"] = ToMinor(amount, cur), ["currency"] = cur.ToUpperInvariant() };

    /// <summary>
    /// CreateOrder body. Tempo's taxes and discount apply to the work lines only (never to a late fee), as on Tempo's own PDF.
    /// With <paramref name="taxesAsLines"/> each tax becomes a fixed line instead, for when Square's own rounding would differ by a cent.
    /// </summary>
    public static string OrderBody(Invoice inv, string locationId, string customerId, bool taxesAsLines, string key)
    {
        var cur = inv.Currency.ToUpperInvariant();
        var taxLines = InvoiceService.TaxLines(inv).Where(t => t.Percent != 0).ToList();
        var lines = new List<object>();
        for (int i = 0; i < inv.Lines.Count; i++)
        {
            var l = inv.Lines[i];
            var name = Cap(l.Description, 512);
            var item = new Dictionary<string, object>
            {
                ["uid"] = "l" + i, ["name"] = name.Length > 0 ? name : Loc.For(inv.Language, "Item"), ["quantity"] = "1", ["base_price_money"] = Money(l.Amount, cur),
            };
            if (!taxesAsLines && taxLines.Count > 0) item["applied_taxes"] = taxLines.Select((_, j) => new Dictionary<string, object> { ["tax_uid"] = "t" + j }).ToList();
            if (inv.DiscountAmount > 0) item["applied_discounts"] = new List<object> { new Dictionary<string, object> { ["discount_uid"] = "d" } };
            lines.Add(item);
        }
        if (taxesAsLines)
        {
            var taxable = inv.Subtotal - inv.DiscountAmount;
            for (int j = 0; j < taxLines.Count; j++)
                lines.Add(new Dictionary<string, object>
                {
                    ["uid"] = "tl" + j, ["name"] = Cap($"{Loc.For(inv.Language, taxLines[j].Label)} ({Inv(taxLines[j].Percent)}%)", 512), ["quantity"] = "1",
                    ["base_price_money"] = Money(InvoiceService.TaxFor(taxable, taxLines[j].Percent), cur),
                });
        }
        if (inv.LateFeeAmount > 0)
            lines.Add(new Dictionary<string, object> { ["uid"] = "fee", ["name"] = Loc.For(inv.Language, "Late fee"), ["quantity"] = "1", ["base_price_money"] = Money(inv.LateFeeAmount, cur) });

        var order = new Dictionary<string, object> { ["location_id"] = locationId, ["customer_id"] = customerId, ["reference_id"] = Cap(inv.Number, 40), ["line_items"] = lines };
        if (!taxesAsLines && taxLines.Count > 0)
            order["taxes"] = taxLines.Select((t, j) => new Dictionary<string, object> { ["uid"] = "t" + j, ["name"] = Cap(Loc.For(inv.Language, t.Label), 255), ["percentage"] = Inv(t.Percent), ["scope"] = "LINE_ITEM" }).ToList();
        if (inv.DiscountAmount > 0)
            order["discounts"] = new List<object> { new Dictionary<string, object> { ["uid"] = "d", ["name"] = Loc.FFor(inv.Language, "Discount ({0}%)", Inv(inv.DiscountPercent)), ["amount_money"] = Money(inv.DiscountAmount, cur), ["scope"] = "LINE_ITEM" } };
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["idempotency_key"] = key, ["order"] = order });
    }

    public static string InvoiceBody(Invoice inv, string locationId, string orderId, string customerId, string key, DateTime todayLocal)
    {
        var invoice = new Dictionary<string, object>
        {
            ["location_id"] = locationId,
            ["order_id"] = orderId,
            ["primary_recipient"] = new Dictionary<string, object> { ["customer_id"] = customerId },
            ["payment_requests"] = new List<object> { new Dictionary<string, object> { ["request_type"] = "BALANCE", ["due_date"] = DueDate(inv, todayLocal) } },
            ["delivery_method"] = "EMAIL",
            ["invoice_number"] = Cap(inv.Number, 191),
            ["accepted_payment_methods"] = new Dictionary<string, object>
            {
                ["card"] = true, ["bank_account"] = inv.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase),   // ACH: US only
                ["square_gift_card"] = false, ["buy_now_pay_later"] = false, ["cash_app_pay"] = false,
            },
        };
        var note = Cap(inv.Note.Replace("\r", " ").Replace("\n", " "), 4000);
        if (note.Length > 0) invoice["description"] = note;
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["idempotency_key"] = key, ["invoice"] = invoice });
    }

    public static string CustomerSearchBody(string email) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["query"] = new Dictionary<string, object> { ["filter"] = new Dictionary<string, object> { ["email_address"] = new Dictionary<string, object> { ["exact"] = email.Trim() } } },
        ["limit"] = 1,
    });

    public static string CustomerBody(Invoice inv, string key)
    {
        var d = new Dictionary<string, object> { ["idempotency_key"] = key, ["email_address"] = inv.ClientEmail.Trim() };
        var name = Cap(inv.ClientName, 500);
        if (name.Length > 0) d["company_name"] = name;
        return JsonSerializer.Serialize(d);
    }

    static string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static JsonElement O(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) ? v : default;
    static long Amount(JsonElement money) => O(money, "amount") is { ValueKind: JsonValueKind.Number } a && a.TryGetInt64(out var n) ? n : 0;

    static T? Try<T>(string json, Func<JsonElement, T?> read) where T : class
    {
        try { using var doc = JsonDocument.Parse(json); return read(doc.RootElement); } catch { return null; }
    }

    /// <summary>The customer id from a SearchCustomers or CreateCustomer response.</summary>
    public static string? CustomerId(string json) => Try(json, r =>
    {
        if (O(r, "customers") is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0 && S(list[0], "id") is { Length: > 0 } id) return id;
        return S(O(r, "customer"), "id") is { Length: > 0 } cid ? cid : null;
    });

    public static SquareOrder? ParseOrder(string json) => Try(json, r =>
    {
        var o = O(r, "order");
        var id = S(o, "id");
        return id.Length == 0 ? null : new SquareOrder(id, Amount(O(o, "total_money")), S(O(o, "total_money"), "currency"));
    });

    public static SquareLocation? ParseLocation(string json) => Try(json, r =>
    {
        var l = O(r, "location");
        var id = S(l, "id");
        return id.Length == 0 ? null : new SquareLocation(id, S(l, "currency"), S(l, "status"));
    });


    static SquareInvoiceState? ReadInvoice(JsonElement i)
    {
        var id = S(i, "id"); var status = S(i, "status");
        if (id.Length == 0 || status.Length == 0) return null;
        long paid = 0, due = 0; var cur = "";
        if (O(i, "payment_requests") is { ValueKind: JsonValueKind.Array } prs)
            foreach (var pr in prs.EnumerateArray())
            {
                paid += Amount(O(pr, "total_completed_amount_money"));
                due += Amount(O(pr, "computed_amount_money"));
                if (cur.Length == 0) cur = S(O(pr, "computed_amount_money"), "currency");
            }
        var version = O(i, "version") is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : 0;
        return new SquareInvoiceState(id, status, version, paid, due, cur, S(i, "public_url"), S(i, "invoice_number"), S(i, "location_id"), S(O(i, "primary_recipient"), "email_address"));
    }

    public static SquareInvoiceState? ParseInvoice(string json) => Try(json, r => ReadInvoice(O(r, "invoice")));

    /// <summary>The invoices in a SearchInvoices response.</summary>
    public static List<SquareInvoiceState> ParseInvoiceList(string json)
    {
        var list = new List<SquareInvoiceState>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (O(doc.RootElement, "invoices") is { ValueKind: JsonValueKind.Array } arr)
                foreach (var e in arr.EnumerateArray()) if (ReadInvoice(e) is { } st) list.Add(st);
        }
        catch { }
        return list;
    }

    public static string InvoiceSearchBody(string locationId, string customerId) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["query"] = new Dictionary<string, object> { ["filter"] = new Dictionary<string, object> { ["location_ids"] = new[] { locationId }, ["customer_ids"] = new[] { customerId } } },
        ["limit"] = 200,
    });

    /// <summary>Square ids are letters, digits, ':', '-' and '_'; anything else never goes into a request path.</summary>
    public static bool IsSafeId(string id) => id.Length is > 0 and <= 255 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_');

    /// <summary>The Square invoice really is this Tempo invoice: same number, and the same location when known.</summary>
    public static bool Belongs(Invoice inv, SquareInvoiceState s, string? locationId) =>
        s.Number == Cap(inv.Number, 191) && (locationId == null || s.LocationId == locationId);

    /// <summary>
    /// Brings Square's state onto the Tempo invoice: what Square has collected beyond what Tempo already recorded becomes a payment
    /// (never more than the balance), and the invoice is marked paid once it's covered. A lower amount later (a refund) never removes payments.
    /// Returns true when a payment was recorded.
    /// </summary>
    public static bool Apply(Invoice inv, SquareInvoiceState s, DateTime nowUtc)
    {
        if (inv.SquareInvoiceId.Length == 0 || s.Id != inv.SquareInvoiceId || !Belongs(inv, s, null)) return false;
        inv.SquareStatus = s.Status; inv.SquareVersion = s.Version;
        if (inv.Status != "Issued" || !s.Currency.Equals(inv.Currency, StringComparison.OrdinalIgnoreCase)) return false;
        var paid = StripeSync.FromMinor(s.PaidMinor, inv.Currency);
        var prefix = "square:" + s.Id + ":";
        var recorded = inv.Payments.Where(p => p.Reference.StartsWith(prefix, StringComparison.Ordinal)).Sum(p => p.Amount);
        var reference = prefix + s.PaidMinor.ToString(CultureInfo.InvariantCulture);
        if (paid <= recorded || inv.Payments.Any(p => p.Reference == reference)) return false;
        // e.g. part was paid by hand, then the client paid in full on Square: record only what was still owed
        var amount = Math.Min(paid - recorded, inv.Balance);
        if (amount <= 0) return false;
        inv.Payments.Add(new Payment { Amount = amount, DateUtc = nowUtc, Note = "Square", Reference = reference });
        if (inv.Balance <= 0) { inv.Status = "Paid"; inv.PaidUtc = nowUtc; }
        return true;
    }

    /// <summary>Sync is newest-wins per record: an edit from another PC that never saw the Square link must not drop it (or the invoice could be sent twice).</summary>
    public static void KeepLink(Invoice local, Invoice incoming)
    {
        if (local.SquareInvoiceId.Length == 0 || incoming.SquareInvoiceId.Length > 0) return;
        incoming.SquareInvoiceId = local.SquareInvoiceId; incoming.SquareStatus = local.SquareStatus; incoming.SquareVersion = local.SquareVersion;
        incoming.SquareFingerprint = local.SquareFingerprint; incoming.SquareSandbox = local.SquareSandbox;
        incoming.SquareAttempt = Math.Max(local.SquareAttempt, incoming.SquareAttempt);
    }

    /// <summary>Records from other PCs are data, not trust: a Square link that isn't a plain id is dropped.</summary>
    public static void SanitizeLink(Invoice inv)
    {
        inv.SquareInvoiceId ??= ""; inv.SquareStatus ??= ""; inv.SquareFingerprint ??= "";   // a hand-made file can send nulls
        if (inv.SquareInvoiceId.Length == 0 || IsSafeId(inv.SquareInvoiceId)) return;
        inv.SquareInvoiceId = ""; inv.SquareStatus = ""; inv.SquareFingerprint = ""; inv.SquareVersion = 0;
    }

    /// <summary>Square's own https pages only (squareup.com and its test site), so a link from a response can't open anything else.</summary>
    public static bool IsSquareUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort) return false;
        var h = u.IdnHost.ToLowerInvariant();
        return h is "squareup.com" or "squareupsandbox.com" || h.EndsWith(".squareup.com", StringComparison.Ordinal) || h.EndsWith(".squareupsandbox.com", StringComparison.Ordinal);
    }
}
