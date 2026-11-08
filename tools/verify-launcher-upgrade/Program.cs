using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Downloads;

var resume = args.FirstOrDefault() == "--resume-staging";
if (resume) args = args[1..];
if (args.Length is < 3 or > 6)
{
    Console.Error.WriteLine("Usage: [--resume-staging] <baseline-package> <expected-index> <output> [timeout-seconds:30..1800] [network-retries:0..3] [expected-visible-files:nonnegative]. Run with the baseline's Windows architecture.");
    return 2;
}
var phase = "prepare";
try
{
    var baseline = Path.GetFullPath(args[0]);
    var expectedPath = Path.GetFullPath(args[1]);
    var expectedBytes = File.ReadAllBytes(expectedPath);
    var output = Path.GetFullPath(args[2]);
    var seconds = args.Length >= 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 900;
    var maximumRetries = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0;
    int? expectedVisible = args.Length == 6 ? int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) : null;
    var visibleFiles = 0;
    var restoredVisibleFiles = 0;
    var networkRetries = 0;
    if (seconds is < 30 or > 1800 || maximumRetries is < 0 or > 3 || expectedVisible is < 0 || Directory.Exists(output) != resume || File.Exists(output))
        throw new InvalidOperationException("Use a fresh output, or explicitly resume an existing isolated output, with a bounded verification deadline.");
    if (output.StartsWith(Path.TrimEndingDirectorySeparator(baseline) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The acceptance output must be outside the baseline distribution.");
    var readerDigest = SHA256.HashData(File.ReadAllBytes(typeof(LauncherUpdateService).Assembly.Location));
    var baselineDigest = SHA256.HashData(File.ReadAllBytes(Path.Combine(baseline, "FantasyTale.Launcher.Core.dll")));
    if (!readerDigest.SequenceEqual(baselineDigest))
        throw new InvalidOperationException("The loaded reader differs from the older distribution.");
    var policy = LauncherUpdatePolicy.Load(baseline) ?? throw new InvalidDataException("The baseline has no configured update source.");
    var expected = LauncherRelease.Parse(policy, expectedBytes);
    var installation = Path.Combine(output, "installed");
    var data = Path.Combine(output, "data");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    var retainedFiles = new List<(string Source, string Destination)>();
    phase = resume ? "verify-baseline" : "copy-baseline";
    var directories = new Stack<string>();
    directories.Push(baseline);
    while (directories.TryPop(out var directory))
    {
        deadline.Token.ThrowIfCancellationRequested();
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Use a distribution without linked directories.");
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (!Path.GetFileName(child).StartsWith(".fantasytale", StringComparison.Ordinal)) directories.Push(child);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var relative = Path.GetRelativePath(baseline, file);
            if (Path.GetFileName(file).StartsWith(".fantasytale", StringComparison.Ordinal)) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Use a distribution without linked files.");
            var destination = Path.Combine(installation, relative);
            if (resume) retainedFiles.Add((file, destination));
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
    }
    await Parallel.ForEachAsync(retainedFiles,
        new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (file, ct) =>
    {
        await using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        if (!await HttpDownloadEngine.MatchesAsync(file.Destination, input.Length, digest, ct))
            throw new InvalidDataException("The resumed installation differs from the unchanged baseline.");
    });
    var changed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
    phase = "compare-content";
    await Parallel.ForEachAsync(expected.Files.GroupBy(file => file.Sha256),
        new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (group, ct) =>
        {
            foreach (var file in group)
                if (await HttpDownloadEngine.MatchesAsync(Path.Combine(installation, file.Path), file.Size, file.Sha256, ct)) return;
            changed.TryAdd(group.Key, 0);
        });
    using var handler = new TransferAuditHandler(expected.Files.Select(file => new KeyValuePair<string, string>(file.SourcePath, file.Sha256)))
    {
        InnerHandler = new SocketsHttpHandler { MaxConnectionsPerServer = 2, ConnectTimeout = TimeSpan.FromSeconds(15), UseCookies = false },
    };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    var stagedDestinations = 0;
    var reusedInstalledDestinations = 0;
    var stagingTime = Stopwatch.StartNew();
    phase = "stage";
    await using (var updates = new LauncherUpdateService(installation, data, http))
    {
        updates.SavePreferences(new(true, true));
        updates.StartMonitoring();
        while (updates.State.Status != "ready")
        {
            await Task.Delay(5000, deadline.Token);
            var tasks = updates.List();
            var task = tasks.Count > 0 ? tasks[0] : null;
            Console.WriteLine("update=" + updates.State.Status + " files=" + (task?.Files.Count(file => file.Stage == "verified") ?? 0) + "/" + (task?.Files.Count ?? 0));
            if (updates.State.Status == "failed")
            {
                var failure = updates.State.Failure is "network" or "disk" or "integrity" or "startup" ? updates.State.Failure : "unknown";
                Console.WriteLine("failure=" + failure);
                if (failure != "network" || networkRetries >= maximumRetries) break;
                Console.WriteLine("explicit-network-retry=" + ++networkRetries);
                if (task is null)
                {
                    // Discovery failures have no transfer to retry; obtain a selection first.
                    var checkedState = await updates.CheckAsync(deadline.Token);
                    if (checkedState.Status == "available")
                        await updates.DownloadAsync(checkedState.ManifestSha256!, deadline.Token);
                }
                else
                {
                    // The visible failure can precede worker completion by a short interval.
                    while (true)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        try { await updates.ActionAsync("retry"); break; }
                        catch (InvalidOperationException) when (updates.State.Status == "failed")
                        { await Task.Delay(50, deadline.Token); }
                    }
                }
            }
        }
        if (updates.State.Status != "ready" || updates.State.ManifestSha256 != expected.ManifestSha256)
            throw new InvalidOperationException("The old reader did not stage the expected release: " + updates.State.Status + "/" + updates.State.Failure);
        visibleFiles = updates.List().Single().Files.Count;
        if (expectedVisible is { } expectedCount && visibleFiles != expectedCount)
            throw new InvalidDataException("The staged task has an unexpected visible file count.");
        phase = "verify-staged-objects";
        string readyDirectory;
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try { readyDirectory = updates.ReadyDirectory(); break; }
            catch (InvalidOperationException) when (updates.State.Status == "ready")
            { await Task.Delay(50, deadline.Token); }
        }
        await Parallel.ForEachAsync(expected.Files,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (file, ct) =>
        {
            if (await HttpDownloadEngine.MatchesAsync(Path.Combine(readyDirectory, "objects", file.Sha256 + ".verified"), file.Size, file.Sha256, ct))
                Interlocked.Increment(ref stagedDestinations);
            else if (await HttpDownloadEngine.MatchesAsync(Path.Combine(installation, file.Path), file.Size, file.Sha256, ct))
                Interlocked.Increment(ref reusedInstalledDestinations);
            else throw new InvalidDataException("Neither staged nor installed bytes match the expected release.");
        });
        updates.SavePreferences(new(false, false));
    }
    stagingTime.Stop();
    if (handler.Objects.Keys.Any(hash => !changed.ContainsKey(hash)))
        throw new InvalidDataException("The old reader requested an unchanged payload object.");
    using var offline = new HttpClient(new OfflineHandler());
    phase = "offline-restoration";
    await using (var reopened = new LauncherUpdateService(installation, data, offline))
    {
        reopened.StartMonitoring();
        while (reopened.State.Status is not ("ready" or "failed")) await Task.Delay(100, deadline.Token);
        if (reopened.State.Status != "ready" || reopened.State.ManifestSha256 != expected.ManifestSha256)
            throw new InvalidDataException("The old reader could not restore the verified offline transfer.");
        restoredVisibleFiles = reopened.List().Single().Files.Count;
        if (restoredVisibleFiles != visibleFiles)
            throw new InvalidDataException("Offline restoration changed the visible file count.");
    }
    File.WriteAllText(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new
    {
        baselineReaderSha256 = Convert.ToHexString(readerDigest).ToLowerInvariant(),
        policy.Platform, policy.FeedPath, expected.Version, expected.Revision, expected.ManifestSha256,
        files = expected.Files.Count, totalBytes = expected.Files.Sum(file => file.Size),
        autoCheck = true, autoDownload = true, networkRetries, staged = true, offlineRestoration = true,
        changedObjects = changed.Count, requestedObjects = handler.Objects.Count,
        visibleFiles, restoredVisibleFiles,
        stagedDestinations, reusedInstalledDestinations, reusedOutput = resume,
        requestCountersScope = "current-run-only",
        objectResolutionRequests = handler.Resolutions.Values.Sum(), payloadGetRequests = handler.PayloadGets.Values.Sum(),
        stagingSeconds = stagingTime.Elapsed.TotalSeconds,
        unchangedObjectRequests = 0,
        installed = false, verifiedAtUtc = DateTimeOffset.UtcNow,
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("PASS older reader: automatic discovery, verified staging and offline restoration. No launcher or game was opened; no update was installed.");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Net.Http.HttpRequestException or OperationCanceledException or FormatException or OverflowException)
{
    // Transport exceptions can contain signed storage URLs; report their type only.
    Console.Error.WriteLine("Upgrade acceptance failed: " + error.GetType().Name + " during " + phase + ". No acceptance receipt was written.");
    return 1;
}

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("Network deliberately unavailable.");
}

sealed class TransferAuditHandler(IEnumerable<KeyValuePair<string, string>> sources) : DelegatingHandler
{
    private readonly Dictionary<string, string> _objects = sources.Distinct()
        .ToDictionary(source => source.Key.TrimStart('/'), source => source.Value, StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _resolved = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, int> Objects { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, int> Resolutions { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, int> PayloadGets { get; } = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? path = null;
        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/api/fs/get", StringComparison.Ordinal) == true
            && request.Content is not null)
        {
            using var metadata = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (metadata.RootElement.TryGetProperty("path", out var value)) path = value.GetString()?.TrimStart('/');
        }
        else if (request.Method == HttpMethod.Get && request.RequestUri is { } uri)
        {
            var candidate = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            path = _objects.Keys.FirstOrDefault(source => candidate == source || candidate.EndsWith("/" + source, StringComparison.Ordinal));
        }
        string? hash = null;
        if (path is not null) _objects.TryGetValue(path, out hash);
        if (hash is null && request.Method == HttpMethod.Get && request.RequestUri is { } direct)
            _resolved.TryGetValue(direct.AbsoluteUri, out hash);
        // Temporary storage addresses stay in memory; receipts contain only declared content hashes.
        if (hash is not null)
        {
            Objects.AddOrUpdate(hash, 1, (_, count) => count + 1);
            var counters = request.Method == HttpMethod.Get ? PayloadGets : Resolutions;
            counters.AddOrUpdate(hash, 1, (_, count) => count + 1);
        }
        var timer = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine($"transport={(hash is null ? "discovery" : "payload")} method={request.Method} failure={error.GetType().Name} seconds={timer.Elapsed.TotalSeconds:F2}");
            if (error is HttpRequestException transport)
            {
                var socket = transport.InnerException as System.Net.Sockets.SocketException
                    ?? transport.InnerException?.InnerException as System.Net.Sockets.SocketException;
                Console.WriteLine($"httpError={transport.HttpRequestError} socketError={socket?.SocketErrorCode.ToString() ?? "none"}");
            }
            throw;
        }
        if (hash is null)
            Console.WriteLine($"transport=discovery method={request.Method} http={(int)response.StatusCode} headersSeconds={timer.Elapsed.TotalSeconds:F2}");
        if (hash is not null && request.Method == HttpMethod.Post && response.IsSuccessStatusCode)
        {
            try
            {
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (metadata.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("raw_url", out var raw) && Uri.TryCreate(raw.GetString(), UriKind.Absolute, out var url))
                    _resolved[url.AbsoluteUri] = hash;
            }
            catch (JsonException) { /* The source adapter reports malformed metadata to the update service. */ }
        }
        return response;
    }
}
