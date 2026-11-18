using System.Net.Http;
using System.Text.Json;

namespace Tempo;

/// <summary>Windows' own signature check (WinVerifyTrust) for a downloaded file.</summary>
static class Authenticode
{
    [System.Runtime.InteropServices.DllImport("wintrust.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }

    public static bool IsValid(string path)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var fi = new WINTRUST_FILE_INFO { cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFile = System.Runtime.InteropServices.Marshal.AllocHGlobal(fi.cbStruct);
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(fi, pFile, false);
            var data = new WINTRUST_DATA { cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /* none */, dwUnionChoice = 1 /* file */, pFile = pFile };
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        catch { return false; }
        finally { System.Runtime.InteropServices.Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile); System.Runtime.InteropServices.Marshal.FreeHGlobal(pFile); }
    }
}

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
            using var http = Client(1_000_000);
            var expected = ExpectedHash(await http.GetStringAsync(su), name);
            if (expected == null) return null;
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempoUpdate");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(name));
            // stream to disk (no 200 MB buffer), hashing as we go, with the file locked against other writers
            using (var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                using (var resp = await http.GetAsync(iu, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    if (resp.Content.Headers.ContentLength is > MaxInstallerBytes) return null;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
                    var buf = new byte[81920]; long total = 0; int n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        total += n; if (total > MaxInstallerBytes) return null;
                        hash.AppendData(buf, 0, n); await dst.WriteAsync(buf.AsMemory(0, n));
                    }
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase)) { TryDelete(path); return null; }
            }
            if (!SignatureMatchesRunningApp(path)) { TryDelete(path); return null; }
            return path;
        }
        catch { return null; }
    }

    const long MaxInstallerBytes = 200_000_000;
    static void TryDelete(string p) { try { System.IO.File.Delete(p); } catch { } }

    /// <summary>
    /// When this copy of Tempo is code-signed, an update must carry a valid signature from the same certificate. That
    /// protects against a release published by someone who got hold of the GitHub account but not the signing key.
    /// Unsigned builds (no certificate configured) rely on HTTPS + the release checksum only.
    /// </summary>
    public static bool SignatureMatchesRunningApp(string installer)
    {
        var mine = SignerThumbprint(Environment.ProcessPath ?? "");
        if (mine == null) return true;
        return Authenticode.IsValid(installer) && SignerThumbprint(installer) == mine;
    }

    static string? SignerThumbprint(string file)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var c = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return c.Thumbprint;
        }
        catch { return null; }   // not signed
    }
}
