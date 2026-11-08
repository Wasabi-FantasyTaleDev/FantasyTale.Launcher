using System.Net;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientUpdateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-updates-").FullName;
    private static readonly StoreSource Source = new("original", "Original", "http", "https://fixture.example/", "catalog.toml", true, false);
    private static readonly InstanceDefinition Client = new("fixture", "Fixture", "fixture", "test-1", null, []);
    private static readonly StorePackage Installed = new("fixture.client", "client", "fixture", ["test-1"], "0.1.5.005",
        new Dictionary<string, string> { ["en"] = "Fixture" }, new Dictionary<string, string>(), "Fixture", 3, "manifest.toml", new string('a', 64), 10);
    private static ClientInstallReceipt Receipt => new(Source.Id, Installed.Id, Installed.Version, Installed.ManifestSha256, StoreSourceIdentity.Of(Source), Installed.Revision);
    private static StoreCatalog Catalog(params StorePackage[] packages) => new("fixture", new Dictionary<string, string> { ["en"] = "Fixture" }, packages);

    [Fact]
    public void SelectionUsesMonotonicRevisionNotLabelsOrCatalogOrder()
    {
        var next = Installed with { Version = "0.2.35.001", Revision = 11, ManifestSha256 = new string('b', 64) };
        var old = Installed with { Version = "9999", Revision = 9, ManifestSha256 = new string('c', 64) };
        var ignored = Installed with { Id = "another.client", Version = "future", Revision = 999 };
        var result = ClientUpdateService.Select(Receipt, Client, Catalog(old, Installed, next, ignored));
        Assert.Equal("available", result.Status); Assert.Equal(next, result.Package);
        Assert.Equal("current", ClientUpdateService.Select(Receipt, Client, Catalog(old)).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Optional label")]
    public void FileSyncUsesContentIdentityWithoutGameVersionChanges(string label)
    {
        var current = Installed with { Version = label, Revision = 0, UpdateMode = "files" };
        Assert.Equal("current", ClientUpdateService.Select(Receipt, Client, Catalog(current)).Status);
        var changed = current with { ManifestSha256 = new string('d', 64) };
        Assert.Equal("available", ClientUpdateService.Select(Receipt, Client, Catalog(changed)).Status);
        Assert.Equal("0.1.5.005", Installed.Version);
    }

    [Fact]
    public void RetiredInstalledReleaseCanAdvanceButUnnumberedReleasesNeedManualSelection()
    {
        var next = Installed with { Version = "new", Revision = 20, ManifestSha256 = new string('d', 64) };
        Assert.Equal("available", ClientUpdateService.Select(Receipt, Client, Catalog(next)).Status);
        Assert.Equal("manual", ClientUpdateService.Select(Receipt with { Revision = 0 }, Client, Catalog(next)).Status);
        Assert.Equal("available", ClientUpdateService.Select(Receipt with { Revision = 0 }, Client, Catalog(Installed, next)).Status);
        Assert.Equal("manual", ClientUpdateService.Select(Receipt, Client, Catalog()).Status);
    }

    [Fact]
    public void MutatedReleaseRevisionsHashesOrBuildsNeverBecomeUpdates()
    {
        foreach (var mutated in new[] { Installed with { ManifestSha256 = new string('b', 64) }, Installed with { Revision = 11 },
            Installed with { Builds = ["test-2"] }, Installed with { Game = "other" }, Installed with { Kind = "launcher" } })
            Assert.Equal("integrity", ClientUpdateService.Select(Receipt, Client, Catalog(mutated)).Status);
    }

    [Fact]
    public void CatalogRejectsDuplicateAndOutOfRangeRevisionNumbers()
    {
        var first = PackageText(Installed); var second = PackageText(Installed with { Version = "new" });
        var header = "schema = 1\n[catalog]\nid = \"fixture\"\nname = { en = \"Fixture\" }\n";
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", header + first + second));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", header + first.Replace("revision = 10", "revision = -1", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DiscoveryReusesOneCatalogAndAutomaticInstallationSkipsRunningClients(bool autoDownload, bool running)
    {
        var store = new InstanceStore(Path.Combine(_root, "instances")); await store.CreateAsync(Client); await store.CreateAsync(Client with { Id = "second" });
        foreach (var client in store.List()) await ClientInstallTransaction.ApplyAsync(store.ClientFolder(client.Id), [], _ => { }, default, Receipt);
        var sources = new StoreSourceStore(Path.Combine(_root, "sources")); sources.Save(Source);
        var settings = new ClientUpdateSettings(_root); settings.Save(new(true, autoDownload));
        var next = Installed with { Version = "0.2.35.001", Revision = 11, ManifestSha256 = new string('b', 64) };
        using var handler = new CatalogHandler(Installed, next); using var http = new HttpClient(handler);
        IDownloadSource Open(StoreSource s) => new HttpSource(new Uri(s.BaseUrl!));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "downloads"), _ => Source, Open);
        var installs = new List<string>();
        Task<ClientDownloadTask> Install(StoreSource source, StorePackage package, string id, CancellationToken ct)
        {
            Assert.Equal(Source.Id, source.Id); Assert.Equal(next.Version, package.Version); installs.Add(id);
            return Task.FromResult(new ClientDownloadTask("fixture", "live", new("client", "fixture", "test-1", id, id, Installed.Version, package.Version, source.Name, source.Kind, store.ClientFolder(id)), "queued", [], null, 0, 0, null, null, "downloading"));
        }
        await using var service = new ClientUpdateService(store, sources, http, downloads, settings, Open, Install, id => running && id == "fixture");
        var result = await service.CheckAsync();
        Assert.False(result.Checking); Assert.Null(result.Error); Assert.Equal(1, handler.Requests);
        Assert.Equal(autoDownload ? running ? 1 : 2 : 0, installs.Count);
        Assert.Equal(autoDownload && !running ? "busy" : "available", result.Clients.Single(c => c.InstanceId == "fixture").Status);
        if (!autoDownload)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync("fixture", new string('f', 64), default));
            Assert.Empty(installs);
            await service.InstallAsync("fixture", next.ManifestSha256, default); Assert.Single(installs);
            sources.Save(Source with { BaseUrl = "https://replacement.example/" });
            Assert.All((await service.CheckAsync()).Clients, c => Assert.Equal("source", c.Status));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync("second", next.ManifestSha256, default));
            Assert.Single(installs);
        }
    }

    [Fact]
    public async Task AutomaticChecksRespectEachClientsPreferenceWhileManualChecksRemainAvailable()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(Client with { CheckIntegrity = false });
        await store.CreateAsync(Client with { Id = "second" });
        foreach (var client in store.List())
            await ClientInstallTransaction.ApplyAsync(store.ClientFolder(client.Id), [], _ => { }, default, Receipt);
        var sources = new StoreSourceStore(Path.Combine(_root, "sources")); sources.Save(Source);
        var settings = new ClientUpdateSettings(_root); settings.Save(new(true, false));
        var next = Installed with { Version = "next", Revision = 11, ManifestSha256 = new string('b', 64) };
        using var handler = new CatalogHandler(Installed, next); using var http = new HttpClient(handler);
        IDownloadSource Open(StoreSource source) => new HttpSource(new Uri(source.BaseUrl!));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "downloads"), _ => Source, Open);
        await using var service = new ClientUpdateService(store, sources, http, downloads, settings, Open,
            (_, _, _, _) => throw new InvalidOperationException("Automatic download is disabled."), _ => false);
        service.StartMonitoring();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (service.List() is { } pending && (pending.Checking || pending.Clients.Count == 0))
            await Task.Delay(20, deadline.Token);
        Assert.Equal("second", Assert.Single(service.List().Clients).InstanceId);
        Assert.False(new InstanceStore(Path.Combine(_root, "instances")).Load(Client.Id).CheckIntegrity);
        Assert.Equal(2, (await service.CheckAsync()).Clients.Count);
        var saved = store.Load(Client.Id);
        store.Save(saved with { CheckIntegrity = true });
        Assert.True(new InstanceStore(Path.Combine(_root, "instances")).Load(Client.Id).CheckIntegrity);
    }

    [Fact]
    public void UpdatePreferencesSurviveRestart()
    {
        Assert.Equal(new ClientUpdatePreferences(), new ClientUpdateSettings(_root).Value);
        new ClientUpdateSettings(_root).Save(new(false, true));
        Assert.Equal(new ClientUpdatePreferences(false, true), new ClientUpdateSettings(_root).Value);
    }

    private static string PackageText(StorePackage p) => $"\n[[packages]]\nid = \"{p.Id}\"\nkind = \"client\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"{p.Version}\"\nrevision = {p.Revision}\nname = {{ en = \"Fixture\" }}\npublisher = \"Fixture\"\nsize = 3\nmanifest = \"manifest.toml\"\nmanifest_sha256 = \"{p.ManifestSha256}\"\n";
    private sealed class CatalogHandler(params StorePackage[] packages) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var text = "schema = 1\n[catalog]\nid = \"fixture\"\nname = { en = \"Fixture\" }\n" + string.Concat(packages.Select(PackageText));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(text)) });
        }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
