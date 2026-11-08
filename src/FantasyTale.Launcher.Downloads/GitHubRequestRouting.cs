namespace FantasyTale.Launcher.Downloads;

/// <summary>Recognizes public GitHub resources supported by URL-prefix download mirrors.</summary>
public static class GitHubRequestRouting
{
    /// <summary>Prefixes the complete original HTTPS URL, preserving its escaped path and query.</summary>
    public static Uri Route(Uri address, Uri mirror) => new(mirror.AbsoluteUri.TrimEnd('/') + "/" + address.AbsoluteUri);

    /// <summary>Recognizes GitHub website, API and GitHub-owned content hosts without matching lookalike domains.</summary>
    public static bool IsGitHub(Uri address) => address.Host is "github.com" or "www.github.com" or "api.github.com"
        || address.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Applies the common GitHub routing preference to both metadata and file-transfer HTTP clients.</summary>
public sealed class GitHubRoutingHandler(Func<bool> enabled, Func<string> selection, GitHubMirrors mirrors, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    /// <summary>Leaves credentials and writes on their original origin and preserves range headers on retried downloads.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var address = request.RequestUri ?? throw new InvalidOperationException("A request URL is required.");
        if (!enabled() || !GitHubRequestRouting.IsGitHub(address) || address.Scheme != "https" ||
            request.Method != HttpMethod.Get || request.Content is not null || request.Headers.Authorization is not null ||
            request.Headers.Contains("Cookie") || request.Headers.Contains("Proxy-Authorization") || address.UserInfo.Length > 0)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        request.Options.TryGetValue(HttpDownloadRouting.Key, out var transfer);
        var routes = transfer is not null
            ? await transfer.RoutesAsync(() => mirrors.RoutesAsync(address, selection(), cancellationToken), address).ConfigureAwait(false)
            : new Queue<Uri>([.. await mirrors.RoutesAsync(address, selection(), cancellationToken).ConfigureAwait(false), address]);
        while (routes.TryDequeue(out var route))
        {
            if (route == address) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            using var attempt = new HttpRequestMessage(request.Method, route) { Version = request.Version, VersionPolicy = request.VersionPolicy };
            foreach (var header in request.Headers.Where(header => !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)))
                attempt.Headers.TryAddWithoutValidation(header.Key, header.Value);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(mirrors.RequestTimeout);
                var response = await base.SendAsync(attempt, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType != "text/html")
                { response.RequestMessage = request; return response; }
                response.Dispose();
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        throw new HttpRequestException("The GitHub download exhausted its routes.");
    }
}
