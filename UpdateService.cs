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

    /// <summary>The installer and checksum files attached to a release (only this repo's download URLs).</summary>
    public static ReleaseAssets ParseAssets(string json)
    {
        string? inst = null, name = null, sums = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return new(null, null, null);
            foreach (var a in assets.EnumerateArray())
            {
                var n = a.GetProperty("name").GetString() ?? ""; var u = a.GetProperty("browser_download_url").GetString() ?? "";
                if (!IsTrustedDownload(u)) continue;
                if (n.StartsWith("Tempo-Setup-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { inst = u; name = n; }
                else if (n.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = u;
            }
        }
        catch { }
        return new(inst, name, sums);
    }

    /// <summary>The SHA-256 for <paramref name="fileName"/> in a SHA256SUMS.txt, or null.</summary>
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var p = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 2 && p[1].Trim().Equals(fileName, StringComparison.OrdinalIgnoreCase) && p[0].Length == 64 && p[0].All(Uri.IsHexDigit)) return p[0].ToUpperInvariant();
        }
        return null;
    }

    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase);

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Asks GitHub for the latest release. Returns it only when it is newer. Never throws.</summary>
    public static async Task<(string Tag, string Url, ReleaseAssets Assets)?> CheckAsync()
    {
        try
        {
            using var http = Client(1_000_000);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(Api);
            var rel = ParseLatest(json);
            return rel is { } r && IsNewer(CurrentVersion, r.Tag) && IsTrustedUrl(r.Url) ? (r.Tag, r.Url, ParseAssets(json)) : null;
        }
        catch { return null; }
    }

    static HttpClient Client(long maxBytes)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = maxBytes };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-update-check");   // GitHub requires a User-Agent; nothing personal is sent
        return http;
    }

    /// <summary>True when Tempo runs from the installer's folder (so an installer update makes sense), not as a portable exe.</summary>
    public static bool IsInstalled =>
        Environment.ProcessPath is string p && p.StartsWith(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tempo") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads the release's installer and checks it against the release's SHA256SUMS.txt. Returns the file, or null if
    /// anything is missing, comes from elsewhere, or doesn't match.
    /// </summary>
    public static async Task<string?> DownloadInstallerAsync(ReleaseAssets a)
    {
        if (a.InstallerUrl is not string iu || a.SumsUrl is not string su || a.InstallerName is not string name) return null;
        if (!IsTrustedDownload(iu) || !IsTrustedDownload(su)) return null;
        try
        {
            using var http = Client(200_000_000);
            var expected = ExpectedHash(await http.GetStringAsync(su), name);
            if (expected == null) return null;
            var bytes = await http.GetByteArrayAsync(iu);
            if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return null;
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempoUpdate");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(name));
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch { return null; }
    }
}
