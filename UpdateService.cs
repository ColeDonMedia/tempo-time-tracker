using System.Net.Http;
using System.Text.Json;

namespace Tempo;

/// <summary>Opt-in check for a newer release on GitHub. It never downloads or runs anything: it only offers to open the release page.</summary>
public static class UpdateService
{
    public const string Repo = "ColeDonMedia/tempo-time-tracker";
    const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";

    /// <summary>True when <paramref name="tag"/> (e.g. "v2.7.0") is a plain release version above <paramref name="current"/>.</summary>
    public static bool IsNewer(string current, string tag)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        if (t.Split('.').Length != 3 || !Version.TryParse(t, out var latest)) return false;   // "2.6", "3.0.0-beta.1", "latest" are not releases
        return Version.TryParse(current, out var cur) && latest > new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
    }

    public static (string Tag, string Url)? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
            if (r.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
            var tag = r.GetProperty("tag_name").GetString(); var url = r.GetProperty("html_url").GetString();
            return string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url) ? null : (tag, url);
        }
        catch { return null; }
    }

    /// <summary>Only https pages of this repository on github.com are ever opened.</summary>
    public static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.StartsWith("/" + Repo + "/releases/", StringComparison.OrdinalIgnoreCase);

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Asks GitHub for the latest release. Returns it only when it is newer. Never throws.</summary>
    public static async Task<(string Tag, string Url)?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_000_000 };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-update-check");   // GitHub requires a User-Agent; nothing personal is sent
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(Api);
            var rel = ParseLatest(json);
            return rel is { } r && IsNewer(CurrentVersion, r.Tag) && IsTrustedUrl(r.Url) ? r : null;
        }
        catch { return null; }
    }
}
