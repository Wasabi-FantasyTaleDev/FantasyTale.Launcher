using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

internal static class PublicArchiveAcceptance
{
    private static readonly JsonSerializerOptions ReceiptJson = new() { WriteIndented = true };

    internal static async Task<int> InspectReadyAsync(string baselineArgument, string outputArgument)
    {
        var baseline = Path.GetFullPath(baselineArgument);
        var output = Path.GetFullPath(outputArgument);
        Require(Hash(await File.ReadAllBytesAsync(typeof(LauncherUpdateService).Assembly.Location)) ==
            Hash(await File.ReadAllBytesAsync(Path.Combine(baseline, "FantasyTale.Launcher.Core.dll"))), "The loaded reader differs from the distribution.");
        using var offline = new HttpClient(new OfflineArchiveHandler());
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var service = new LauncherUpdateService(Path.Combine(output, "installed"), Path.Combine(output, "data"), offline);
        service.StartMonitoring();
        while (service.State.Status is not ("ready" or "failed")) await Task.Delay(50, deadline.Token);
        var files = service.List().SingleOrDefault()?.Files ?? [];
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            service.State.Status, service.State.Failure, service.State.FailureReason, service.State.HttpStatus,
            service.State.ManifestSha256, files = files.Count, verifiedFiles = files.Count(file => file.Stage == "verified"),
            received = files.Sum(file => file.Received), size = files.Sum(file => file.Size), publicNetwork = false,
        }, ReceiptJson));
        return service.State.Status == "ready" ? 0 : 1;
    }

    internal static async Task<int> RunAsync(string baselineArgument, string indexArgument, string outputArgument, bool resumeExisting = false)
    {
        var phase = "prepare";
        try
        {
            var baseline = Path.GetFullPath(baselineArgument);
            var output = Path.GetFullPath(outputArgument);
            Require(!File.Exists(output) && Directory.Exists(output) == resumeExisting &&
                !output.StartsWith(Path.TrimEndingDirectorySeparator(baseline) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Use a fresh output outside the distribution.");
            var readers = new Dictionary<string, string>();
            foreach (var assembly in new[] { typeof(LauncherUpdateService).Assembly, typeof(HttpDownloadEngine).Assembly, typeof(IDownloadSource).Assembly })
            {
                var name = Path.GetFileName(assembly.Location);
                var hash = Hash(await File.ReadAllBytesAsync(assembly.Location));
                Require(hash == Hash(await File.ReadAllBytesAsync(Path.Combine(baseline, name))), "The loaded reader differs from the distribution.");
                readers.Add(name, hash);
            }
            var decoder = Path.Combine("archive", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "7z.dll");
            Require(Hash(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, decoder))) ==
                Hash(await File.ReadAllBytesAsync(Path.Combine(baseline, decoder))), "The archive decoder differs from the distribution.");
            var policy = LauncherUpdatePolicy.Load(baseline) ?? throw new InvalidDataException("The distribution has no update policy.");
            var indexPath = Path.GetFullPath(indexArgument);
            var release = LauncherRelease.Parse(policy, await File.ReadAllBytesAsync(indexPath));
            var changedPaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            await Parallel.ForEachAsync(release.Files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (file, token) =>
            {
                if (!await HttpDownloadEngine.MatchesAsync(Path.Combine(baseline, file.Path), file.Size, file.Sha256, token))
                    changedPaths.TryAdd(file.Path, 0);
            });
            var companionBytes = await File.ReadAllBytesAsync(indexPath + ".archive.toml");
            var companion = TomlSerializer.Deserialize<TomlTable>(System.Text.Encoding.UTF8.GetString(companionBytes))!;
            Require((string)companion["manifest_sha256"] == release.ManifestSha256, "The companion selects another index.");
            var volumes = ((TomlTableArray)companion["files"]).Select(row =>
                (Path: (string)row["source_path"], Size: (long)row["size"], Hash: (string)row["sha256"])).ToArray();
            Require(volumes.Length > 0 && volumes.All(volume => volume.Size > 0), "The expected archive has no volumes.");
            var installation = Path.Combine(output, "installed");
            var dataRoot = Path.Combine(output, "data");
            if (resumeExisting)
            {
                foreach (var original in Directory.EnumerateFiles(baseline, "*", SearchOption.AllDirectories))
                {
                    var installed = Path.Combine(installation, Path.GetRelativePath(baseline, original));
                    Require(File.Exists(installed) && Hash(await File.ReadAllBytesAsync(original)) == Hash(await File.ReadAllBytesAsync(installed)),
                        "The retained output no longer contains the unchanged distribution.");
                }
            }
            else CopyDistribution(baseline, installation);
            var initialPartialBytes = Directory.Exists(dataRoot)
                ? Directory.EnumerateFiles(dataRoot, "*.part", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0;
            var originalPolicy = await File.ReadAllBytesAsync(Path.Combine(installation, LauncherUpdatePolicy.FileName));
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var handler = new PublicArchiveHandler(policy.FeedPath, release, volumes.Select(volume => volume.Path), Hash(companionBytes))
            {
                InnerHandler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), MaxConnectionsPerServer = 4, UseCookies = false },
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            var timer = Stopwatch.StartNew();
            int visibleFiles;
            int restoredVisibleFiles;
            string ready;
            phase = "public-discovery";
            await using (var service = new LauncherUpdateService(installation, dataRoot, http))
            {
                service.SavePreferences(new(false, false));
                var selected = await service.CheckAsync(deadline.Token);
                Require(selected.Status == "available" && selected.ManifestSha256 == release.ManifestSha256, "Discovery did not select the expected public release.");
                Require(handler.CompanionReads == 0 && handler.ArchiveReads == 0 && handler.FailedIncrementalReads == 0,
                    "Discovery requested a payload or archive companion.");
                phase = "public-archive-staging";
                await service.DownloadAsync(release.ManifestSha256, deadline.Token);
                while (service.State.Status is not ("ready" or "failed"))
                {
                    await Task.Delay(5000, deadline.Token);
                    var task = service.List().Single();
                    Console.WriteLine($"update={service.State.Status} stage={task.Stage} received={task.Files.Sum(file => file.Received)} size={task.Files.Sum(file => file.Size)}");
                }
                Require(service.State.Status == "ready", "The public archive was not staged.");
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try { ready = service.ReadyDirectory(); break; }
                    catch (InvalidOperationException) when (service.State.Status == "ready") { await Task.Delay(25, deadline.Token); }
                }
                Require(handler.FailedIncrementalReads >= 2 && handler.CompanionReads == 1 && handler.ArchiveReads == volumes.Length,
                    "The reader did not retry incremental transfer before selecting and downloading the archive.");
                visibleFiles = service.List().Single().Files.Count;
                Require(visibleFiles == volumes.Length, "The task exposes extracted files instead of archive volumes.");
                phase = "verify-public-archive-and-expanded-objects";
                foreach (var volume in volumes)
                    Require(await HttpDownloadEngine.MatchesAsync(Path.Combine(ready, "archive-fallback", ".fantasytale-install", volume.Hash + ".verified"),
                        volume.Size, volume.Hash, deadline.Token), "A downloaded archive differs from the expected companion.");
                foreach (var group in release.Files.GroupBy(file => file.Sha256))
                {
                    Require(group.Select(file => file.Size).Distinct().Count() == 1, "A content identity has inconsistent sizes.");
                    var file = group.First();
                    Require(await HttpDownloadEngine.MatchesAsync(Path.Combine(ready, "objects", file.Sha256 + ".verified"),
                        file.Size, file.Sha256, deadline.Token), "An expanded object differs from the selected index.");
                }
            }
            phase = "unchanged-installation";
            foreach (var file in Directory.EnumerateFiles(baseline, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(baseline, file);
                Require(Hash(await File.ReadAllBytesAsync(file, deadline.Token)) ==
                    Hash(await File.ReadAllBytesAsync(Path.Combine(installation, relative), deadline.Token)), "Staging changed an installed distribution file.");
            }
            var preservedPolicy = await File.ReadAllBytesAsync(Path.Combine(installation, LauncherUpdatePolicy.FileName), deadline.Token);
            Require(originalPolicy.SequenceEqual(preservedPolicy),
                "The archive changed the installed source policy.");
            phase = "offline-restoration";
            using var offline = new HttpClient(new OfflineArchiveHandler());
            await using (var reopened = new LauncherUpdateService(installation, dataRoot, offline))
            {
                reopened.StartMonitoring();
                while (reopened.State.Status is not ("ready" or "failed")) await Task.Delay(50, deadline.Token);
                Require(reopened.State.Status == "ready" && reopened.State.ManifestSha256 == release.ManifestSha256,
                    "The public archive could not be restored without network access.");
                // Older readers reconstruct the ready task from changed destinations, not transport volumes.
                var restoredFiles = reopened.List().Single().Files;
                restoredVisibleFiles = restoredFiles.Count;
                Require(restoredFiles.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(changedPaths.Keys) &&
                    restoredFiles.All(file => file.Stage == "verified" && file.Received == file.Size),
                    "Offline restoration does not describe exactly the verified changed destinations.");
            }
            var receipt = new
            {
                verifiedAtUtc = DateTimeOffset.UtcNow, platform = RuntimeInformation.ProcessArchitecture.ToString(), readers,
                fixtureOnly = false, publicNetwork = true, incrementalFailureInjected = true, resumedExistingOutput = resumeExisting, initialPartialBytes,
                policy.Kind, policy.FeedPath, release.Version, release.Revision, release.ManifestSha256,
                files = release.Files.Count, totalBytes = release.Files.Sum(file => file.Size),
                uniqueObjects = release.Files.Select(file => file.Sha256).Distinct().Count(),
                archiveVolumes = volumes.Select(volume => new { sourcePath = volume.Path, size = volume.Size, sha256 = volume.Hash }),
                handler.FailedIncrementalReads, handler.CompanionReads, handler.ArchiveReads, handler.ArchiveRangeReads,
                visibleFiles, restoredVisibleFiles, changedDestinations = changedPaths.Count,
                offlineRestoration = true, sourcePolicyUnchanged = true, installationUnchanged = true,
                installed = false, stagingSeconds = timer.Elapsed.TotalSeconds,
            };
            await File.WriteAllTextAsync(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(receipt, ReceiptJson));
            Console.WriteLine("PASS public archive fallback: shipped reader, real bytes, complete verification and offline restoration. No update was installed.");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or OperationCanceledException or KeyNotFoundException)
        {
            // Storage responses can contain signed URLs; retain only the failing phase and exception class.
            Console.Error.WriteLine($"Public archive acceptance failed: {error.GetType().Name} during {phase}. No acceptance receipt was written.");
            return 1;
        }
    }

    private static void CopyDistribution(string source, string destination)
    {
        Require((File.GetAttributes(source) & FileAttributes.ReparsePoint) == 0, "The distribution contains a linked directory.");
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDistribution(directory, Path.Combine(destination, Path.GetFileName(directory)));
        foreach (var file in Directory.EnumerateFiles(source))
        {
            Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0, "The distribution contains a linked file.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    private sealed class OfflineArchiveHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Acceptance network deliberately disabled.");
    }
}

internal sealed class PublicArchiveHandler(string feed, LauncherRelease release, IEnumerable<string> archives, string companionHash) : DelegatingHandler
{
    private readonly string _companion = feed + ".archive.toml";
    private readonly Dictionary<string, ClientReleaseFile> _objects = release.Files.GroupBy(file => file.SourcePath)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly HashSet<string> _archives = archives.ToHashSet(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _resolved = new(StringComparer.Ordinal);
    private int _failedIncrementalReads, _companionReads, _archiveReads, _archiveRangeReads;
    internal int FailedIncrementalReads => Volatile.Read(ref _failedIncrementalReads);
    internal int CompanionReads => Volatile.Read(ref _companionReads);
    internal int ArchiveReads => Volatile.Read(ref _archiveReads);
    internal int ArchiveRangeReads => Volatile.Read(ref _archiveRangeReads);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isResolution = request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/api/fs/get", StringComparison.Ordinal) == true;
        string? path = null;
        if (isResolution)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            path = document.RootElement.GetProperty("path").GetString()?.TrimStart('/');
        }
        else if (request.RequestUri is { } uri && request.Method == HttpMethod.Get)
        {
            if (!_resolved.TryGetValue(uri.AbsoluteUri, out path))
            {
                var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
                path = _objects.Keys.Concat(_archives).Append(_companion).FirstOrDefault(source => relative == source || relative.EndsWith("/" + source, StringComparison.Ordinal));
            }
        }
        if (path is not null && _objects.TryGetValue(path, out var file))
        {
            if (isResolution)
            {
                var synthetic = "https://unavailable-payload.invalid/" + file.Sha256;
                _resolved[synthetic] = path;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { code = 200, data = new { raw_url = synthetic, size = file.Size, is_dir = false } }) };
            }
            Interlocked.Increment(ref _failedIncrementalReads);
            return new(HttpStatusCode.ServiceUnavailable);
        }
        if (path == _companion && FailedIncrementalReads < 2)
            throw new InvalidDataException("Archive selection preceded incremental retries.");
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return response;
        if (isResolution && path is not null)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("raw_url", out var raw) && Uri.TryCreate(raw.GetString(), UriKind.Absolute, out var uri))
                _resolved[uri.AbsoluteUri] = path;
        }
        else if (request.Method == HttpMethod.Get && path == _companion)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != companionHash)
                throw new InvalidDataException("The public archive companion differs from the expected publication.");
            Interlocked.Increment(ref _companionReads);
        }
        else if (request.Method == HttpMethod.Get && path is not null && _archives.Contains(path))
        {
            Interlocked.Increment(ref _archiveReads);
            if (request.Headers.Range is not null) Interlocked.Increment(ref _archiveRangeReads);
        }
        return response;
    }
}
