using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Tempo;

/// <summary>
/// Talks to Stripe, GitHub, Jira and Asana with the user's own keys. Only fixed API hosts (or the user's own Jira site) are
/// contacted, responses are size-capped, redirects are not followed, and keys are never logged or shown again.
/// </summary>
public static class IntegrationClient
{
    public const string Stripe = "stripe", GitHub = "github", Jira = "jira", JiraEmail = "jira-email", JiraSite = "jira-site", Asana = "asana";
    static string Dir => State.Dir;

    static HttpClient Client()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 5_000_000 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-time-tracker");
        return http;
    }

    static async Task<(string? Body, string? Error)> Get(HttpClient http, string url, AuthenticationHeaderValue auth, string? accept = null)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = auth;
            if (accept != null) req.Headers.Accept.ParseAdd(accept);
            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return (body, null);
            return (null, (int)resp.StatusCode switch
            {
                401 or 403 => Loc.T("The key was refused. Check it has the permissions listed."),
                404 => Loc.T("Not found. Check the address."),
                429 => Loc.T("Too many requests. Tempo will try again later."),
                _ => Loc.F("The service answered with error {0}.", (int)resp.StatusCode),
            });
        }
        catch (TaskCanceledException) { return (null, Loc.T("The service didn't answer in time.")); }
        catch { return (null, Loc.T("Couldn't reach the service. Check your internet connection.")); }
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
        for (int page = 0; page < 5; page++)
        {
            var url = $"https://api.stripe.com/v1/checkout/sessions?status=complete&limit=100&created[gte]={since}" + (after != null ? "&starting_after=" + Uri.EscapeDataString(after) : "");
            var (b, e) = await Get(http, url, Bearer(sk));
            if (b == null) { LastStripeError = e; return new(); }
            payments.AddRange(StripeSync.ParseSessions(b));
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(b);
                var data = doc.RootElement.GetProperty("data");
                if (!doc.RootElement.TryGetProperty("has_more", out var more) || !more.GetBoolean() || data.GetArrayLength() == 0) break;
                after = data[data.GetArrayLength() - 1].GetProperty("id").GetString();
            }
            catch { break; }
        }
        LastStripeError = null;
        var changed = StripeSync.Apply(payments, State.Data.Invoices);
        if (changed.Count > 0) State.Notify();
        return changed;
    }
}
