using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

if (args is ["--public-archive", var publicBaseline, var publicIndex, var publicOutput])
    return await PublicArchiveAcceptance.RunAsync(publicBaseline, publicIndex, publicOutput);
if (args is ["--resume-public-archive", var resumeBaseline, var resumeIndex, var resumeOutput])
    return await PublicArchiveAcceptance.RunAsync(resumeBaseline, resumeIndex, resumeOutput, resumeExisting: true);
if (args is ["--inspect-ready", var inspectionBaseline, var inspectionOutput])
    return await PublicArchiveAcceptance.InspectReadyAsync(inspectionBaseline, inspectionOutput);

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: <unchanged-baseline-package> <fresh-output>. Use the baseline's Windows architecture.");
    return 2;
}
try
{
    var baseline = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (Directory.Exists(output) || File.Exists(output) ||
        output.StartsWith(Path.TrimEndingDirectorySeparator(baseline) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Use a fresh output outside the original distribution.");
    var readers = new Dictionary<string, string>();
    foreach (var assembly in new[] { typeof(LauncherUpdateService).Assembly, typeof(HttpDownloadEngine).Assembly, typeof(IDownloadSource).Assembly })
    {
        var name = Path.GetFileName(assembly.Location);
        var hash = Hash(File.ReadAllBytes(assembly.Location));
        Require(hash == Hash(File.ReadAllBytes(Path.Combine(baseline, name))), "A loaded assembly differs from the shipped reader.");
        readers.Add(name, hash);
    }
    Directory.CreateDirectory(output);
    var results = new List<object>();
    foreach (var kind in new[] { "http", "alist" })
    foreach (var scenario in new[] { "incremental", "zip", "split-zip", "wrong-release", "corrupt-transfer", "wrong-file", "missing-file", "unexpected-file", "duplicate-file", "traversal" })
    {
        var root = Path.Combine(output, kind + "-" + scenario);
        var installation = Path.Combine(root, "installed");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(installation);
        var policyText = $"""
            schema = 1
            kind = "{kind}"
            base_url = "https://fallback-fixture.invalid/"
            feed_path = "launcher/current.toml"
            minimum_revision = 1
            """;
        var policyFile = Path.Combine(installation, LauncherUpdatePolicy.FileName);
        await File.WriteAllTextAsync(policyFile, policyText);
        using var handler = new FallbackHandler(scenario);
        using var http = new HttpClient(handler);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var timer = Stopwatch.StartNew();
        var accepted = scenario is "incremental" or "zip" or "split-zip";
        string finalState;
        var restored = false;
        await using (var service = new LauncherUpdateService(installation, data, http))
        {
            service.SavePreferences(new(false, false));
            var selected = await service.CheckAsync(deadline.Token);
            Require(selected.Status == "available", "The shipped reader rejected the fixture index.");
            Require(handler.ArchiveRequests == 0 && handler.CompanionRequests == 0 && handler.IncrementalRequests == 0,
                "Discovery requested payloads or archive fallback metadata.");
            await service.DownloadAsync(selected.ManifestSha256!, deadline.Token);
            while (service.State.Status is not ("ready" or "failed")) await Task.Delay(20, deadline.Token);
            finalState = service.State.Status;
            Require(finalState == (accepted ? "ready" : "failed"), "The archive fallback produced an unexpected outcome.");
            Require(handler.IncrementalRequests >= (scenario == "incremental" ? 1 : 2), "Fallback ran without retrying incremental transfer.");
            Require(handler.CompanionRequests == (scenario == "incremental" ? 0 : 1), "The archive selection was not requested exactly once after incremental failure.");
            Require(handler.ArchiveRequests == (scenario is "incremental" or "wrong-release" ? 0 : scenario == "split-zip" ? 2 : 1),
                "The archive transfer count differs from the selected fallback volumes.");
            if (accepted)
            {
                string staged;
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try { staged = service.ReadyDirectory(); break; }
                    catch (InvalidOperationException) { await Task.Delay(10, deadline.Token); }
                }
                foreach (var (path, bytes) in FallbackHandler.Payloads)
                {
                    var stagedFile = Path.Combine(staged, "objects", Hash(bytes) + ".verified");
                    Require(File.ReadAllBytes(stagedFile).SequenceEqual(bytes), "A staged object differs from the selected index.");
                }
                if (scenario != "incremental") Require(service.List().Single().Files.Count == (scenario == "split-zip" ? 2 : 1),
                    "Archive fallback exposes extracted payloads as individual downloads.");
            }
            foreach (var path in FallbackHandler.Payloads.Keys)
                Require(!File.Exists(Path.Combine(installation, path)), "Staging modified the installation.");
            Require(!Directory.EnumerateFiles(root, "outside", SearchOption.AllDirectories).Any(), "Archive traversal created a file outside its selected destinations.");
            Require(await File.ReadAllTextAsync(policyFile, deadline.Token) == policyText, "The portable source settings replaced installed source policy.");
        }
        if (accepted)
        {
            using var offline = new HttpClient(new OfflineHandler());
            await using var reopened = new LauncherUpdateService(installation, data, offline);
            reopened.StartMonitoring();
            while (reopened.State.Status is not ("ready" or "failed")) await Task.Delay(20, deadline.Token);
            Require(reopened.State.Status == "ready", "The shipped reader could not restore the verified transfer offline.");
            restored = true;
        }
        var result = new { kind, scenario, status = finalState, handler.IncrementalRequests, handler.CompanionRequests, handler.ArchiveRequests,
            offlineRestoration = restored, installationUnchanged = true, seconds = timer.Elapsed.TotalSeconds };
        results.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    await File.WriteAllTextAsync(Path.Combine(output, "receipt.json"), JsonSerializer.Serialize(new
    {
        verifiedAtUtc = DateTimeOffset.UtcNow, platform = RuntimeInformation.ProcessArchitecture.ToString(), readers,
        fixtureOnly = true, publicNetwork = false, installed = false, cases = results,
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("PASS shipped-reader fallback fixtures. No launcher, updater helper, game or external service was started.");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine("Shipped-reader fallback acceptance failed: " + error.GetType().Name + ": " + error.Message);
    return 1;
}

static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("Fixture network deliberately disabled.");
}

sealed class FallbackHandler : HttpMessageHandler
{
    internal static readonly Dictionary<string, byte[]> Payloads = new()
    {
        [LauncherRelease.Executable] = "new"u8.ToArray(),
        ["wwwroot/index.html"] = "web"u8.ToArray(),
        [LauncherRelease.UpdaterExecutable] = "new"u8.ToArray(),
    };
    private readonly string _scenario;
    private readonly byte[] _index, _companion;
    private readonly Dictionary<string, byte[]> _volumes = new(StringComparer.Ordinal);
    private int _incrementalRequests, _companionRequests, _archiveRequests;
    internal int IncrementalRequests => Volatile.Read(ref _incrementalRequests);
    internal int CompanionRequests => Volatile.Read(ref _companionRequests);
    internal int ArchiveRequests => Volatile.Read(ref _archiveRequests);

    internal FallbackHandler(string scenario)
    {
        _scenario = scenario;
        var index = new StringBuilder($"""
            schema=1
            product="fantasytale-launcher"
            platform="win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}"
            revision=2
            version="Fallback fixture"

            """);
        foreach (var (path, bytes) in Payloads)
            index.Append(CultureInfo.InvariantCulture, $"[[files]]\npath={JsonSerializer.Serialize(path)}\nsource_path=\"objects/{Digest(bytes)}\"\nsize={bytes.Length}\nsha256=\"{Digest(bytes)}\"\n");
        _index = Encoding.UTF8.GetBytes(index.ToString());
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            void Entry(string path, byte[] bytes) { using var stream = zip.CreateEntry(path).Open(); stream.Write(bytes); }
            foreach (var (path, bytes) in Payloads)
            {
                if (scenario == "missing-file" && path == LauncherRelease.UpdaterExecutable) continue;
                Entry(path, scenario == "wrong-file" && path == LauncherRelease.Executable ? "bad"u8.ToArray() : bytes);
            }
            Entry(LauncherUpdatePolicy.FileName, "portable policy must be ignored"u8.ToArray());
            if (scenario == "unexpected-file") Entry("unexpected.dll", "bad"u8.ToArray());
            if (scenario == "duplicate-file") Entry(LauncherRelease.Executable, "new"u8.ToArray());
            if (scenario == "traversal") Entry("../outside", "bad"u8.ToArray());
        }
        var archive = buffer.ToArray();
        _volumes.Add("portable/launcher.zip", scenario == "split-zip" ? archive[..(archive.Length / 2)] : archive);
        if (scenario == "split-zip") _volumes.Add("portable/launcher.zip.002", archive[(archive.Length / 2)..]);
        var companion = new StringBuilder($"schema=1\nmanifest_sha256=\"{(scenario == "wrong-release" ? new string('0', 64) : Digest(_index))}\"\nformat=\"zip\"\n");
        var volume = 0;
        foreach (var (path, bytes) in _volumes)
            companion.Append(CultureInfo.InvariantCulture, $"[[files]]\npath=\"{Path.GetFileName(path)}\"\nsource_path=\"{path}\"\nsize={bytes.Length}\nsha256=\"{(scenario == "corrupt-transfer" ? new string('0', 64) : Digest(bytes))}\"\nvolume={++volume}\n");
        _companion = Encoding.UTF8.GetBytes(companion.ToString());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        if (request.Method == HttpMethod.Post && path == "api/fs/get")
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            path = json.RootElement.GetProperty("path").GetString()!.TrimStart('/');
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { code = 200, data = new
            {
                raw_url = "https://fallback-fixture.invalid/" + path, size = Bytes(path)?.LongLength ?? 3, is_dir = false,
            } }) };
        }
        if (path == "launcher/current.toml.archive.toml") Interlocked.Increment(ref _companionRequests);
        else if (_volumes.ContainsKey(path)) Interlocked.Increment(ref _archiveRequests);
        else if (path.StartsWith("objects/", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _incrementalRequests);
            if (_scenario != "incremental") return new(HttpStatusCode.ServiceUnavailable);
        }
        return Bytes(path) is { } bytes ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new(HttpStatusCode.NotFound);
    }

    private byte[]? Bytes(string path) => path switch
    {
        "launcher/current.toml" => _index,
        "launcher/current.toml.archive.toml" => _companion,
        _ => _volumes.TryGetValue(path, out var volume) ? volume :
            Payloads.Values.FirstOrDefault(bytes => path == "objects/" + Digest(bytes)),
    };

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
