using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads;

/// <summary>Resolves virtual catalog paths beneath a static HTTP root or equivalent API route.</summary>
public sealed class HttpSource : IDownloadSource
{
    private readonly Uri _baseUrl;
    private readonly bool _preferIp;

    /// <summary>Creates a transport without issuing an unreliable HEAD preflight request.</summary>
    public HttpSource(Uri baseUrl, bool preferIp = false) { _baseUrl = SourceAddress.BaseUrl(baseUrl.AbsoluteUri); _preferIp = preferIp; }

    /// <inheritdoc />
    public string Kind => "http";

    /// <inheritdoc />
    public async Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = SourceAddress.RelativePath(path);
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var uri = new Uri(_baseUrl, escaped);
        return await SourceIpResolver.ResolveAsync(uri, _preferIp, cancellationToken).ConfigureAwait(false);
    }
}
