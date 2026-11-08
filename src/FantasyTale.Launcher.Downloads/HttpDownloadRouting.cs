namespace FantasyTale.Launcher.Downloads;

// A transfer keeps one route plan across response-body retries so failed nodes are
// attempted once and mirror measurements are not restarted for every byte range.
internal sealed class HttpDownloadRouting
{
    internal static readonly HttpRequestOptionsKey<HttpDownloadRouting> Key = new("FantasyTale.DownloadRouting");
    private Queue<Uri>? _routes;

    internal CancellationTokenSource? HeaderDeadline { get; set; }
    internal TimeSpan HeaderTimeout { get; } = TimeSpan.FromSeconds(30);
    internal bool HasNext => _routes is { Count: > 0 };

    internal async Task<Queue<Uri>> RoutesAsync(Func<Task<IReadOnlyList<Uri>>> prepare, Uri original)
    {
        if (_routes is not null) return _routes;
        // Probes have their own per-node deadlines; they precede the actual
        // request-header deadline and remain cancellable by the transfer owner.
        HeaderDeadline?.CancelAfter(Timeout.InfiniteTimeSpan);
        try { _routes = new([.. await prepare().ConfigureAwait(false), original]); }
        finally { HeaderDeadline?.CancelAfter(HeaderTimeout); }
        return _routes;
    }
}
