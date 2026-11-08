using System.Text.RegularExpressions;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Reads the public node list from a directory's current application scripts without executing remote code.</summary>
public static partial class GitHubMirrorDirectory
{
    [GeneratedRegex("src=\"([^\"]+\\.js)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Scripts();

    [GeneratedRegex("label:\"(?:contribute|search)\",value:\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Nodes();

    /// <summary>Discovers same-origin script URLs and merges their declared HTTPS mirror nodes with bounded parallel reads.</summary>
    public static async Task<IReadOnlyList<GitHubMirror>> FetchAsync(Uri directory, TimeSpan timeout, int parallelism, CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false, ConnectTimeout = timeout }) { Timeout = timeout };
        var page = await http.GetStringAsync(directory, ct).ConfigureAwait(false);
        var scripts = Scripts().Matches(page).Select(match => new Uri(directory, match.Groups[1].Value))
            .Where(uri => uri.Scheme == directory.Scheme && uri.Authority == directory.Authority && uri.UserInfo.Length == 0)
            .Distinct().ToArray();
        var found = new System.Collections.Concurrent.ConcurrentBag<GitHubMirror>();
        await Parallel.ForEachAsync(scripts, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct }, async (script, token) =>
        {
            string source;
            try { source = await http.GetStringAsync(script, token).ConfigureAwait(false); }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                return;
            }
            foreach (Match match in Nodes().Matches(source))
            {
                var host = match.Groups[1].Value;
                if (!Uri.TryCreate("https://" + host + "/", UriKind.Absolute, out var address) || address.UserInfo.Length > 0 ||
                    !address.IsDefaultPort || address.AbsolutePath != "/" || address.Query.Length > 0 || address.Fragment.Length > 0 ||
                    address.HostNameType != UriHostNameType.Dns) continue;
                found.Add(new(address.IdnHost, host, address));
            }
        }).ConfigureAwait(false);
        var result = found.DistinctBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase).OrderBy(provider => provider.Id, StringComparer.Ordinal).ToArray();
        return result.Length > 0 ? result : throw new InvalidDataException("The mirror directory did not publish a recognizable node list.");
    }
}
