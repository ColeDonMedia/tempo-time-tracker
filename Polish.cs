namespace Tempo;

public class CalendarFeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";          // the calendar's secret iCal address
}

/// <summary>Live calendars through their secret iCal address (Google, Outlook, iCloud all offer one). Opt-in, read-only.</summary>
public static class CalendarFeeds
{
    /// <summary>Accepts https:// and webcal:// addresses; returns the https form, or null.</summary>
    public static string? Normalize(string url)
    {
        url = url.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || u.IsLoopback) return null;
        if (System.Net.IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip)) { if (IsPrivate(ip)) return null; }
        else if (!u.Host.Contains('.') || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return null;
        return u.ToString();
    }

    /// <summary>Loopback, private, link-local and similar addresses: a calendar feed has no business on the local network.</summary>
    public static bool IsPrivate(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast || ip.Equals(System.Net.IPAddress.IPv6None) || ip.Equals(System.Net.IPAddress.IPv6Any);
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224;
    }

    public static string CacheFile(string dir, CalendarFeed f) =>
        System.IO.Path.Combine(dir, "calendars", new string(f.Id.Where(char.IsLetterOrDigit).ToArray()) + ".ics");

    const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>Downloads each feed (at most every 30 minutes) into the data folder. Failures keep the last good copy.</summary>
    public static async Task RefreshAsync(string dir, IEnumerable<CalendarFeed> feeds, bool force = false)
    {
        // no redirects: a feed must not bounce us to an address we'd have refused
        using var handler = SafeHttp.Handler();
        using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = MaxBytes };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Tempo-calendar");
        foreach (var f in feeds.ToList())
        {
            var path = CacheFile(dir, f);
            try
            {
                if (!force && System.IO.File.Exists(path) && DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(30)) continue;
                if (Normalize(f.Url) is not string url) continue;
                var text = await http.GetStringAsync(url);
                if (!text.Contains("BEGIN:VCALENDAR")) continue;   // a login page or error, not a calendar
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, text);
            }
            catch { }
        }
    }

    /// <summary>Timed events from all cached feeds that overlap the range.</summary>
    public static List<(CalendarFeed Feed, CalEvent Event)> Events(string dir, IEnumerable<CalendarFeed> feeds, DateTime fromUtc, DateTime toUtc)
    {
        var list = new List<(CalendarFeed, CalEvent)>();
        foreach (var f in feeds)
        {
            var path = CacheFile(dir, f);
            if (!System.IO.File.Exists(path)) continue;
            try { list.AddRange(IcsImport.Parse(System.IO.File.ReadAllText(path), State.Tz, fromUtc, toUtc).Where(e => !e.AllDay).Select(e => (f, e))); }
            catch { }
        }
        return list;
    }
}

/// <summary>"Report a problem": a pre-filled GitHub issue the user reviews before sending.</summary>
public static class Problem
{
    public static string IssueUrl(string version, string os, string log, string userProfile, string userName = "", string machine = "", string syncFolder = "")
    {
        if (!string.IsNullOrEmpty(syncFolder)) log = log.Replace(syncFolder, "%SYNCFOLDER%", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(userProfile)) log = log.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (machine.Length > 1) log = log.Replace(machine, "%COMPUTERNAME%", StringComparison.OrdinalIgnoreCase);
        if (userName.Length > 1) log = System.Text.RegularExpressions.Regex.Replace(log, $@"\b{System.Text.RegularExpressions.Regex.Escape(userName)}\b", "%USERNAME%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        log = log.Trim();
        if (log.Length > 3000) log = "…" + log[^3000..];   // the newest part of the log matters most; links over ~8 KB get cut off
        var body = $"**What happened?**\n\n(Describe what you were doing.)\n\n**Tempo** {version}\n**Windows** {os}\n\n**Error log (your user folder is hidden)**\n```\n{(log.Length == 0 ? "(no errors logged)" : log)}\n```\n";
        return $"https://github.com/{UpdateService.Repo}/issues/new?title={Uri.EscapeDataString("Problem: ")}&body={Uri.EscapeDataString(body)}";
    }
}

public sealed record ReleaseAssets(string? InstallerUrl, string? InstallerName, string? SumsUrl);
