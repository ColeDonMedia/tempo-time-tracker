using System.Diagnostics;
using System.IO;

namespace Tempo;

/// <summary>Guards for paths and links that come from the data file (which a restored backup could have changed).</summary>
public static class SafeOpen
{
    /// <summary>A local (non-UNC) file with one of the given extensions, inside <paramref name="root"/> when one is given.</summary>
    public static bool IsAllowed(string path, string? root, params string[] extensions)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\") || path.StartsWith("//")) return false;
            var full = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(full) || new Uri(full).IsUnc) return false;
            if (!extensions.Any(e => full.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return false;
            if (root == null) return true;
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Opens the file with its default app, but only if <see cref="IsAllowed"/> says so.</summary>
    public static bool Open(string path, string? root, params string[] extensions)
    {
        if (!IsAllowed(path, root, extensions) || !File.Exists(path)) return false;
        try { Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    /// <summary>Pay-now links on invoices: empty, or an absolute https URL.</summary>
    public static bool IsPayLink(string url) =>
        url.Length == 0 || Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Contains('.');
}
