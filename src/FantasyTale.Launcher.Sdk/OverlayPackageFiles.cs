namespace FantasyTale.Launcher.Sdk;

/// <summary>The outcome of resolving one overlay package URL.</summary>
/// <param name="Status">HTTP status to answer with: 200, 400, 403, 404 or 405.</param>
/// <param name="Path">Absolute file to stream when <paramref name="Status"/> is 200; otherwise null.</param>
/// <param name="ContentType">Media type for the file, or an empty string for errors.</param>
/// <param name="Immutable">Whether the file name is content-addressed, so a cached copy can never go stale.</param>
public readonly record struct OverlayPackageFile(int Status, string? Path, string ContentType, bool Immutable)
{
    /// <summary>Response headers for this result, ending without a trailing line break.</summary>
    public string Headers(string allowedOrigin) =>
        (Status == 200 ? "Content-Type: " + ContentType + "\r\nCache-Control: " + (Immutable ? "public, max-age=31536000, immutable" : "no-cache")
            : "Content-Type: text/plain\r\nCache-Control: no-store")
        + "\r\nX-Content-Type-Options: nosniff\r\nAccess-Control-Allow-Origin: " + allowedOrigin;
}

/// <summary>
/// Read-only file access for overlay pages at <c>https://packages.fantasytale.local/&lt;package id&gt;/&lt;relative path&gt;</c>.
/// Only registered package folders are reachable; traversal, absolute paths, links, directories and non-GET requests are refused.
/// </summary>
public sealed class OverlayPackageFiles
{
    /// <summary>The virtual host that serves package files.</summary>
    public const string Host = "packages.fantasytale.local";

    private static readonly System.Text.RegularExpressions.Regex IdPattern = new("^[a-z0-9.-]{1,96}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private volatile Dictionary<string, string> _roots = new(StringComparer.Ordinal);

    /// <summary>Builds the URL a page uses for a package file; both parts are validated as <see cref="Resolve"/> will.</summary>
    public static string Url(string packageId, string relativePath)
    {
        if (!IdPattern.IsMatch(packageId ?? "")) throw new ArgumentException("Invalid package id.", nameof(packageId));
        if (!TryParseRelative(relativePath ?? "", out var segments)) throw new ArgumentException("Invalid package file path.", nameof(relativePath));
        return "https://" + Host + "/" + packageId + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// Replaces the served folders. Each key is a package id; each value an existing absolute directory that is not a link.
    /// Throws without changing the current set when any entry is invalid.
    /// </summary>
    public void SetRoots(IReadOnlyDictionary<string, string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, folder) in roots)
        {
            if (!IdPattern.IsMatch(id)) throw new ArgumentException("Invalid package id.", nameof(roots));
            if (!System.IO.Path.IsPathRooted(folder)) throw new ArgumentException("Package folders must be absolute.", nameof(roots));
            var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
            if (!Directory.Exists(full) || IsLink(full)) throw new ArgumentException("A package folder is missing or is a link.", nameof(roots));
            next.Add(id, full);
        }
        _roots = next;
    }

    /// <summary>Resolves a request to a file under a registered package folder, or to the status that refuses it.</summary>
    public OverlayPackageFile Resolve(string method, string uri)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return Refuse(405);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps
            || !string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase) || !parsed.IsDefaultPort || parsed.UserInfo.Length != 0)
            return Refuse(400);
        // Parsing has already collapsed dot segments in the literal path; encoded separators and dots are checked after decoding.
        string decoded;
        try { decoded = Uri.UnescapeDataString(parsed.AbsolutePath); }
        catch (UriFormatException) { return Refuse(400); }
        if (decoded.Length < 2 || decoded[0] != '/') return Refuse(400);
        var slash = decoded.IndexOf('/', 1);
        if (slash < 0) return Refuse(404);
        var id = decoded[1..slash];
        if (!IdPattern.IsMatch(id)) return Refuse(400);
        if (!TryParseRelative(decoded[(slash + 1)..], out var segments)) return Refuse(segments.Count == 0 && decoded.Length == slash + 1 ? 404 : 400);
        if (!_roots.TryGetValue(id, out var root)) return Refuse(404);

        var current = root;
        foreach (var segment in segments)
        {
            current = System.IO.Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) return Refuse(404);
            if (IsLink(current)) return Refuse(403);
        }
        if (!File.Exists(current)) return Refuse(404);
        var name = segments[^1];
        return new(200, current, ContentTypeFor(System.IO.Path.GetExtension(name)), IsContentAddressed(name));
    }

    /// <summary>The media type for a file extension; unknown extensions are served as opaque bytes.</summary>
    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".json" => "application/json; charset=utf-8",
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".otf" => "font/otf",
        ".ttf" => "font/ttf",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static OverlayPackageFile Refuse(int status) => new(status, null, "", false);

    // A stem of at least 16 lower-case hexadecimal digits names its own content, so the bytes behind it never change.
    private static bool IsContentAddressed(string name)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        return stem.Length >= 16 && stem.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool TryParseRelative(string path, out List<string> segments)
    {
        segments = [];
        if (path.Length is 0 or > 1024) return false;
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.Length > 255 || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(['\\', ':', '*', '?', '"', '<', '>', '|', '\0']) >= 0 || part.Any(char.IsControl))
                return false;
            segments.Add(part);
        }
        return true;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
