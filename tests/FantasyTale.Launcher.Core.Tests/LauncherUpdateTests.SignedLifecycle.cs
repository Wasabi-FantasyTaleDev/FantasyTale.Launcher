using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData("http", "healthy")]
    [InlineData("alist", "healthy")]
    [InlineData("http", "invalid-health")]
    [InlineData("alist", "invalid-health")]
    [InlineData("http", "changed-signature")]
    [InlineData("alist", "changed-signature")]
    public async Task SignedWireDownloadSurvivesOfflineHandoffAndCommitsOrRestores(string kind, string scenario)
        => await RunWireLifecycleAsync(kind, scenario, switchSource: false);

    [Theory]
    [InlineData("http", "healthy", false)]
    [InlineData("alist", "healthy", false)]
    [InlineData("http", "invalid-health", false)]
    [InlineData("alist", "invalid-health", false)]
    [InlineData("http", "healthy", true)]
    [InlineData("alist", "healthy", true)]
    [InlineData("http", "invalid-health", true)]
    [InlineData("alist", "invalid-health", true)]
    public async Task ConfiguredSourceTransitionCommitsOrRestoresPreviousOwnership(string kind, string scenario, bool withSignature)
        => await RunWireLifecycleAsync(kind, scenario, switchSource: true, withSignature);

    [Theory]
    [InlineData("http", "healthy")]
    [InlineData("alist", "healthy")]
    [InlineData("http", "invalid-health")]
    [InlineData("alist", "invalid-health")]
    public async Task UnavailablePrimarySourceFallsBackThroughExternalReplacement(string kind, string scenario)
        => await RunWireLifecycleAsync(kind, scenario, switchSource: true, keepPreviousSource: true);

    private async Task RunWireLifecycleAsync(string kind, string scenario, bool switchSource, bool withSignature = true, bool keepPreviousSource = false)
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Installation);
        var client = Path.Combine(AppContext.BaseDirectory, "test-client");
        foreach (var file in Directory.GetFiles(client)) File.Copy(file, Path.Combine(Installation, Path.GetFileName(file)), true);
        File.Copy(Path.Combine(client, "FantasyTale.Launcher.TestClient.exe"), Path.Combine(Installation, LauncherRelease.Executable));
        var updater = Directory.CreateDirectory(Path.Combine(Installation, "updater")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "test-updater")))
            File.Copy(file, Path.Combine(updater, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(Installation, "update-probe"), scenario == "invalid-health" ? "invalid-health" : "healthy");
        File.WriteAllText(Path.Combine(Installation, "personal.txt"), "user data");

        var content = new Dictionary<string, byte[]>
        {
            [LauncherRelease.Executable] = File.ReadAllBytes(Path.Combine(Installation, LauncherRelease.Executable)),
            ["updater/FantasyTale.Launcher.Updater.exe"] = File.ReadAllBytes(Path.Combine(updater, "FantasyTale.Launcher.Updater.exe")),
            ["wwwroot/index.html"] = IndexBytes("<html>new release</html>"),
            ["wwwroot/assets/new.js"] = IndexBytes("export const release = 2;"),
        };
        var documents = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var manifest = new System.Text.StringBuilder($"schema = 1\nproduct = \"fantasytale-launcher\"\nplatform = \"{Platform}\"\nrevision = 2\nversion = \"Next\"\n");
        foreach (var (path, bytes) in content)
        {
            var sourcePath = "objects/" + Hash(bytes);
            documents[sourcePath] = bytes;
            manifest.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[[files]]\npath = \"{path}\"\nsource_path = \"{sourcePath}\"\nsize = {bytes.Length}\nsha256 = \"{Hash(bytes)}\"\n");
        }
        using var key = RSA.Create(2048);
        var index = IndexBytes(manifest.ToString());
        documents["launcher/stable.toml"] = index;
        documents["launcher/stable.toml.sig"] = key.SignData(index, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var server = new StoreWireTests.WireServer(documents);
        var sourceDisposed = false;
        var unavailableRequests = new ConcurrentQueue<string>();
        server.Unavailable = path =>
        {
            if (path != "launcher/previous.toml") return false;
            unavailableRequests.Enqueue(path);
            return true;
        };
        try
        {
            var previousPolicy = switchSource ? Policy() : null;
            if (keepPreviousSource)
            {
                File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""
                    kind = "{kind}"
                    base_url = "{server.Address}"
                    feed_path = "launcher/previous.toml"
                    """);
                previousPolicy = LauncherUpdatePolicy.Load(Installation)!;
            }
            var signatureSettings = withSignature ? $"signature_path = \"launcher/stable.toml.sig\"\nsignature_public_key = \"{Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}\"" : "";
            var currentSettings = $"""
                kind = "{kind}"
                base_url = "{server.Address}"
                feed_path = "launcher/stable.toml"
                minimum_revision = 1
                {signatureSettings}
                """;
            var sourceConfiguration = keepPreviousSource ? $"""
                schema = 2
                [[sources]]
                kind = "{kind}"
                base_url = "{server.Address}"
                feed_path = "launcher/previous.toml"
                [[sources]]
                {currentSettings}
                """ : "schema = 1\n" + currentSettings;
            File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), sourceConfiguration);
            var policy = LauncherUpdatePolicy.LoadAll(Installation)[^1];
            var release = LauncherRelease.Parse(policy, index);
            var old = IndexBytes("old");
            var oldHash = Hash(old);
            Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
            File.WriteAllBytes(ClientInstallTransaction.Staged(Installation, oldHash), old);
            var previousReceipt = LauncherUpdateService.Receipt(previousPolicy ?? policy, release) with { Revision = 1 };
            await ClientInstallTransaction.ApplyAsync(Installation,
                [new("wwwroot/index.html", "old", old.Length, oldHash), new("retired.dll", "old", old.Length, oldHash)],
                _ => { }, default, previousReceipt, pruneOwned: true);
            var sourceSettings = File.ReadAllBytes(Path.Combine(Installation, LauncherUpdatePolicy.FileName));
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            await using (var service = new LauncherUpdateService(Installation, Data, http))
            {
                service.SavePreferences(new(false, false));
                var available = await service.CheckAsync();
                Assert.Equal("available", available.Status);
                Assert.Equal(Hash(index), available.ManifestSha256);
                if (keepPreviousSource) Assert.NotEmpty(unavailableRequests);
                Assert.DoesNotContain(server.GetPaths, path => path.StartsWith("objects/", StringComparison.Ordinal));
                await service.DownloadAsync(available.ManifestSha256!);
                await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
                var task = Assert.Single(service.List());
                Assert.Equal("ready", task.Stage);
                Assert.Equal(2, task.Files.Count);
                Assert.All(task.Files, file => { Assert.Equal(file.Size, file.Received); Assert.Equal("verified", file.Stage); });
                Assert.Equal(content["wwwroot/index.html"].LongLength + content["wwwroot/assets/new.js"].LongLength, task.Files.Sum(file => file.Received));
                Assert.Equal(kind == "alist", server.Resolutions > 0);
            }
            Assert.Equal(2, server.GetPaths.Count(path => path.StartsWith("objects/", StringComparison.Ordinal)));
            await server.DisposeAsync(); sourceDisposed = true;

            // The source is gone before offline restoration and both external processes start.
            using var offlineHandler = new FeedHandler([]) { Unavailable = true };
            using var offline = new HttpClient(offlineHandler);
            await using (var restored = new LauncherUpdateService(Installation, Data, offline))
            {
                restored.StartMonitoring();
                await UntilAsync(() => restored.List().Count > 0 && restored.List()[0].Stage is "ready" or "failed");
                Assert.Equal("ready", restored.List()[0].Stage);
                Assert.Equal(Hash(index), restored.State.ManifestSha256);
                Assert.Equal(Path.Combine(Data, "launcher-update"), restored.ReadyDirectory());
            }
            var updateRoot = Path.Combine(Data, "launcher-update");
            if (scenario == "changed-signature")
            {
                var signature = File.ReadAllBytes(Path.Combine(updateRoot, "release.toml.sig"));
                signature[0] ^= 0xff;
                File.WriteAllBytes(Path.Combine(updateRoot, "release.toml.sig"), signature);
            }
            var start = new ProcessStartInfo(Path.Combine(Installation, LauncherRelease.Executable))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Installation };
            foreach (var argument in new[] { "--data-root", Data, "--prepare-update" }) start.ArgumentList.Add(argument);
            using var parent = Process.Start(start)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await parent.WaitForExitAsync(deadline.Token); }
            catch
            {
                // A timed-out disposable launcher must not leave its updater holding fixture files.
                if (!parent.HasExited)
                {
                    parent.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await parent.WaitForExitAsync(cleanup.Token);
                }
                throw;
            }
            var healthy = scenario == "healthy";
            if (scenario == "changed-signature")
            {
                Assert.Equal(1, parent.ExitCode);
                Assert.True(File.Exists(Path.Combine(Data, "probe-error")));
                Assert.False(File.Exists(Path.Combine(updateRoot, "outcome.json")));
            }
            else
            {
                Assert.Equal(0, parent.ExitCode);
                await UntilAsync(() => UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(Path.Combine(updateRoot, "outcome.json")) is { Status: not "installing" }, 45000);
                var outcome = UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(Path.Combine(updateRoot, "outcome.json"))!;
                Assert.Equal(healthy ? "completed" : "rolled-back", outcome.Status);
                Assert.Equal(healthy ? null : "startup", outcome.Failure);
                Assert.Equal(policy.Identity, outcome.SourceIdentity);
                Assert.Equal(Hash(index), outcome.ManifestSha256);
                await UntilAsync(() => File.Exists(Path.Combine(Data, healthy ? "healthy" : "restored")));
            }
            Assert.Equal(healthy ? content["wwwroot/index.html"] : old, File.ReadAllBytes(Path.Combine(Installation, "wwwroot/index.html")));
            Assert.Equal(healthy, File.Exists(Path.Combine(Installation, "wwwroot/assets/new.js")));
            Assert.Equal(!healthy, File.Exists(Path.Combine(Installation, "retired.dll")));
            Assert.Equal(healthy ? 2 : 1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
            Assert.Equal(healthy ? LauncherUpdateService.Receipt(policy, release) : previousReceipt,
                ClientInstallTransaction.ReadReceipt(Installation));
            Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
            Assert.Equal(sourceSettings, File.ReadAllBytes(Path.Combine(Installation, LauncherUpdatePolicy.FileName)));
            Assert.False(LauncherUpdateHandoff.RequiresRecovery(Installation));
            Assert.Empty(Directory.GetFiles(ClientInstallTransaction.Workspace(Installation), "original-*"));
            if (scenario == "invalid-health")
                Assert.Equal(Hash(index), UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(Path.Combine(updateRoot, "rejected.json"))!.ManifestSha256);
            await Task.Delay(800);
        }
        finally { if (!sourceDisposed) await server.DisposeAsync(); }
    }
}
