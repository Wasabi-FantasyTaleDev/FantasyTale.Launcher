using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed partial class LauncherUpdateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-self-update-").FullName;
    private string Installation => Path.Combine(_root, "installed");
    private string Data => Path.Combine(_root, "data");
    private static string Platform => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    private LauncherUpdatePolicy Policy(string kind = "http")
    {
        Directory.CreateDirectory(Installation);
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""
            schema = 1
            kind = "{kind}"
            base_url = "{(kind == "github" ? "https://github.com/team/repo/tree/main/" : "https://updates.example/")}"
            feed_path = "launcher/stable.toml"
            minimum_revision = 1
            """);
        return LauncherUpdatePolicy.Load(Installation)!;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Manifest(long revision = 2, string extra = "", string? mainHash = null, long mainSize = 3) => $"""
        schema = 1
        product = "fantasytale-launcher"
        platform = "{Platform}"
        revision = {revision}
        version = "Next"
        [[files]]
        path = "FantasyTale.Launcher.exe"
        source_path = "objects/app"
        size = {mainSize}
        sha256 = "{mainHash ?? Hash("new"u8.ToArray())}"
        [[files]]
        path = "wwwroot/index.html"
        source_path = "objects/index"
        size = 3
        sha256 = "{Hash("web"u8.ToArray())}"
        [[files]]
        path = "updater/FantasyTale.Launcher.Updater.exe"
        source_path = "objects/helper"
        size = 3
        sha256 = "{Hash("new"u8.ToArray())}"
        {extra}
        """;
    private static byte[] IndexBytes(string text) => Encoding.UTF8.GetBytes(text);
    [Fact]
    public void ExactIndexBytesIdentifyASelection()
    {
        var policy = Policy(); var bytes = IndexBytes(Manifest());
        var release = LauncherRelease.Parse(policy, bytes);
        Assert.Equal(2, release.Revision); Assert.Equal(3, release.Files.Count);
        Assert.Equal(Hash(bytes), release.ManifestSha256);
        Assert.NotEqual(release.ManifestSha256, LauncherRelease.Parse(policy, IndexBytes(Manifest(3))).ManifestSha256);
        var padded = IndexBytes(Manifest() + "\n#" + new string('x', 256 * 1024));
        Assert.Equal(3, LauncherRelease.Parse(policy, padded).Files.Count);
        Assert.Equal(Hash(padded), LauncherRelease.Parse(policy, padded).ManifestSha256);
    }

    [Fact]
    public void ConfiguredDetachedSignatureMustMatchExactFeedBytes()
    {
        Directory.CreateDirectory(Installation);
        using var rsa = RSA.Create(2048);
        var key = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""
            schema = 1
            kind = "http"
            base_url = "https://updates.example/"
            feed_path = "launcher/stable.toml"
            signature_path = "launcher/stable.toml.sig"
            signature_public_key = "{key}"
            """);
        var policy = LauncherUpdatePolicy.Load(Installation)!;
        policy.ValidateRuntime();
        var manifest = IndexBytes(Manifest());
        var signature = rsa.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        policy.VerifySignature(manifest, signature);
        var invalid = signature.ToArray(); invalid[0] ^= 0xff;
        Assert.Throws<CryptographicException>(() => policy.VerifySignature(manifest, invalid));
    }
    [Theory]
    [InlineData("platform", "wrong-platform")]
    [InlineData("product = \"fantasytale-launcher\"", "product = \"other\"")]
    [InlineData("updater/FantasyTale.Launcher.Updater.exe", "assets/helper.exe")]
    [InlineData("wwwroot/index.html", "../index.html")]
    [InlineData("wwwroot/index.html", "launcher-update.toml")]
    [InlineData("wwwroot/index.html", "fantasytale.launcher.EXE")]
    public void MalformedOrWrongTargetReleasesAreRejected(string from, string to)
    {
        var policy = Policy();
        if (from == "platform") { from = $"platform = \"{Platform}\""; to = "platform = \"unsupported\""; }
        Assert.ThrowsAny<Exception>(() => LauncherRelease.Parse(policy, IndexBytes(Manifest().Replace(from, to, StringComparison.Ordinal))));
    }
    [Fact]
    public async Task EveryStartupChecksEvenWhenPeriodicChecksAreDisabled()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(false, false));
            first.StartMonitoring();
            await UntilAsync(() => first.State.Status == "available");
            Assert.Equal(2, first.State.Revision); Assert.Empty(first.List());
        }
        handler.Feed = IndexBytes(Manifest(3));
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        Assert.False(restarted.Preferences.AutoCheck);
        restarted.StartMonitoring();
        await UntilAsync(() => restarted.State.Status == "available");
        Assert.Equal(3, restarted.State.Revision); Assert.Empty(restarted.List());
    }

    [Fact]
    public async Task DiscoveryPinsHighestRevisionAndRejectsReplayAndEquivocation()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest(3))); using var http = new HttpClient(handler);
        await using (var service = new LauncherUpdateService(Installation, Data, http)) Assert.Equal("available", (await service.CheckAsync()).Status);
        handler.Feed = IndexBytes(Manifest(2));
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal("integrity", (await restarted.CheckAsync()).Failure);
        handler.Feed = IndexBytes(Manifest(3).Replace("version = \"Next\"", "version = \"Rewritten\"", StringComparison.Ordinal));
        Assert.Equal("integrity", (await restarted.CheckAsync()).Failure);
        handler.Feed = IndexBytes(Manifest(3)); Assert.Equal("available", (await restarted.CheckAsync()).Status);
    }
    [Theory]
    [InlineData("http")]
    [InlineData("alist")]
    public async Task BothTransportsStageVerifiedFilesWithoutReplacingTheRunningInstallation(string kind)
    {
        Policy(kind); File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "old");
        using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var state = await service.CheckAsync(); Assert.Equal("available", state.Status);
        await service.DownloadAsync(state.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage); Assert.All(service.List()[0].Files, f => Assert.Equal("verified", f.Stage));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.False(File.Exists(Path.Combine(Installation, "wwwroot", "index.html")));
        Assert.Equal(Path.Combine(Data, "launcher-update"), service.ReadyDirectory());
        // The helper fixture shares the executable object and reuses its verified bytes.
        Assert.Equal(kind == "alist" ? 3 : 0, handler.Resolutions);
    }
    [Theory]
    [InlineData("http", false)]
    [InlineData("alist", false)]
    [InlineData("http", true)]
    [InlineData("alist", true)]
    public async Task IncrementalLauncherUpdateStagesOnlyFilesWhoseInstalledContentChanged(string kind, bool reuseInstalledObject)
    {
        var policy = Policy(kind);
        var previousText = Manifest(1, mainHash: Hash("old"u8.ToArray()));
        if (!reuseInstalledObject) previousText = previousText.Replace(Hash("new"u8.ToArray()), Hash("old"u8.ToArray()), StringComparison.Ordinal);
        var previous = LauncherRelease.Parse(policy, IndexBytes(previousText));
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        foreach (var file in previous.Files)
            File.WriteAllBytes(ClientInstallTransaction.Staged(Installation, file.Sha256), file.Sha256 == Hash("old"u8.ToArray()) ? "old"u8.ToArray() : file.Path == "wwwroot/index.html" ? "web"u8.ToArray() : "new"u8.ToArray());
        await ClientInstallTransaction.ApplyAsync(Installation, previous.Files, _ => { }, default, LauncherUpdateService.Receipt(policy, previous));
        var cache = new ClientFileCache(Installation); cache.RecordInstalled(previous.Files);

        using var handler = new FeedHandler(IndexBytes(Manifest(4))); using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync(); Assert.Equal("available", selected.Status);
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");

        Assert.Equal("ready", service.List()[0].Stage);
        Assert.Equal(4, selected.Revision);
        var expectedPaths = reuseInstalledObject
            ? new[] { LauncherRelease.Executable }
            : new[] { LauncherRelease.Executable, "updater/FantasyTale.Launcher.Updater.exe" };
        Assert.Equal(expectedPaths, service.List()[0].Files.Select(file => file.Path));
        var unchangedUnique = previous.Files.Single(file => file.Path == "wwwroot/index.html");
        Assert.False(File.Exists(Path.Combine(service.ReadyDirectory(), "objects", unchangedUnique.Sha256 + ".verified")));
        Assert.Equal(reuseInstalledObject ? 0 : 1, handler.PayloadPaths.Count);
        Assert.DoesNotContain("/objects/index", handler.PayloadPaths);
        Assert.Equal(kind == "alist" ? (reuseInstalledObject ? 1 : 2) : 0, handler.Resolutions);
    }
    [Fact]
    public async Task CorruptDownloadsFailWithoutCreatingInstalledFilesAndCanRetry()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())) { Corrupt = true }; using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage == "failed");
        Assert.Equal("integrity", service.List()[0].Failure); Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
        handler.Corrupt = false; await Task.Delay(20); await service.ActionAsync("retry");
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed"); Assert.Equal("ready", service.List()[0].Stage);
    }
    [Fact]
    public async Task AutomaticDownloadHonorsCanceledSelectionButAcceptsManualRetryAndNewerRelease()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(true, true));
            await first.DownloadAsync((await first.CheckAsync()).ManifestSha256!);
            await UntilAsync(() => first.List()[0].Stage == "ready");
            await first.ActionAsync("cancel");
        }
        await using (var second = new LauncherUpdateService(Installation, Data, http))
        {
            second.StartMonitoring();
            await UntilAsync(() => second.State.Status == "available");
            await Task.Delay(200);
            Assert.Empty(second.List());
            await second.DownloadAsync(second.State.ManifestSha256!);
            await UntilAsync(() => second.List()[0].Stage == "ready");
            await second.ActionAsync("cancel");
        }
        handler.Feed = IndexBytes(Manifest(3));
        await using var third = new LauncherUpdateService(Installation, Data, http);
        third.StartMonitoring();
        await UntilAsync(() => third.List() is { Count: > 0 } tasks && tasks[0].Stage == "ready");
        Assert.Equal(3, third.State.Revision);
    }

    [Fact]
    public async Task CancelingAReadyUpdateRemovesReadinessAndDoesNotResumeAfterRestart()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using (var service = new LauncherUpdateService(Installation, Data, http))
        {
            await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
            await UntilAsync(() => service.List()[0].Stage == "ready");
            await service.ActionAsync("cancel");
            Assert.Equal("canceled", service.List()[0].Stage);
            Assert.Equal("available", service.State.Status);
            Assert.Throws<InvalidOperationException>(() => service.ReadyDirectory());
            var selected = await service.CheckAsync();
            handler.Unavailable = true;
            await service.DownloadAsync(selected.ManifestSha256!);
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
            Assert.Equal("ready", service.List()[0].Stage);
            await service.ActionAsync("cancel");
        }
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        restarted.StartMonitoring();
        await UntilAsync(() => restarted.State.Status == "failed");
        Assert.Equal("network", restarted.State.Failure);
        Assert.Empty(restarted.List());
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    [Fact]
    public async Task DownloadedUpdateSurvivesExitAndReturnsReadyWithoutAnyNetworkRequests()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        string selected;
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            selected = (await first.CheckAsync()).ManifestSha256!;
            await first.DownloadAsync(selected);
            await UntilAsync(() => first.List()[0].Stage == "ready");
        }
        var requests = handler.Requests; handler.Unavailable = true;
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        restarted.StartMonitoring();
        await UntilAsync(() => restarted.List() is { Count: > 0 } tasks && tasks[0].Stage == "ready");
        Assert.Equal("ready", restarted.State.Status); Assert.Equal(selected, restarted.State.ManifestSha256);
        Assert.All(restarted.List()[0].Files, file => Assert.Equal("verified", file.Stage));
        Assert.Equal(requests, handler.Requests);
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    [Fact]
    public async Task PausedTransfersStayPausedAfterRestartAndReadyTransfersAreReverifiedOffline()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(false, false));
            await first.DownloadAsync((await first.CheckAsync()).ManifestSha256!);
            await UntilAsync(() => first.List()[0].Stage == "ready");
            await first.ActionAsync("pause");
            Assert.Equal("downloading", first.State.Status);
            Assert.Throws<InvalidOperationException>(() => first.ReadyDirectory());
        }
        handler.Unavailable = true;
        await using (var second = new LauncherUpdateService(Installation, Data, http))
        {
            second.StartMonitoring(); await UntilAsync(() => second.List().Count != 0);
            Assert.Equal("paused", second.List()[0].Stage);
            Assert.Equal("downloading", second.State.Status);
            Assert.Throws<InvalidOperationException>(() => second.ReadyDirectory());
            await second.ActionAsync("resume"); await UntilAsync(() => second.List()[0].Stage is "ready" or "failed");
            Assert.Equal("ready", second.List()[0].Stage);
        }
        await using var third = new LauncherUpdateService(Installation, Data, http);
        third.StartMonitoring(); await UntilAsync(() => third.List() is { Count: > 0 } tasks && tasks[0].Stage == "ready");
        Assert.Equal("ready", third.State.Status);
    }

    [Fact]
    public async Task PublisherCanCreateDetachedSignatureWithoutWritingPrivateKey()
    {
        var policy = Policy();
        File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "new");
        Directory.CreateDirectory(Path.Combine(Installation, "wwwroot")); File.WriteAllText(Path.Combine(Installation, "wwwroot", "index.html"), "web");
        Directory.CreateDirectory(Path.Combine(Installation, "updater")); File.WriteAllText(Path.Combine(Installation, "updater", "FantasyTale.Launcher.Updater.exe"), "helper");
        using var rsa = RSA.Create(2048);
        var environment = "FT_TEST_SIGNING_" + Guid.NewGuid().ToString("N");
        var prior = Environment.GetEnvironmentVariable(environment);
        try
        {
            Environment.SetEnvironmentVariable(environment, Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
            var config = Path.Combine(_root, "signed-publish.toml");
            File.WriteAllText(config, $"input = \"installed\"\noutput = \"signed-public\"\nfeed_path = \"launcher/{Platform}/current.toml\"\nsource_prefix = \"launcher/{Platform}\"\nplatform = \"{Platform}\"\nrevision = 4\nsignature_path = \"launcher/{Platform}/current.toml.sig\"\nsignature_private_key_env = \"{environment}\"\nsignature_public_key_path = \"launcher/{Platform}/current.toml.public-key.txt\"\n");
            var feed = await LauncherReleasePublisher.PublishAsync(config);
            var published = Path.Combine(_root, "signed-public", "launcher", Platform);
            var publicKey = File.ReadAllText(Path.Combine(published, "current.toml.public-key.txt")).Trim();
            File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"kind = \"http\"\nbase_url = \"https://updates.example/\"\nfeed_path = \"launcher/{Platform}/current.toml\"\nsignature_path = \"launcher/{Platform}/current.toml.sig\"\nsignature_public_key = \"{publicKey}\"\n");
            var signedPolicy = LauncherUpdatePolicy.Load(Installation)!;
            signedPolicy.VerifySignature(File.ReadAllBytes(feed), File.ReadAllBytes(Path.Combine(published, "current.toml.sig")));
            Assert.DoesNotContain("PRIVATE", File.ReadAllText(Path.Combine(published, "current.toml.public-key.txt"), Encoding.UTF8));
            var recordText = File.ReadAllText(Path.Combine(_root, "signed-public", ".fantasytale-signature.json"));
            using var record = JsonDocument.Parse(recordText);
            Assert.Equal(Hash(File.ReadAllBytes(feed)), record.RootElement.GetProperty("sha256").GetString());
            Assert.Equal(Hash(File.ReadAllBytes(Path.Combine(published, "current.toml.sig"))), record.RootElement.GetProperty("signature_sha256").GetString());
            Assert.Equal(Hash(File.ReadAllBytes(Path.Combine(published, "current.toml.public-key.txt"))), record.RootElement.GetProperty("public_key_sha256").GetString());
            Assert.DoesNotContain(Environment.GetEnvironmentVariable(environment)!, recordText, StringComparison.Ordinal);
            File.WriteAllText(config, $"input = 'installed'\noutput = 'signed-public'\nplatform = '{Platform}'\nrevision = 5\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(config));
        }
        finally { Environment.SetEnvironmentVariable(environment, prior); }
    }

    [Fact]
    public async Task PublisherDiscoversFilesAndRefusesSecretsAndRevisionReuse()
    {
        var policy = Policy();
        File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "new");
        Directory.CreateDirectory(Path.Combine(Installation, "wwwroot")); File.WriteAllText(Path.Combine(Installation, "wwwroot", "index.html"), "web");
        Directory.CreateDirectory(Path.Combine(Installation, "updater")); File.WriteAllText(Path.Combine(Installation, "updater", "FantasyTale.Launcher.Updater.exe"), "helper");
        var config = Path.Combine(_root, "publish.toml");
        var text = $"input = \"installed\"\noutput = \"published\"\nfeed_path = \"launcher/stable.toml\"\nsource_prefix = \"launcher\"\nplatform = \"{Platform}\"\nrevision = 2\n";
        File.WriteAllText(config, text);
        var path = await LauncherReleasePublisher.PublishAsync(config);
        var release = LauncherRelease.Parse(policy, File.ReadAllBytes(path));
        Assert.Equal(3, release.Files.Count); Assert.DoesNotContain(release.Files, f => f.Path == LauncherUpdatePolicy.FileName);
        foreach (var file in release.Files) Assert.Equal(file.Sha256, Hash(File.ReadAllBytes(Path.Combine(_root, "published", file.SourcePath))));
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(config));
        File.WriteAllText(config, text.Replace("revision = 2", "revision = 3", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(Installation, "local.key"), "private");
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(config));
        Assert.Equal(2, LauncherRelease.Parse(policy, File.ReadAllBytes(path)).Revision);
    }

    [Theory]
    [InlineData("remembered-logins.bin")]
    [InlineData("nested/remembered-logins.bin.tmp-fixture")]
    [InlineData("account-sessions.bin")]
    public async Task ProtectedCredentialsCannotEnterPublishedOrDownloadedLauncherReleases(string path)
    {
        var policy = Policy();
        var destination = Path.Combine(Installation, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "protected-fixture");
        var config = Path.Combine(_root, "publish.toml");
        await File.WriteAllTextAsync(config, $"input = 'installed'\noutput = 'published'\nplatform = '{Platform}'\nrevision = 2\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(config));
        var row = $"\n[[files]]\npath = '{path}'\nsource_path = 'credential'\nsize = 1\nsha256 = '{new string('a', 64)}'\n";
        Assert.Throws<InvalidDataException>(() => LauncherRelease.Parse(policy, IndexBytes(Manifest(extra: row))));
    }
    [Fact]
    public async Task DeferredInstallRollsBackUntilExplicitHealthyCommit()
    {
        var policy = Policy(); var release = LauncherRelease.Parse(policy, IndexBytes(Manifest()));
        File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "old");
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        foreach (var file in release.Files) File.WriteAllText(ClientInstallTransaction.Staged(Installation, file.Sha256), file.Path.EndsWith(".exe", StringComparison.Ordinal) ? "new" : "web");
        await ClientInstallTransaction.ApplyAsync(Installation, release.Files, _ => { }, default, LauncherUpdateService.Receipt(policy, release), deferCommit: true);
        Assert.Null(ClientInstallTransaction.ReadReceipt(Installation));
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.False(File.Exists(Path.Combine(Installation, "wwwroot", "index.html")));
        await ClientInstallTransaction.ApplyAsync(Installation, release.Files, _ => { }, default, LauncherUpdateService.Receipt(policy, release), deferCommit: true);
        await ClientInstallTransaction.CommitAsync(Installation);
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal(2, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
    }
    [Fact]
    public async Task HealthCommitRefusesExternalEditsAndRetainsBackups()
    {
        var policy = Policy(); var release = LauncherRelease.Parse(policy, IndexBytes(Manifest()));
        File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "old");
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        foreach (var file in release.Files) File.WriteAllText(ClientInstallTransaction.Staged(Installation, file.Sha256), file.Path.EndsWith(".exe", StringComparison.Ordinal) ? "new" : "web");
        await ClientInstallTransaction.ApplyAsync(Installation, release.Files, _ => { }, default, deferCommit: true);
        File.WriteAllText(Path.Combine(Installation, LauncherRelease.Executable), "user edit");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.CommitAsync(Installation));
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.RecoverAsync(Installation));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ClientInstallTransaction.Workspace(Installation), "original-0")));
    }
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, false, false, false, true)]
    public async Task RealExternalHelperCommitsHealthyProcessOrRestartsAfterFailure(bool crash, bool locked, bool invalidHealth, bool requiresSignature, bool stagingLocked)
    {
        if (!OperatingSystem.IsWindows()) return;
        var policy = Policy();
        using var key = requiresSignature ? RSA.Create(2048) : null;
        if (key is not null) { RequireSignature(key); policy = LauncherUpdatePolicy.Load(Installation)!; }
        var client = Path.Combine(AppContext.BaseDirectory, "test-client");
        foreach (var file in Directory.GetFiles(client)) File.Copy(file, Path.Combine(Installation, Path.GetFileName(file)), true);
        File.Copy(Path.Combine(client, "FantasyTale.Launcher.TestClient.exe"), Path.Combine(Installation, LauncherRelease.Executable), true);
        var helperFiles = Path.Combine(AppContext.BaseDirectory, "test-updater");
        Directory.CreateDirectory(Path.Combine(Installation, "updater"));
        foreach (var file in Directory.GetFiles(helperFiles)) File.Copy(file, Path.Combine(Installation, "updater", Path.GetFileName(file)), true);
        File.WriteAllText(Path.Combine(Installation, "update-probe"), invalidHealth ? "invalid-health" : crash ? "exit" : "healthy");
        crash |= invalidHealth;
        var exe = File.ReadAllBytes(Path.Combine(Installation, LauncherRelease.Executable));
        var payloads = Enumerable.Range(0, 16).ToDictionary(index => $"wwwroot/assets/chunk-{index}.js",
            index => Encoding.UTF8.GetBytes($"const chunk = {index};"));
        var extra = string.Join("\n", payloads.Select(pair => $"""
            [[files]]
            path = "{pair.Key}"
            source_path = "objects/{Hash(pair.Value)}"
            size = {pair.Value.Length}
            sha256 = "{Hash(pair.Value)}"
            """));
        var envelope = IndexBytes(Manifest(extra: extra, mainHash: Hash(exe), mainSize: exe.Length));
        var release = LauncherRelease.Parse(policy, envelope);
        var retired = new ClientReleaseFile("retired.dll", "objects/retired", 3, Hash("old"u8.ToArray()));
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, retired.Sha256), "old");
        await ClientInstallTransaction.ApplyAsync(Installation, [release.Files[0], retired], _ => { }, default,
            LauncherUpdateService.Receipt(policy, release) with { Revision = 1 }, pruneOwned: true);
        var updateRoot = Path.Combine(Data, "launcher-update"); Directory.CreateDirectory(Path.Combine(updateRoot, "objects"));
        File.WriteAllBytes(Path.Combine(updateRoot, "release.toml"), envelope);
        if (key is not null) File.WriteAllBytes(Path.Combine(updateRoot, "release.toml.sig"), key.SignData(envelope, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        foreach (var file in release.Files) File.WriteAllBytes(Path.Combine(updateRoot, "objects", file.Sha256 + ".verified"),
            payloads.TryGetValue(file.Path, out var payload) ? payload : file.Path == LauncherRelease.Executable ? exe : file.Path == "wwwroot/index.html" ? "web"u8.ToArray() : "new"u8.ToArray());
        var start = new ProcessStartInfo(Path.Combine(Installation, LauncherRelease.Executable)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Installation };
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        using var blockedLease = locked ? new FileStream(Path.Combine(ClientInstallTransaction.Workspace(Installation), "self-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None) : null;
        using var blockedStage = stagingLocked ? new FileStream(ClientInstallTransaction.Staged(Installation, Hash("web"u8.ToArray())), FileMode.Create, FileAccess.ReadWrite, FileShare.None) : null;
        start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(Data); start.ArgumentList.Add("--prepare-update");
        using var parent = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await parent.WaitForExitAsync(timeout.Token);
        Assert.True(parent.ExitCode == 0, File.Exists(Path.Combine(Data, "probe-error")) ? File.ReadAllText(Path.Combine(Data, "probe-error")) : "Parent failed.");
        var outcome = Path.Combine(updateRoot, "outcome.json");
        await UntilAsync(() => UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(outcome) is { Status: not "installing" }, 45000,
            () => $"parentExited={parent.HasExited}; parentExitCode={parent.ExitCode}; outcomeStatus={UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(outcome)?.Status ?? "missing"}; root={_root}");
        var savedOutcome = UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(outcome)!;
        Assert.Equal(locked || stagingLocked ? "failed" : crash ? "rolled-back" : "completed", savedOutcome.Status);
        Assert.Equal(locked || stagingLocked ? "disk" : crash ? "startup" : null, savedOutcome.Failure);
        if (stagingLocked) { Assert.Equal("staging", savedOutcome.Phase); Assert.NotNull(savedOutcome.ErrorCode); }
        if (invalidHealth)
        {
            var invalidPid = int.Parse(File.ReadAllText(Path.Combine(Data, "invalid-health-pid")), System.Globalization.CultureInfo.InvariantCulture);
            Process? invalidChild = null;
            try { invalidChild = Process.GetProcessById(invalidPid); }
            catch (ArgumentException) { }
            using (invalidChild) Assert.True(invalidChild is null || invalidChild.HasExited, "The rejected child must exit before rollback is reported.");
        }
        foreach (var pair in payloads)
        {
            var path = Path.Combine(Installation, pair.Key);
            if (crash || locked || stagingLocked) Assert.False(File.Exists(path));
            else Assert.Equal(pair.Value, File.ReadAllBytes(path));
        }
        var rejection = UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(Path.Combine(updateRoot, "rejected.json"));
        if (crash && !locked)
        {
            Assert.NotNull(rejection);
            Assert.Equal(policy.Identity, rejection.SourceIdentity);
            Assert.Equal(release.ManifestSha256, rejection.ManifestSha256);
        }
        else Assert.Null(rejection);
        await UntilAsync(() => File.Exists(Path.Combine(Data, crash || locked || stagingLocked ? "restored" : "healthy")));
        Assert.Equal(!crash && !locked && !stagingLocked, File.Exists(Path.Combine(Installation, "wwwroot", "index.html")));
        Assert.Equal(!crash && !locked && !stagingLocked ? 2 : 1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
        Assert.Equal(crash || locked || stagingLocked, File.Exists(Path.Combine(Installation, "retired.dll")));
        // The probe intentionally stays alive briefly after acknowledgement to exercise helper commit ordering.
        await Task.Delay(800);
    }
    private static async Task UntilAsync(Func<bool> ready, int timeout = 10000, Func<string>? diagnostics = null)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.ElapsedMilliseconds > timeout) throw new TimeoutException($"Update fixture did not finish. {diagnostics?.Invoke()}"); await Task.Delay(20); }
    }
    private sealed class FeedHandler(byte[] feed) : HttpMessageHandler
    {
        internal byte[] Feed = feed;
        internal bool Corrupt;
        internal bool Unavailable;
        internal int Resolutions;
        internal int Requests;
        internal readonly System.Collections.Concurrent.ConcurrentQueue<string> PayloadPaths = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Unavailable) throw new HttpRequestException("Offline fixture.");
            Assert.Null(request.Headers.Authorization);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/fs/get")
            {
                Resolutions++; using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var relative = input.RootElement.GetProperty("path").GetString();
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { code = 200, data = new { raw_url = "https://storage.example" + relative, is_dir = false, size = relative == "/launcher/stable.toml" ? Feed.Length : 3 } })) };
            }
            if (path != "/launcher/stable.toml") PayloadPaths.Enqueue(path);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(path == "/launcher/stable.toml" ? Feed : path is "/objects/app" or "/objects/helper" ? Encoding.UTF8.GetBytes(Corrupt ? "bad" : "new") : "web"u8.ToArray()) };
        }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
