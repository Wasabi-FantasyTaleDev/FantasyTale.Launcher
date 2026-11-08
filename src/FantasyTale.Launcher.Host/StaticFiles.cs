namespace FantasyTale.Launcher.Host;

/// <summary>
/// Serves the built frontend and game media through the host's reserved resource origin.
/// Only allowlisted files inside the frontend, game and imported artwork roots are reachable.
/// </summary>
internal static class StaticFiles
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
    };

    /// <summary>Opens the file for <paramref name="url"/>; <c>/games/...</c> maps to the games folder.</summary>
    public static Stream? Open(string webRoot, string gamesRoot, string assetOrigin, string url, out string contentType, string? serverMediaRoot = null)
    {
        contentType = "text/plain";
        var origin = new Uri(assetOrigin);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != origin.Scheme
            || uri.Host != origin.Host || uri.Port != origin.Port || uri.UserInfo.Length != 0) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        // Returning an empty stream also handles misses locally instead of falling through to DNS.
        if (path.IndexOfAny(['\\', ':', '\0']) >= 0 || path.Split('/').Any(p => p is "." or "..")) return Stream.Null;
        var (root, relative) = path.StartsWith("games/", StringComparison.Ordinal) ? (gamesRoot, path[6..]) : (webRoot, path);
        if (path.StartsWith("server-media/", StringComparison.Ordinal))
        {
            if (serverMediaRoot is null || !Core.Servers.ServerArtworkStore.IsReference("/" + path)) return Stream.Null;
            (root, relative) = (serverMediaRoot, path[13..]);
        }
        if (relative.Length == 0) relative = "index.html";
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)
            || !Types.TryGetValue(Path.GetExtension(full), out var type)) return Stream.Null;
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return Stream.Null;
        contentType = type;
        return File.OpenRead(full);
    }
}
