namespace SabakaLang.AspNet;

internal static class PathGuard
{
    /// <summary>Resolves a relative path under <paramref name="root"/>; null if it would escape the root.</summary>
    public static string? Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\0')) return null;

        relative = relative.Replace('\\', '/').TrimStart('/');
        if (relative.Split('/').Any(s => s == "..")) return null;

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }
}
