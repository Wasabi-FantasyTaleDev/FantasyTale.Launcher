using System.Diagnostics;
using System.Net.Http.Headers;

namespace FantasyTale.Launcher.Downloads;

/// <summary>A configured public GitHub mirror with a URL-prefix endpoint.</summary>
/// <param name="Id">Stable preference key.</param>
/// <param name="Name">Short display name.</param>
/// <param name="Address">HTTPS origin accepting an appended original URL.</param>
public sealed record GitHubMirror(string Id, string Name, Uri Address);

/// <summary>The current bounded download measurement for one configured mirror.</summary>
/// <param name="Id">Configured mirror identity.</param>
/// <param name="State">Pending, testing, available or failed.</param>
/// <param name="Milliseconds">Sample transfer time, including connection establishment.</param>
/// <param name="BytesPerSecond">Measured transfer rate for the bounded sample.</param>
public sealed record GitHubMirrorResult(string Id, string State, long? Milliseconds = null, double? BytesPerSecond = null);

/// <summary>Measures configured mirrors without buffering packages and shares rankings between transports.</summary>
public sealed class GitHubMirrors : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _probe;
    private readonly byte[] _prefix;
    private readonly int _sampleBytes;
    private readonly TimeSpan _timeout;
    private readonly int _parallelism;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _resultsGate = new();
    private GitHubMirrorResult[] _results;
    private DateTimeOffset _measuredAt;
    private IReadOnlyList<GitHubMirror> _providers;
    private readonly HashSet<string> _retained = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<IReadOnlyList<GitHubMirror>>>? _refreshProviders;

    /// <summary>Creates an independent probe transport with deployment-owned endpoints and measurement bounds.</summary>
    public GitHubMirrors(IReadOnlyList<GitHubMirror> providers, Uri probe, byte[] prefix, int sampleBytes, TimeSpan timeout,
        int parallelism, HttpMessageHandler? handler = null,
        Func<CancellationToken, Task<IReadOnlyList<GitHubMirror>>>? refreshProviders = null)
    {
        if (providers.Count == 0 || providers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != providers.Count ||
            providers.Any(p => p.Id == "auto" || string.IsNullOrWhiteSpace(p.Id) || p.Address.Scheme != "https" || p.Address.UserInfo.Length > 0 ||
                p.Address.AbsolutePath != "/" || p.Address.Query.Length > 0 || p.Address.Fragment.Length > 0) ||
            probe.Scheme != "https" || !GitHubRequestRouting.IsGitHub(probe) || prefix.Length == 0 || sampleBytes < prefix.Length ||
            timeout <= TimeSpan.Zero || parallelism < 1)
            throw new ArgumentException("Invalid GitHub mirror configuration.");
        _providers = providers.ToArray(); _refreshProviders = refreshProviders; _probe = probe; _prefix = prefix.ToArray(); _sampleBytes = sampleBytes;
        _timeout = timeout; _parallelism = parallelism;
        _results = Providers.Select(p => new GitHubMirrorResult(p.Id, "pending")).ToArray();
        _http = new(handler ?? new SocketsHttpHandler { UseCookies = false, MaxConnectionsPerServer = 2, ConnectTimeout = timeout })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Gets the configured provider order before measurements are available.</summary>
    public IReadOnlyList<GitHubMirror> Providers { get { lock (_resultsGate) return _providers.ToArray(); } }

    /// <summary>Gets the configured per-attempt network deadline for mirror response headers.</summary>
    public TimeSpan RequestTimeout => _timeout;

    /// <summary>Returns a stable snapshot while measurements continue asynchronously.</summary>
    public IReadOnlyList<GitHubMirrorResult> Results { get { lock (_resultsGate) return _results.ToArray(); } }

    /// <summary>Checks a saved provider selection while allowing automatic ranking.</summary>
    public string ValidateSelection(string selection) => selection == "auto" || Providers.Any(p => p.Id == selection)
        ? selection : throw new ArgumentException("Unknown GitHub mirror selection.");

    /// <summary>Retains a previously selected directory host when the current fallback snapshot predates that node.</summary>
    public void RestoreSelection(string selection)
    {
        if (selection == "auto") return;
        lock (_resultsGate)
        {
            if (_providers.Any(provider => provider.Id == selection)) { _retained.Add(selection); return; }
        }
        if (!Uri.TryCreate("https://" + selection + "/", UriKind.Absolute, out var address) || address.IdnHost != selection ||
            address.HostNameType != UriHostNameType.Dns || address.AbsolutePath != "/" || !address.IsDefaultPort || address.UserInfo.Length > 0)
            throw new ArgumentException("Invalid saved mirror host.");
        lock (_resultsGate)
        {
            _retained.Add(selection);
            if (_providers.Any(provider => provider.Id == selection)) return;
            _providers = [.. _providers, new GitHubMirror(selection, selection, address)];
            _results = [.. _results, new GitHubMirrorResult(selection, "pending")];
        }
    }

    /// <summary>Tests all providers with limited concurrency, cancellation and bounded byte-range samples.</summary>
    public async Task MeasureAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await MeasureCoreAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task MeasureCoreAsync(CancellationToken ct)
    {
        if (_refreshProviders is not null)
        {
            try
            {
                var providers = await _refreshProviders(ct).ConfigureAwait(false);
                if (providers.Count > 0) lock (_resultsGate)
                {
                    _providers = providers.Concat(_providers.Where(provider => _retained.Contains(provider.Id)))
                        .DistinctBy(provider => provider.Id, StringComparer.Ordinal).ToArray();
                    _results = _providers.Select(p => new GitHubMirrorResult(p.Id, "pending")).ToArray();
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            { ct.ThrowIfCancellationRequested(); }
        }
        lock (_resultsGate) { _measuredAt = default; _results = _providers.Select(p => new GitHubMirrorResult(p.Id, "pending")).ToArray(); }
        await Parallel.ForEachAsync(Providers, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = _parallelism }, async (provider, token) =>
        {
            Update(new(provider.Id, "testing"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(_timeout);
            var watch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, GitHubRequestRouting.Route(_probe, provider.Address));
                request.Headers.Range = new RangeHeaderValue(0, _sampleBytes - 1);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType == "text/html") throw new InvalidDataException("Mirror returned a web page.");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var buffer = new byte[_sampleBytes]; var read = 0;
                while (read < buffer.Length)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(read), timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    read += count;
                }
                if (read < _prefix.Length || !buffer.AsSpan(0, _prefix.Length).SequenceEqual(_prefix))
                    throw new InvalidDataException("Mirror sample has an unexpected signature.");
                Update(new(provider.Id, "available", watch.ElapsedMilliseconds, read / Math.Max(watch.Elapsed.TotalSeconds, 0.001)));
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                Update(new(provider.Id, "failed"));
                token.ThrowIfCancellationRequested();
            }
        }).ConfigureAwait(false);
        lock (_resultsGate) _measuredAt = DateTimeOffset.UtcNow;
    }

    private void Update(GitHubMirrorResult result)
    {
        lock (_resultsGate) _results[Array.FindIndex(_results, row => row.Id == result.Id)] = result;
    }

    /// <summary>Measures before automatic release downloads and returns fastest mirrors first; callers append direct fallback.</summary>
    public async Task<IReadOnlyList<Uri>> RoutesAsync(Uri address, string selection, CancellationToken ct)
    {
        ValidateSelection(selection);
        if (selection == "auto")
        {
            var requestedAt = DateTimeOffset.UtcNow;
            var download = address.Host == "github.com" && (address.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal) || address.AbsolutePath.Contains("/archive/", StringComparison.Ordinal));
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (download && _measuredAt < requestedAt) await MeasureCoreAsync(ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        GitHubMirrorResult[] results; GitHubMirror[] providers; bool measured;
        lock (_resultsGate) { results = _results.ToArray(); providers = _providers.ToArray(); measured = _measuredAt != default; }
        var ranked = providers.OrderBy(p => p.Id == selection ? 0 : 1)
            .ThenBy(p => results.Single(r => r.Id == p.Id).State == "available" ? 0 : 1)
            .ThenByDescending(p => results.Single(r => r.Id == p.Id).BytesPerSecond ?? 0);
        return ranked.Where(p => selection != "auto" || !measured || results.Single(r => r.Id == p.Id).State == "available")
            .Select(p => GitHubRequestRouting.Route(address, p.Address)).ToArray();
    }

    /// <summary>Releases the probe client and coordination after transport activity has stopped.</summary>
    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
