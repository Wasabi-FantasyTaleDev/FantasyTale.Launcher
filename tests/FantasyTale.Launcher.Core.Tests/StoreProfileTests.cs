using System.Net;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class StoreProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-store-profiles-").FullName;
    private static readonly StoreSource Source = new("fixture", "Fixture", "http", "https://fixture.invalid/", "catalog.toml", true, false);
    private const string Definition = """
        schema = 1
        [build]
        id = "kr-1"
        game = "fixture"
        branch = "kr"

        protocol_version = 1
        executable = "Game.exe"
        region = "KR"
        name = { en = "Fixture" }
        [launch]
        login = "hook"
        launcher_login = "required"
        arguments = []
        [[client_policy_rules]]
        plugin = "fixture.auth"
        id = "auth"
        type = "require"
        [[client_policy_rules]]
        plugin = "fixture.optional"
        id = "offer"
        type = "recommend"
        sources = []
        """;

    private static string Catalog(long revision = 1, string? definition = null) => "schema = 2\n[catalog]\nid = \"fixture\"\nname = { en = \"Fixture\" }\n"
        + "[[profiles]]\ngame = \"fixture\"\nbuild = \"kr-1\"\nrevision = " + revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "\ndefinition = " + SessionWriter.Quote(definition ?? Definition) + "\n";

    [Fact]
    public void InlineProfileCarriesLoginWireAndMandatoryPolicyBeforeAnyPackageIsInstalled()
    {
        var catalog = StoreCatalogFile.Parse("catalog", Catalog());
        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal("KR", profile.Definition.Region);
        Assert.Equal(1, profile.Definition.ProtocolVersion);
        Assert.Equal("required", profile.Definition.LauncherLogin);
        Assert.Equal("fixture.auth", Assert.Single(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Rules(profile.Definition), rule => rule.Required).Id);
        var instance = new InstanceDefinition("fixture", "Fixture", "fixture", "kr-1", null, []);
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance, profile.Definition, [], false));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog().Replace("schema = 2", "schema = 1", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog() + Catalog()[Catalog().IndexOf("[[profiles]]", StringComparison.Ordinal)..]));
    }

    [Fact]
    public async Task ConcurrentViewsShareOneRequestAndCancelingOneWaiterDoesNotCancelSynchronization()
    {
        using var handler = new CatalogHandler(Catalog()) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!)));
        using var cancel = new CancellationTokenSource();
        var first = cache.GetAsync(Source, cancellationToken: cancel.Token);
        var second = cache.GetAsync(Source);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Gate.SetResult();
        var result = await second;
        Assert.Same(result, await cache.GetAsync(Source));
        Assert.Equal(1, handler.Requests);
        Assert.Equal("current", cache.State(Source).Status);
    }

    [Fact]
    public async Task ForcedRefreshObservesNewMetadataWhileOrdinaryViewsReuseTheSnapshot()
    {
        using var handler = new CatalogHandler(Catalog(1)); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!)));
        var initial = await cache.GetAsync(Source);
        handler.Text = Catalog(2);
        Assert.Same(initial, await cache.GetAsync(Source));
        Assert.Equal(1, handler.Requests);
        var refreshed = await cache.GetAsync(Source, force: true);
        Assert.Equal(2, Assert.Single(refreshed.Profiles).Revision);
        Assert.Equal(2, handler.Requests);
        Assert.Same(refreshed, await cache.GetAsync(Source));
    }

    [Fact]
    public async Task InvalidRefreshAndProfileRollbackPreserveTheValidatedPersistentSnapshot()
    {
        using var handler = new CatalogHandler(Catalog(3)); using var http = new HttpClient(handler);
        await using (var cache = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!))))
        {
            var valid = await cache.GetAsync(Source);
            handler.Text = Catalog(2);
            await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(Source, true));
            Assert.Same(valid, cache.Cached(Source));
            handler.Text = Catalog(3, Definition.Replace("Game.exe", "Other.exe", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(Source, true));
            handler.Text = "not a catalog";
            await Assert.ThrowsAsync<DefinitionException>(() => cache.GetAsync(Source, true));
            Assert.Equal("failed", cache.State(Source).Status);
            Assert.Same(valid, cache.Cached(Source));
        }
        await using var restored = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!)));
        Assert.Equal(3, Assert.Single(restored.Cached(Source)!.Profiles).Revision);
        Assert.Null(restored.Cached(Source with { BaseUrl = "https://other.invalid/" }));
    }

    [Fact]
    public async Task AllSourcesFinishEvenWhenOneCatalogIsMalformed()
    {
        using var handler = new CatalogHandler(Catalog()); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!)));
        handler.OtherText = "invalid";
        var results = await cache.SynchronizeAsync([Source, Source with { Id = "other", BaseUrl = "https://other.invalid/" }]);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Status == "current");
        Assert.Contains(results, r => r.Status == "failed" && r.Failure == "integrity");
    }

    [Fact]
    public async Task StoreBuildReplacementRetainsGameClientPolicy()
    {
        using var handler = new CatalogHandler(Catalog()); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, source => new HttpSource(new Uri(source.BaseUrl!)));
        await cache.GetAsync(Source);
        var local = GameCatalog.ParseBuild("kr-1.toml", Definition, "fixture");
        var policy = PolicyFixture.Rules("fixture.game-component", "require");
        var game = new GameDefinition("fixture", GameStatus.Enabled, new(new Dictionary<string, string> { ["en"] = "Fixture" }),
            "fixture.dll", "external", "", null, _root, [local]) { ClientPolicy = policy };
        var build = Assert.Single(Assert.Single(cache.Apply([game], [Source])).Builds);
        Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, "fixture.auth", "require"));
        Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, "fixture.game-component", "require"));
    }

    [Fact]
    public void PluginMinimumCannotDisagreeWithItsInlineManifest()
    {
        const string manifest = "schema = 1\n[plugin]\nid = \"fixture.auth\"\ngame = \"fixture\"\nversion = \"1.0.0\"\nkind = \"patch\"\nruntime = \">=3.0.0 <4.0.0\"\nminimum_launcher_version = \"0.2.0\"\nname = {en = \"Auth\"}\n[compatibility]\nbuilds = [\"kr-1\"]\n";
        var text = Catalog() + "\n[[packages]]\nid = \"fixture.auth\"\ngame = \"fixture\"\nversion = \"1.0.0\"\nkind = \"plugin\"\nbuilds = [\"kr-1\"]\nname = {en=\"Auth\"}\npublisher=\"Fixture\"\nsize=1\nmanifest=\"release.toml\"\nmanifest_sha256=\""
            + new string('a', 64) + "\"\nminimum_launcher_version=\"0.2.0\"\nplugin_definition=" + SessionWriter.Quote(manifest);
        Assert.Equal("0.2.0", Assert.Single(StoreCatalogFile.Parse("catalog", text).Packages).MinimumLauncherVersion);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", text.Replace("minimum_launcher_version=\"0.2.0\"", "minimum_launcher_version=\"0.1.0\"", StringComparison.Ordinal)));
    }

    private sealed class CatalogHandler(string text) : HttpMessageHandler
    {
        internal string Text = text;
        internal string? OtherText;
        internal int Requests;
        internal TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host == "other.invalid" ? OtherText ?? Text : Text) };
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
