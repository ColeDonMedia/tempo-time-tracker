using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Tempo;

/// <summary>
/// API keys the user pasted in (Stripe, GitHub, Jira, Asana). Encrypted with Windows DPAPI for this Windows user and kept in
/// their own file, outside tempo-data.json, so backups, sync files and problem reports never contain them.
/// </summary>
public static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? desc, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr desc, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
    const int CRYPTPROTECT_UI_FORBIDDEN = 1;
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Tempo.secrets.v1");

    static byte[]? Crypt(byte[] data, bool protect)
    {
        var inH = GCHandle.Alloc(data, GCHandleType.Pinned); var enH = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = inH.AddrOfPinnedObject() };
            var entropy = new Blob { Size = Entropy.Length, Data = enH.AddrOfPinnedObject() };
            var output = new Blob();
            var ok = protect ? CryptProtectData(ref input, "Tempo", ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                             : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
            if (!ok) return null;
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally { inH.Free(); enH.Free(); }
    }

    public static string PathIn(string dir) => System.IO.Path.Combine(dir, "secrets.dat");

    static Dictionary<string, string> Load(string dir)
    {
        try
        {
            var p = PathIn(dir);
            if (!System.IO.File.Exists(p)) return new();
            var plain = Crypt(System.IO.File.ReadAllBytes(p), false);
            return plain == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch { return new(); }   // damaged, or encrypted for a different Windows user: treat as no keys
    }

    public static void Set(string dir, string name, string? value)
    {
        var all = Load(dir);
        if (string.IsNullOrWhiteSpace(value)) all.Remove(name); else all[name] = value.Trim();
        var blob = Crypt(JsonSerializer.SerializeToUtf8Bytes(all), true) ?? throw new InvalidOperationException("Windows couldn't encrypt the key.");
        System.IO.Directory.CreateDirectory(dir);
        var tmp = PathIn(dir) + ".tmp";
        System.IO.File.WriteAllBytes(tmp, blob);
        System.IO.File.Move(tmp, PathIn(dir), true);
    }

    public static string? Get(string dir, string name) => Load(dir).TryGetValue(name, out var v) ? v : null;
    public static bool Has(string dir, string name) => Get(dir, name) != null;
}

public sealed record StripePayment(string SessionId, string Reference, decimal Amount, string Currency, DateTime PaidUtc);

/// <summary>Stripe Payment Links: tag each invoice's link with its number, then match completed checkouts back to invoices.</summary>
public static class StripeSync
{
    static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
        { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };
    static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase) { "BHD", "JOD", "KWD", "OMR", "TND" };

    /// <summary>Every reference Tempo puts on a link starts with this, so checkouts from a website or another tool never match an invoice.</summary>
    public const string Prefix = "tempo_";

    public static bool IsPaymentLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("buy.stripe.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stripe allows letters, digits, '-' and '_' (max 200) in client_reference_id.</summary>
    public static string ReferenceFor(string invoiceNumber)
    {
        var s = Prefix + new string(invoiceNumber.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return s.Length > 200 ? s[..200] : s;
    }

    /// <summary>Adds (or replaces) ?client_reference_id=INVOICE on a Stripe payment link. Other links are left alone.</summary>
    public static string WithReference(string url, string invoiceNumber)
    {
        if (!IsPaymentLink(url) || invoiceNumber.Length == 0) return url;
        var q = url.IndexOf('?');
        var path = q < 0 ? url : url[..q];
        var parts = q < 0 ? new List<string>() : url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("client_reference_id=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add("client_reference_id=" + ReferenceFor(invoiceNumber));
        return path + "?" + string.Join("&", parts);
    }

    public static decimal FromMinor(long amount, string currency) => ZeroDecimal.Contains(currency) ? amount : ThreeDecimal.Contains(currency) ? amount / 1000m : amount / 100m;

    /// <summary>Completed, paid checkouts that carry a reference (from GET /v1/checkout/sessions).</summary>
    public static List<StripePayment> ParseSessions(string json)
    {
        var list = new List<StripePayment>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
            foreach (var s in data.EnumerateArray())
            {
                string Str(string p) => s.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (Str("payment_status") != "paid" || Str("status") != "complete") continue;
                var reference = Str("client_reference_id");
                // only Tempo's own references, and only checkouts that came through a payment link
                if (!reference.StartsWith(Prefix, StringComparison.Ordinal) || Str("payment_link").Length == 0) continue;
                if (!s.TryGetProperty("amount_total", out var at) || at.ValueKind != JsonValueKind.Number) continue;
                var cur = Str("currency").ToUpperInvariant();
                var created = s.TryGetProperty("created", out var c) && c.TryGetInt64(out var secs) ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime : DateTime.UtcNow;
                list.Add(new StripePayment(Str("id"), reference, FromMinor(at.GetInt64(), cur), cur, created));
            }
        }
        catch { }
        return list;
    }

    public static List<Invoice> Apply(IEnumerable<StripePayment> payments, List<Invoice> invoices)
    {
        var changed = new List<Invoice>();
        // only open invoices that actually use a Stripe link; a reference shared by two invoices is ambiguous, so neither is touched
        var candidates = invoices.Where(i => i.Kind == "Invoice" && i.Status == "Issued" && IsPaymentLink(i.PayLink ?? "")).GroupBy(i => ReferenceFor(i.Number))
                                 .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in payments)
        {
            if (p.Amount <= 0 || p.SessionId.Length == 0) continue;
            if (!candidates.TryGetValue(p.Reference, out var inv) || !inv.Currency.Equals(p.Currency, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.PaidUtc < inv.IssuedUtc.AddDays(-1)) continue;   // paid before this invoice existed: not for it
            if (inv.Payments.Any(x => x.Reference == p.SessionId)) continue;
            inv.Payments.Add(new Payment { Amount = p.Amount, DateUtc = p.PaidUtc, Note = "Stripe", Reference = p.SessionId });
            if (inv.Balance <= 0) { inv.Status = "Paid"; inv.PaidUtc = p.PaidUtc; }
            if (!changed.Contains(inv)) changed.Add(inv);
        }
        return changed;
    }
}

public sealed record WorkItem(string Source, string Key, string Title, string Url, string Container);

/// <summary>Your assigned issues and tasks from GitHub, Jira and Asana, to use as entry descriptions.</summary>
public static class WorkItems
{
    static string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Titles are written by other people: one line, no control characters, at most 300 characters.</summary>
    static string Clean(string s)
    {
        var one = new string(s.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        while (one.Contains("  ")) one = one.Replace("  ", " ");
        return one.Length > 300 ? one[..300] : one;
    }

    public static List<WorkItem> ParseGitHub(string json)
    {
        var list = new List<WorkItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var i in doc.RootElement.EnumerateArray())
            {
                var repo = i.TryGetProperty("repository", out var r) ? S(r, "full_name") : "";
                var num = i.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                var title = Clean(S(i, "title"));
                if (title.Length == 0) continue;
                list.Add(new WorkItem("GitHub", repo.Length > 0 ? $"{repo}#{num}" : $"#{num}", title, S(i, "html_url"), repo));
            }
        }
        catch { }
        return list;
    }

    public static List<WorkItem> ParseJira(string json, string site)
    {
        var list = new List<WorkItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("issues", out var issues) || issues.ValueKind != JsonValueKind.Array) return list;
            foreach (var i in issues.EnumerateArray())
            {
                var key = S(i, "key");
                var f = i.TryGetProperty("fields", out var fv) ? fv : default;
                var summary = Clean(S(f, "summary"));
                var project = f.ValueKind == JsonValueKind.Object && f.TryGetProperty("project", out var p) ? S(p, "name") : "";
                if (key.Length == 0) continue;
                list.Add(new WorkItem("Jira", key, summary, $"{site.TrimEnd('/')}/browse/{Uri.EscapeDataString(key)}", project));
            }
        }
        catch { }
        return list;
    }

    public static List<WorkItem> ParseAsana(string json)
    {
        var list = new List<WorkItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
            foreach (var t in data.EnumerateArray())
            {
                var name = Clean(S(t, "name"));
                if (name.Length == 0) continue;
                var project = t.TryGetProperty("projects", out var ps) && ps.ValueKind == JsonValueKind.Array && ps.GetArrayLength() > 0 ? S(ps[0], "name") : "";
                list.Add(new WorkItem("Asana", S(t, "gid"), name, S(t, "permalink_url"), project));
            }
        }
        catch { }
        return list;
    }

    /// <summary>"acme.atlassian.net" or "https://jira.company.com" → https origin; refuses http and local-network addresses.</summary>
    public static string? NormalizeJiraSite(string url)
    {
        url = url.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        var n = CalendarFeeds.Normalize(url);
        if (n == null || !Uri.TryCreate(n, UriKind.Absolute, out var u)) return null;
        return $"https://{u.Host}" + (u.IsDefaultPort ? "" : ":" + u.Port.ToString(CultureInfo.InvariantCulture));
    }

    public static string Describe(WorkItem w) => w.Source == "Asana" ? w.Title : $"{w.Key} {w.Title}".Trim();
}
