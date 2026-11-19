using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Tempo;

/// <summary>
/// Talks to Stripe, GitHub, Jira and Asana with the user's own keys. Only fixed API hosts (or the user's own Jira site) are
/// contacted, responses are size-capped, redirects are not followed, and keys are never logged or shown again.
/// </summary>
/// <summary>
/// HTTP for addresses the user typed (Jira site, calendar feeds): no redirects, and the connection is refused if the name
/// resolves to this PC or the local network, so a public-looking name can't point Tempo at a router or intranet service.
/// </summary>
public static class SafeHttp
{
    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (ctx, ct) =>
        {
            var addrs = await System.Net.Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var ok = addrs.Where(a => !CalendarFeeds.IsPrivate(a)).ToArray();
            if (ok.Length == 0) throw new HttpRequestException("Refused: that address is on the local network.");
            var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(ok, ctx.DnsEndPoint.Port, ct); return new System.Net.Sockets.NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        },
    };
}

public static class IntegrationClient
{
    public const string Stripe = "stripe", GitHub = "github", Jira = "jira", JiraEmail = "jira-email", JiraSite = "jira-site", Asana = "asana", Square = "square";
    static string Dir => State.Dir;

    static HttpClient Client()
    {
        var http = new HttpClient(SafeHttp.Handler()) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 5_000_000 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-time-tracker");
        return http;
    }

    static async Task<(string? Body, string? Error)> Get(HttpClient http, string url, AuthenticationHeaderValue auth, string? accept = null)
    {
        var (b, e, _) = await Call(http, HttpMethod.Get, url, auth, null, accept);
        return (b, e);
    }

    /// <summary>One request. Status is the HTTP status (0 when the service couldn't be reached). Only Square's own error text is ever shown.</summary>
    static async Task<(string? Body, string? Error, int Status)> Call(HttpClient http, HttpMethod method, string url, AuthenticationHeaderValue auth, string? json, string? accept = null, bool square = false)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = auth;
            if (accept != null) req.Headers.Accept.ParseAdd(accept);
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            var code = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode) return (body, null, code);
            return (null, code switch
            {
                401 or 403 => Loc.T("The key was refused. Check it has the permissions listed."),
                404 => Loc.T("Not found. Check the address."),
                429 => Loc.T("Too many requests. Tempo will try again later."),
                400 or 409 or 422 when square && ServiceDetail(body) is string d => Loc.F("Square said: {0}", d),
                _ => Loc.F("The service answered with error {0}.", code),
            }, code);
        }
        catch (TaskCanceledException) { return (null, Loc.T("The service didn't answer in time."), 0); }
        catch { return (null, Loc.T("Couldn't reach the service. Check your internet connection."), 0); }
    }

    /// <summary>Square's own explanation of a refused request: one short line, no control or direction-changing characters.</summary>
    static string? ServiceDetail(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errs) || errs.ValueKind != System.Text.Json.JsonValueKind.Array || errs.GetArrayLength() == 0) return null;
            var e = errs[0];
            var d = e.TryGetProperty("detail", out var dv) && dv.ValueKind == System.Text.Json.JsonValueKind.String ? dv.GetString() ?? "" : "";
            d = new string(d.Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c).ToArray()).Trim();
            return d.Length == 0 ? null : d.Length > 200 ? d[..200] + "…" : d;
        }
        catch { return null; }
    }

    static AuthenticationHeaderValue Bearer(string key) => new("Bearer", key);
    static AuthenticationHeaderValue Basic(string user, string pass) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

    // ---------- connection tests (Settings) ----------
    public static async Task<string?> TestAsync(string service)
    {
        using var http = Client();
        switch (service)
        {
            case Stripe:
                return SecretStore.Get(Dir, Stripe) is string sk ? (await Get(http, "https://api.stripe.com/v1/checkout/sessions?limit=1", Bearer(sk))).Error : Loc.T("No key saved.");
            case GitHub:
                return SecretStore.Get(Dir, GitHub) is string gk ? (await Get(http, "https://api.github.com/user", Bearer(gk), "application/vnd.github+json")).Error : Loc.T("No key saved.");
            case Jira:
                if (SecretStore.Get(Dir, Jira) is not string jt || SecretStore.Get(Dir, JiraEmail) is not string je || WorkItems.NormalizeJiraSite(SecretStore.Get(Dir, JiraSite) ?? "") is not string site) return Loc.T("No key saved.");
                return (await Get(http, site + "/rest/api/3/myself", Basic(je, jt), "application/json")).Error;
            case Asana:
                return SecretStore.Get(Dir, Asana) is string ak ? (await Get(http, "https://app.asana.com/api/1.0/users/me", Bearer(ak))).Error : Loc.T("No key saved.");
            case Square:
                return SecretStore.Get(Dir, Square) is string qk ? (await Get(http, SquareApi + "/v2/locations/main", Bearer(qk))).Error : Loc.T("No key saved.");
        }
        return null;
    }

    public static bool HasAnyTaskSource => SecretStore.Has(Dir, GitHub) || SecretStore.Has(Dir, Jira) || SecretStore.Has(Dir, Asana);

    // ---------- your tasks ----------
    static (DateTime At, List<WorkItem> Items, List<string> Errors)? cache;

    /// <summary>Assigned, open work from every connected service (cached for 5 minutes).</summary>
    public static async Task<(List<WorkItem> Items, List<string> Errors)> TasksAsync(bool refresh = false)
    {
        if (!refresh && cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(5)) return (c.Items, c.Errors);
        var items = new List<WorkItem>(); var errors = new List<string>();
        using var http = Client();
        if (SecretStore.Get(Dir, GitHub) is string gk)
        {
            var (b, e) = await Get(http, "https://api.github.com/issues?filter=assigned&state=open&per_page=50", Bearer(gk), "application/vnd.github+json");
            if (b != null) items.AddRange(WorkItems.ParseGitHub(b)); else errors.Add("GitHub: " + e);
        }
        if (SecretStore.Get(Dir, Jira) is string jt && SecretStore.Get(Dir, JiraEmail) is string je && WorkItems.NormalizeJiraSite(SecretStore.Get(Dir, JiraSite) ?? "") is string site)
        {
            var jql = Uri.EscapeDataString("assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC");
            var (b, e) = await Get(http, $"{site}/rest/api/3/search/jql?jql={jql}&fields=summary,project&maxResults=50", Basic(je, jt), "application/json");
            if (b != null) items.AddRange(WorkItems.ParseJira(b, site)); else errors.Add("Jira: " + e);
        }
        if (SecretStore.Get(Dir, Asana) is string ak)
        {
            var (me, e) = await Get(http, "https://app.asana.com/api/1.0/users/me?opt_fields=workspaces.gid", Bearer(ak));
            if (me == null) errors.Add("Asana: " + e);
            else
            {
                var gids = new List<string>();
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(me);
                    foreach (var w in doc.RootElement.GetProperty("data").GetProperty("workspaces").EnumerateArray().Take(3))
                        if (w.GetProperty("gid").GetString() is string g && g.All(char.IsDigit)) gids.Add(g);
                }
                catch { }
                foreach (var g in gids)
                {
                    var (b, e2) = await Get(http, $"https://app.asana.com/api/1.0/tasks?assignee=me&workspace={g}&completed_since=now&limit=50&opt_fields=name,permalink_url,projects.name", Bearer(ak));
                    if (b != null) items.AddRange(WorkItems.ParseAsana(b)); else { errors.Add("Asana: " + e2); break; }
                }
            }
        }
        cache = (DateTime.UtcNow, items, errors);
        return (items, errors);
    }

    // ---------- Stripe: mark invoices paid ----------
    public static DateTime? LastStripeCheckUtc { get; private set; }
    public static string? LastStripeError { get; private set; }
    /// <summary>Not an error: e.g. "too many payments to check them all".</summary>
    public static string? StripeNote { get; private set; }

    /// <summary>True when the service itself rejected the key or address (as opposed to a network hiccup).</summary>
    public static bool IsRejection(string? error) => error != null && (error == Loc.T("The key was refused. Check it has the permissions listed.") || error == Loc.T("Not found. Check the address."));

    /// <summary>Looks for completed checkouts on open invoices that use a Stripe payment link. Returns the invoices that changed.</summary>
    public static async Task<List<Invoice>> CheckStripeAsync()
    {
        LastStripeCheckUtc = DateTime.UtcNow;
        if (SecretStore.Get(Dir, Stripe) is not string sk) return new();
        var open = State.Data.Invoices.Where(i => i.Kind == "Invoice" && i.Status == "Issued" && StripeSync.IsPaymentLink(i.PayLink)).ToList();
        if (open.Count == 0) return new();
        var since = new DateTimeOffset(open.Min(i => i.IssuedUtc).AddDays(-1), TimeSpan.Zero).ToUnixTimeSeconds();
        using var http = Client();
        var payments = new List<StripePayment>();
        string? after = null;
        bool more = false;
        for (int page = 0; page < 10; page++)
        {
            var url = $"https://api.stripe.com/v1/checkout/sessions?status=complete&limit=100&created[gte]={since}" + (after != null ? "&starting_after=" + Uri.EscapeDataString(after) : "");
            var (b, e) = await Get(http, url, Bearer(sk));
            if (b == null) { LastStripeError = e; return new(); }
            payments.AddRange(StripeSync.ParseSessions(b));
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(b);
                var data = doc.RootElement.GetProperty("data");
                more = doc.RootElement.TryGetProperty("has_more", out var hm) && hm.GetBoolean() && data.GetArrayLength() > 0;
                if (!more) break;
                after = data[data.GetArrayLength() - 1].GetProperty("id").GetString();
            }
            catch { more = false; break; }
        }
        // a very busy Stripe account: say so instead of silently missing older payments
        LastStripeError = null;
        StripeNote = more ? Loc.T("Your Stripe account has more than 1,000 payments since the oldest open invoice; older ones weren't checked. Record those by hand.") : null;
        var changed = StripeSync.Apply(payments, State.Data.Invoices);
        if (changed.Count > 0) State.Notify();
        return changed;
    }

    // ---------- Square: send invoices, bring payments back ----------
    static string SquareApi => SquareSync.Api(State.Data.Settings.SquareSandbox);
    public static DateTime? LastSquareCheckUtc { get; private set; }
    public static string? LastSquareError { get; private set; }
    static readonly HashSet<string> squareBusy = new();
    public static bool SquareBusy(Invoice inv) => squareBusy.Contains(inv.Id);

    static Task<(string? Body, string? Error, int Status)> Sq(HttpClient http, HttpMethod method, string path, string token, string? json = null) =>
        Call(http, method, SquareApi + path, Bearer(token), json, null, square: true);

    static string Unreadable => Loc.T("Square sent an answer Tempo couldn't read.");

    /// <summary>The Square invoice by id. Missing is true only when Square says it doesn't exist (404).</summary>
    static async Task<(SquareInvoiceState? State, string? Error, bool Missing)> GetSquareInvoice(HttpClient http, string token, string id)
    {
        if (!SquareSync.IsSafeId(id)) return (null, Loc.T("This invoice's Square link is damaged, so Tempo won't use it."), false);
        var (b, e, code) = await Sq(http, HttpMethod.Get, "/v2/invoices/" + Uri.EscapeDataString(id), token);
        if (b == null) return (null, e, code == 404);
        return SquareSync.ParseInvoice(b) is { } s && s.Id == id ? (s, null, false) : (null, Unreadable, false);
    }

    static void Link(Invoice inv, SquareInvoiceState s, string fp, bool sandbox)
    {
        inv.SquareInvoiceId = s.Id; inv.SquareVersion = s.Version; inv.SquareStatus = s.Status; inv.SquareFingerprint = fp; inv.SquareSandbox = sandbox;
    }

    static void Unlink(Invoice inv)
    {
        inv.SquareInvoiceId = ""; inv.SquareStatus = ""; inv.SquareFingerprint = ""; inv.SquareVersion = 0; inv.SquareAttempt++;
    }

    /// <summary>
    /// Makes (or reuses) the Square draft for this invoice: finds or creates the customer, makes sure Square has no invoice with
    /// this number yet, then creates the order and the draft invoice. Nothing is emailed. Returns the amount Square will collect
    /// (minor units) and the email Square will send to, or the reason it couldn't.
    /// </summary>
    public static async Task<(string? Error, long TotalMinor, string Recipient)> PrepareSquareAsync(Invoice inv)
    {
        if (SecretStore.Get(Dir, Square) is not string token) return (Loc.T("No key saved."), 0, "");
        if (!squareBusy.Add(inv.Id)) return (Loc.T("Square is already working on this invoice."), 0, "");
        try
        {
            var sandbox = State.Data.Settings.SquareSandbox;
            using var http = Client();
            var (lb, le, _) = await Sq(http, HttpMethod.Get, "/v2/locations/main", token);
            if (lb == null) return (le, 0, "");
            if (SquareSync.ParseLocation(lb) is not { } loc || !SquareSync.IsSafeId(loc.Id)) return (Unreadable, 0, "");
            if (SquareSync.CannotSend(inv, loc.Currency) is string why) return (why, 0, "");
            var fp = SquareSync.Fingerprint(inv);
            var expected = SquareSync.ToMinor(inv.GrandTotal, inv.Currency);
            var alreadySent = Loc.T("This invoice was already sent with Square.");

            if (inv.SquareInvoiceId.Length > 0)
            {
                if (inv.SquareSandbox != sandbox) return (Loc.T("This invoice is linked to your other Square account (test or live). Connect that account again in Settings → Integrations to use it."), 0, "");
                var (st, ge, missing) = await GetSquareInvoice(http, token, inv.SquareInvoiceId);
                if (st == null && !missing) return (ge, 0, "");
                if (st != null && !SquareSync.Belongs(inv, st, loc.Id)) st = null;   // points at some other Square invoice: never use it
                else if (st != null && st.Status != "DRAFT") { SquareSync.Apply(inv, st, DateTime.UtcNow); State.Notify(); return (alreadySent, 0, ""); }
                else if (st != null && inv.SquareFingerprint == fp && st.DueMinor == expected)
                {
                    inv.SquareVersion = st.Version; inv.SquareStatus = st.Status; State.Notify();   // the draft may have been touched in Square
                    return (null, st.DueMinor, st.Email.Length > 0 ? st.Email : inv.ClientEmail.Trim());
                }
                else if (st != null)
                {
                    // the invoice changed since the draft was made: replace the draft so Square bills what Tempo shows
                    var (_, de, _) = await Sq(http, HttpMethod.Delete, $"/v2/invoices/{Uri.EscapeDataString(st.Id)}?version={st.Version}", token);
                    if (de != null) return (de, 0, "");
                }
                Unlink(inv); State.Notify();
            }

            var (cb, ce, _) = await Sq(http, HttpMethod.Post, "/v2/customers/search", token, SquareSync.CustomerSearchBody(inv.ClientEmail));
            if (cb == null) return (ce, 0, "");
            var customer = SquareSync.CustomerId(cb);
            if (customer == null)
            {
                var (nb, ne, _) = await Sq(http, HttpMethod.Post, "/v2/customers", token, SquareSync.CustomerBody(inv, SquareSync.CustomerKey(inv.ClientEmail)));
                if (nb == null) return (ne, 0, "");
                customer = SquareSync.CustomerId(nb);
            }
            if (customer == null || !SquareSync.IsSafeId(customer)) return (Unreadable, 0, "");

            // never a second invoice with this number: another PC (or a lost link) may have sent it already
            var (sb, se, _) = await Sq(http, HttpMethod.Post, "/v2/invoices/search", token, SquareSync.InvoiceSearchBody(loc.Id, customer));
            if (sb == null) return (se, 0, "");
            // a canceled or failed one with the same number doesn't count: sending again is the point
            foreach (var found in SquareSync.ParseInvoiceList(sb).Where(f => SquareSync.Belongs(inv, f, loc.Id) && SquareSync.IsSafeId(f.Id) && f.Status is not ("CANCELED" or "FAILED")))
            {
                if (found.Status != "DRAFT")
                {
                    Link(inv, found, "", sandbox); SquareSync.Apply(inv, found, DateTime.UtcNow); State.Notify();
                    return (alreadySent, 0, "");
                }
                var (_, de, _) = await Sq(http, HttpMethod.Delete, $"/v2/invoices/{Uri.EscapeDataString(found.Id)}?version={found.Version}", token);   // an old draft nobody can see in Tempo
                if (de != null) return (de, 0, "");
            }

            SquareOrder? order = null;
            foreach (var asLines in new[] { false, true })
            {
                var (ob, oe, _) = await Sq(http, HttpMethod.Post, "/v2/orders", token, SquareSync.OrderBody(inv, loc.Id, customer, asLines, SquareSync.OrderKey(inv, fp, asLines)));
                if (ob == null) return (oe, 0, "");
                order = SquareSync.ParseOrder(ob);
                if (order == null || !SquareSync.IsSafeId(order.Id)) return (Unreadable, 0, "");
                if (order.TotalMinor == expected) break;   // Square's own tax rounding can differ by a cent; then taxes go in as fixed lines
            }
            if (order!.TotalMinor != expected)
                return (Loc.F("Square worked out {0} instead of {1}. Nothing was sent.", Fmt.Money(StripeSync.FromMinor(order.TotalMinor, inv.Currency), inv.Currency), Fmt.Money(inv.GrandTotal, inv.Currency)), 0, "");

            var due = SquareSync.DueDate(inv, State.TodayLocal);
            var (ib, ie, _) = await Sq(http, HttpMethod.Post, "/v2/invoices", token, SquareSync.InvoiceBody(inv, loc.Id, order.Id, customer, SquareSync.InvoiceKey(inv, fp, due), State.TodayLocal));
            if (ib == null) return (ie, 0, "");
            if (SquareSync.ParseInvoice(ib) is not { } created || !SquareSync.IsSafeId(created.Id) || !SquareSync.Belongs(inv, created, loc.Id)) return (Unreadable, 0, "");
            Link(inv, created, fp, sandbox);
            State.Notify();
            return (null, expected, created.Email.Length > 0 ? created.Email : inv.ClientEmail.Trim());
        }
        finally { squareBusy.Remove(inv.Id); }
    }

    /// <summary>Publishes the draft: Square emails the invoice to the client. Only after the user confirmed.</summary>
    public static async Task<string?> PublishSquareAsync(Invoice inv)
    {
        if (SecretStore.Get(Dir, Square) is not string token) return Loc.T("No key saved.");
        if (!SquareSync.IsSafeId(inv.SquareInvoiceId) || inv.SquareSandbox != State.Data.Settings.SquareSandbox) return Loc.T("Not found. Check the address.");
        if (!squareBusy.Add(inv.Id)) return Loc.T("Square is already working on this invoice.");
        try
        {
            using var http = Client();
            var body = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = inv.SquareVersion, ["idempotency_key"] = SquareSync.PublishKey(inv, inv.SquareFingerprint) });
            var (b, e, _) = await Sq(http, HttpMethod.Post, $"/v2/invoices/{Uri.EscapeDataString(inv.SquareInvoiceId)}/publish", token, body);
            if (b == null)
            {
                // the answer may have been lost after Square did send it: look before reporting a failure
                var (st, _, _) = await GetSquareInvoice(http, token, inv.SquareInvoiceId);
                if (st == null || st.Status == "DRAFT") return e;
                SquareSync.Apply(inv, st, DateTime.UtcNow); State.Notify();
                return null;
            }
            if (SquareSync.ParseInvoice(b) is { } sent && sent.Id == inv.SquareInvoiceId) SquareSync.Apply(inv, sent, DateTime.UtcNow);
            else inv.SquareStatus = "UNPAID";
            State.Notify();
            return null;
        }
        finally { squareBusy.Remove(inv.Id); }
    }

    /// <summary>Cancels the invoice in Square (a draft is deleted), so the client can't pay something voided or deleted in Tempo.</summary>
    public static async Task<string?> CancelSquareAsync(Invoice inv)
    {
        if (SecretStore.Get(Dir, Square) is not string token) return Loc.T("No key saved.");
        if (inv.SquareSandbox != State.Data.Settings.SquareSandbox) return Loc.T("This invoice is linked to your other Square account (test or live). Connect that account again in Settings → Integrations to use it.");
        if (!squareBusy.Add(inv.Id)) return Loc.T("Square is already working on this invoice.");
        try
        {
        using var http = Client();
        var (st, ge, missing) = await GetSquareInvoice(http, token, inv.SquareInvoiceId);
        if (st == null) return missing ? null : ge;
        if (!SquareSync.Belongs(inv, st, null)) return Unreadable;
        if (st.Status is "PAID" or "PARTIALLY_PAID" or "PARTIALLY_REFUNDED") { inv.SquareStatus = st.Status; State.Notify(); return Loc.T("Your client has already paid this invoice in Square. Refund it there if needed."); }
        if (st.Status is "CANCELED" or "REFUNDED" or "FAILED") { inv.SquareStatus = st.Status; State.Notify(); return null; }
        var (b, e, _) = st.Status == "DRAFT"
            ? await Sq(http, HttpMethod.Delete, $"/v2/invoices/{Uri.EscapeDataString(st.Id)}?version={st.Version}", token)
            : await Sq(http, HttpMethod.Post, $"/v2/invoices/{Uri.EscapeDataString(st.Id)}/cancel", token, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = st.Version }));
        if (b == null) return e;
        if (st.Status == "DRAFT") { inv.SquareInvoiceId = ""; inv.SquareStatus = ""; } else inv.SquareStatus = "CANCELED";
        State.Notify();
        return null;
        }
        finally { squareBusy.Remove(inv.Id); }
    }

    /// <summary>Square's current payment page for the invoice (the link changes over time), or null.</summary>
    public static async Task<string?> SquareLinkAsync(Invoice inv)
    {
        if (SecretStore.Get(Dir, Square) is not string token || inv.SquareSandbox != State.Data.Settings.SquareSandbox) return null;
        using var http = Client();
        var (st, _, _) = await GetSquareInvoice(http, token, inv.SquareInvoiceId);
        return st != null && SquareSync.Belongs(inv, st, null) && SquareSync.IsSquareUrl(st.PublicUrl) ? new Uri(st.PublicUrl).AbsoluteUri : null;
    }

    // drafts are still checked: a send whose answer got lost shows up here once Square has it
    static readonly HashSet<string> SquareClosed = new() { "CANCELED", "FAILED", "REFUNDED", "PAID" };

    /// <summary>Checks open invoices that were sent with Square. Returns the ones where a payment was recorded.</summary>
    public static async Task<List<Invoice>> CheckSquareAsync()
    {
        LastSquareCheckUtc = DateTime.UtcNow;
        var changed = new List<Invoice>();
        if (SecretStore.Get(Dir, Square) is not string token) return changed;
        var sandbox = State.Data.Settings.SquareSandbox;
        var open = State.Data.Invoices.Where(i => i.Kind == "Invoice" && i.Status == "Issued" && i.SquareInvoiceId.Length > 0 && i.SquareSandbox == sandbox && !SquareClosed.Contains(i.SquareStatus) && !squareBusy.Contains(i.Id))
                                      .OrderByDescending(i => i.IssuedUtc).Take(50).ToList();
        if (open.Count == 0) { LastSquareError = null; return changed; }
        using var http = Client();
        bool touched = false;
        foreach (var inv in open)
        {
            var (st, e, missing) = await GetSquareInvoice(http, token, inv.SquareInvoiceId);
            if (st == null) { if (!missing) { LastSquareError = e; if (touched) State.Notify(); return changed; } continue; }
            var before = inv.SquareStatus;
            if (SquareSync.Apply(inv, st, DateTime.UtcNow)) changed.Add(inv);
            touched |= before != inv.SquareStatus || changed.Contains(inv);
        }
        LastSquareError = null;
        if (touched) State.Notify();
        return changed;
    }
}
