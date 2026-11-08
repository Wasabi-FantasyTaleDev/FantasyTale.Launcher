using System.Net;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class StoreServerDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-store-discovery-").FullName;
    private static readonly StoreSource First = new("first", "First", "http", "https://first.invalid/", "source.toml", true, false);
    private const string Header = "schema=2\n[catalog]\nid=\"fixture\"\nname={en=\"Fixture\"}\n";
    private static string Server(string id = "one", string host = "203.0.113.7", int port = 10101, string kind = "server", string? expires = null) =>
        $"\n[[servers]]\nid=\"{id}\"\nkind=\"{kind}\"\ngame=\"fixture\"\nname=\"Fixture {id}\"\napi_url=\"https://{host}/\"\nhost=\"{host}\"\nport={port}\nbuilds=[\"kr-1\"]\n"
        + (expires is null ? "" : $"expires_at=\"{expires}\"\n");

    [Fact]
    public async Task SharedCatalogDiscoversBothKindsWithoutSavingAndPersistsItsOfflineSnapshot()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        using var handler = new Handler(Header + Server() + Server("room", "203.0.113.8", kind: "room", expires: expiry));
        using var http = new HttpClient(handler);
        await using (var cache = new StoreCatalogCache(Path.Combine(_root, "cache"), http, source => SourceTransport.Open(http, source.Kind, new Uri(source.BaseUrl!))))
        {
            await cache.SynchronizeAsync([First]);
            var discovery = new StoreServerDiscovery(cache);
            var listing = await discovery.ListAsync([First]);
            Assert.Equal(2, listing.Entries.Count);
            Assert.Equal(["server", "room"], listing.Entries.Select(entry => entry.Kind));
            Assert.Equal(1, handler.Requests);
            Assert.Empty(new Core.Servers.ServerStore(Path.Combine(_root, "saved")).List([]));
        }
        await using var restored = new StoreCatalogCache(Path.Combine(_root, "cache"), http, source => SourceTransport.Open(http, source.Kind, new Uri(source.BaseUrl!)));
        handler.Fail = true;
        await restored.SynchronizeAsync([First]);
        Assert.Equal("failed", restored.State(First).Status);
        Assert.Equal(2, (await new StoreServerDiscovery(restored).ListAsync([First])).Entries.Count);
    }

    [Fact]
    public async Task SourcesAreDeduplicatedByResolvedGameIpAndPortWithSourcePriorityAndExpiredRoomRemoval()
    {
        using var handler = new Handler(Header + Server()); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, source => SourceTransport.Open(http, source.Kind, new Uri(source.BaseUrl!)));
        await cache.GetAsync(First);
        handler.Text = Header + Server("copy", "game.example") + Server("other-port", port: 10102)
            + Server("expired", "203.0.113.9", kind: "room", expires: "2020-01-01T00:00:00Z");
        var other = First with { Id = "second", BaseUrl = "https://second.invalid/" };
        await cache.GetAsync(other);
        var resolves = 0;
        var discovery = new StoreServerDiscovery(cache, (_, _) => { resolves++; return Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }); });
        var result = await discovery.ListAsync([First, other]);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("Fixture one", result.Entries[0].Details.Name);
        Assert.Equal(10102, result.Entries[1].Details.Endpoint!.Port);
        Assert.Equal(1, resolves);
        Assert.Single((await discovery.ListAsync([First, other with { Enabled = false }])).Entries);
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData("kind=\"server\"", "kind=\"invalid\"")]
    [InlineData("port=10101", "port=0")]
    [InlineData("port=10101", "")]
    [InlineData("https://203.0.113.7/", "https://user:secret@203.0.113.7/")]
    [InlineData("kind=\"server\"", "kind=\"room\"")]
    public void InvalidDiscoveryNeverBecomesAnAcceptedSnapshot(string original, string replacement) =>
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("fixture", Header + Server().Replace(original, replacement, StringComparison.Ordinal)));

    [Fact]
    public void DuplicateEntriesAndInvalidDirectoryDefaultsAreRejected()
    {
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("fixture", Header + Server() + Server()));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("fixture", Header + Server("Community_Server") + Server("community_SERVER")));
        Assert.Equal("Community_Server", Assert.Single(StoreCatalogFile.Parse("fixture", Header + Server("Community_Server")).Servers).Id);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("fixture", Header + "\n[[directory_sources]]\nid=\"live\"\nname=\"Live\"\nbase_url=\"file:///local\"\n"));
        var catalog = StoreCatalogFile.Parse("fixture", Header + "\n[[directory_sources]]\nid=\"live\"\nname=\"Live\"\nbase_url=\"https://directory.example/\"\n");
        var store = new DirectorySourceStore(_root);
        var live = store.List(catalog.DirectorySources).Single(source => source.Id == "live");
        store.Save(live with { Enabled = false });
        Assert.False(store.List(catalog.DirectorySources).Single(source => source.Id == "live").Enabled);
        Assert.Throws<InvalidOperationException>(() => store.Delete("live", catalog.DirectorySources));
        Assert.False(store.List().Single(source => source.Id == "live").BuiltIn);
    }

    [Theory]
    [InlineData("http", "https://store.invalid/")]
    [InlineData("github", "https://github.com/operator/catalog/")]
    [InlineData("alist", "https://store.invalid/")]
    public async Task ProfileIsRequestedOnlyAfterExplicitSelectionAndUsesTheSameTransport(string kind, string url)
    {
        var profile = new ServerDefinition("one", "fixture", "Full server name", "https://203.0.113.7/", new("203.0.113.7", 10101), true, false)
        { Builds = ["kr-1"], Presentation = new() { SummaryTranslations = new Dictionary<string, string> { ["ko"] = "Short" } } };
        var listing = Header + Server() + "profile=\"servers/profile.toml\"\n";
        using var handler = new ProfileHandler(listing, Core.Servers.ServerProfileFile.Format(profile));
        using var http = new HttpClient(handler);
        var source = First with { Kind = kind, BaseUrl = url };
        IDownloadSource Open(StoreSource source) => SourceTransport.Open(http, source.Kind, new Uri(source.BaseUrl!));
        await using var cache = new StoreCatalogCache(Path.Combine(_root, "cache"), http, Open);
        var discovery = new StoreServerDiscovery(cache);
        var entry = Assert.Single((await discovery.ListAsync([source])).Entries);
        Assert.Equal(0, handler.ProfileRequests);
        await discovery.ListAsync([source]);
        Assert.Equal(0, handler.ProfileRequests);
        var fetched = await StoreServerDiscovery.DownloadProfileAsync(http, Open, source, entry);
        Assert.Equal(1, handler.ProfileRequests);
        Assert.Equal("Short", fetched.Presentation!.SummaryTranslations["ko"]);
        Assert.Empty(new Core.Servers.ServerStore(Path.Combine(_root, "saved")).List([]));
        handler.Profile = handler.Profile.Replace("id = \"one\"", "id = \"changed\"", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => StoreServerDiscovery.DownloadProfileAsync(http, Open, source, entry));
    }

    private sealed class ProfileHandler(string catalog, string profile) : HttpMessageHandler
    {
        internal int ProfileRequests;
        internal string Profile = profile;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/fs/get", StringComparison.Ordinal))
            {
                var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var path = body.RootElement.GetProperty("path").GetString();
                return new(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(new { code = 200, data = new { raw_url = "https://storage.invalid" + path, size = System.Text.Encoding.UTF8.GetByteCount(path!.EndsWith("profile.toml", StringComparison.Ordinal) ? Profile : catalog) } }) };
            }
            var isProfile = request.RequestUri.AbsolutePath.EndsWith("profile.toml", StringComparison.Ordinal);
            if (isProfile) ProfileRequests++;
            return new(HttpStatusCode.OK) { Content = new StringContent(isProfile ? Profile : catalog) };
        }
    }

    private sealed class Handler(string text) : HttpMessageHandler
    {
        internal string Text = text;
        internal bool Fail;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Fail) throw new HttpRequestException("Unavailable", null, HttpStatusCode.BadRequest);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Text) });
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
