using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads;

/// <summary>Creates the shared path-based transport used by every public catalog, manifest and payload consumer.</summary>
public static class SourceTransport
{
    /// <summary>Validates a configured adapter and its root before persisting or using a source.</summary>
    public static void Validate(string kind, string baseUrl)
    {
        var root = SourceAddress.BaseUrl(baseUrl);
        if (kind == "github") _ = GitHubSource.ContentRoot(root);
        else if (kind is not ("http" or "alist")) throw new ArgumentException("Source transport must be alist, http or github.");
    }

    /// <summary>Returns an adapter without fetching metadata; callers use the same resolver for all package and listing types.</summary>
    public static IDownloadSource Open(HttpClient http, string kind, Uri baseUrl, bool preferIp = false) => kind switch
    {
        "http" => new HttpSource(baseUrl, preferIp),
        "alist" => new AListSource(http, baseUrl, preferIp: preferIp),
        "github" => new GitHubSource(baseUrl),
        _ => throw new ArgumentException("Source transport must be alist, http or github.", nameof(kind)),
    };
}

/// <summary>Resolves repository content, raw files and release assets through the common download-source contract.</summary>
public sealed class GitHubSource : IDownloadSource
{
    private readonly HttpSource _content;

    /// <summary>Accepts a GitHub repository, tree/blob root, raw content root, Pages root or release asset directory.</summary>
    public GitHubSource(Uri baseUrl) => _content = new HttpSource(ContentRoot(SourceAddress.BaseUrl(baseUrl.AbsoluteUri)));

    /// <inheritdoc />
    public string Kind => "github";

    /// <inheritdoc />
    public Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken) => _content.ResolveAsync(path, cancellationToken);

    internal static Uri ContentRoot(Uri root)
    {
        if (root.Scheme != "https") throw new ArgumentException("GitHub sources require HTTPS.");
        if (root.Host == "raw.githubusercontent.com" || root.Host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase)) return root;
        if (root.Host is not ("github.com" or "www.github.com")) throw new ArgumentException("Use a GitHub repository, raw content or Pages URL.");
        var parts = root.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("GitHub sources require an owner and repository.");
        parts[1] = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        if (parts.Length >= 5 && parts[2] == "releases" && parts[3] == "download") return root;
        if (parts.Length == 2) return new Uri($"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/HEAD/");
        if (parts.Length >= 4 && parts[2] is "tree" or "blob")
            return new Uri("https://raw.githubusercontent.com/" + string.Join('/', parts.Take(2).Concat(parts.Skip(3))) + "/");
        throw new ArgumentException("Use a repository URL, a tree URL with a ref, or a release download directory.");
    }
}
