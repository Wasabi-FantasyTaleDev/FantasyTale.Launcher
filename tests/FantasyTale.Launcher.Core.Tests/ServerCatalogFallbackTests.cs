using System.Net;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerCatalogFallbackTests
{
    private static readonly ServerDefinition Server = new("fixture", "test", "Local name", "https://game.example/", null, false, false)
    {
        Presentation = new()
        {
            Motd = "Local full introduction", Summary = "Local short introduction",
            MotdTranslations = new Dictionary<string, string> { ["en"] = "Local English", ["ko"] = "Local Korean" },
            SummaryTranslations = new Dictionary<string, string> { ["ko"] = "Local short Korean" },
            IconUrl = "https://images.example/local.png", Images = ["https://images.example/gallery.png"],
            IconUrlTranslations = new Dictionary<string, string> { ["en"] = "https://images.example/en.png" },
            ImagesTranslations = new Dictionary<string, IReadOnlyList<string>> { ["ko"] = ["https://images.example/ko.png"] },
            Whitelist = true, RosterEnabled = false, OnlinePlayers = 12, PeakOnlinePlayers = 100, PlayerLimit = 50, State = "maintenance",
        },
    };

    [Fact]
    public async Task CounterOnlyCatalogPreservesOmittedPresentationFieldsAndDoesNotWriteTheFallback()
    {
        var original = JsonSerializer.Serialize(Server);
        var calls = 0;
        using var handler = new Handler(() => { calls++; return Reply("\"presentation\":{\"onlinePlayers\":0}"); });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var catalog = await api.CatalogAsync(Server);
        var display = catalog.Presentation!;
        Assert.Equal(0, display.OnlinePlayers);
        Assert.Equal(100, display.PeakOnlinePlayers);
        Assert.Equal(50, display.PlayerLimit);
        Assert.False(display.RosterEnabled);
        Assert.True(display.Whitelist);
        Assert.Equal("maintenance", display.State);
        Assert.Equal(Server.Presentation!.Motd, display.Motd);
        Assert.Equal(Server.Presentation.Summary, display.Summary);
        Assert.Equal(Server.Presentation.Images, display.Images);
        Assert.Equal(Server.Presentation.IconUrl, display.IconUrl);
        Assert.Equal(Server.Presentation.MotdTranslations, display.MotdTranslations);
        Assert.Same(catalog, api.CachedCatalog(Server));
        Assert.Equal(1, calls);
        Assert.Equal(original, JsonSerializer.Serialize(Server));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"presentation\":null")]
    [InlineData("\"presentation\":{}")]
    public async Task MissingPresentationUsesTheSavedFallback(string presentation)
    {
        using var handler = new Handler(() => Reply(presentation));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var result = await api.CatalogAsync(Server);
        Assert.Equal(JsonSerializer.Serialize(Server.Presentation), JsonSerializer.Serialize(result.Presentation));
    }

    [Fact]
    public async Task ExplicitVisibilityNullLimitsAndEmptyArtworkOverrideSavedValues()
    {
        using var handler = new Handler(() => Reply("""
            "Presentation": {
                "RosterEnabled": true, "Whitelist": false, "OnlinePlayers": -1,
                "PeakOnlinePlayers": null, "PlayerLimit": null, "State": "online",
                "Motd": "", "Summary": "", "IconUrl": null, "Images": [],
                "MotdTranslations": {"EN":"Updated English"},
                "IconUrlTranslations": {"EN":"https://images.example/new.png"},
                "ImagesTranslations": {"KO":[]}
            }
            """));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var display = (await api.CatalogAsync(Server)).Presentation!;
        Assert.True(display.RosterEnabled);
        Assert.False(display.Whitelist);
        Assert.Equal(-1, display.OnlinePlayers);
        Assert.Null(display.PeakOnlinePlayers);
        Assert.Null(display.PlayerLimit);
        Assert.Equal("online", display.State);
        Assert.Empty(display.Motd);
        Assert.Empty(display.Summary);
        Assert.Null(display.IconUrl);
        Assert.Empty(display.Images);
        Assert.Equal(2, display.MotdTranslations.Count);
        Assert.Equal("Updated English", display.MotdTranslations["en"]);
        Assert.Equal("Local Korean", display.MotdTranslations["ko"]);
        Assert.Equal("Local short Korean", display.SummaryTranslations["ko"]);
        Assert.Equal("https://images.example/new.png", Assert.Single(display.IconUrlTranslations).Value);
        Assert.Empty(Assert.Single(display.ImagesTranslations).Value);
    }

    [Theory]
    [InlineData("\"onlinePlayers\":-2")]
    [InlineData("\"rosterEnabled\":null")]
    [InlineData("\"images\":null")]
    [InlineData("\"iconUrl\":\"file:///private/image.png\"")]
    [InlineData("\"motdTranslations\":{\"en\":null}")]
    public async Task MalformedRemoteFieldsCannotBeConcealedByValidLocalFallbacks(string invalid)
    {
        var body = "\"presentation\":{\"onlinePlayers\":0}";
        using var handler = new Handler(() => Reply(body));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var accepted = await api.CatalogAsync(Server);
        body = "\"presentation\":{" + invalid + "}";
        await Assert.ThrowsAsync<InvalidDataException>(() => api.CatalogAsync(Server));
        Assert.Same(accepted, api.CachedCatalog(Server));
    }

    [Fact]
    public async Task AnEndpointChangeCannotReuseThePreviousCatalogOrItsMergedPresentation()
    {
        using var handler = new Handler(() => Reply("\"presentation\":{\"onlinePlayers\":0}"));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.CatalogAsync(Server);
        Assert.Null(api.CachedCatalog(Server with { ApiUrl = "https://new.example/" }));
        api.Forget(Server.Id);
        Assert.Null(api.CachedCatalog(Server));
        var edited = Server with { Presentation = new() { RosterEnabled = true, Whitelist = false, Motd = "Edited locally" } };
        var display = (await api.CatalogAsync(edited)).Presentation!;
        Assert.Equal("Edited locally", display.Motd);
        Assert.True(display.RosterEnabled);
        Assert.False(display.Whitelist);
    }

    [Fact]
    public void ExplicitProfileRefreshUpdatesValidatedPresentationButRetainsStableIdentityAndEndpoint()
    {
        var current = Server with { Name = "Saved name", Builds = ["old-build"] };
        var catalog = new LauncherCatalog(1, "test", "Remote name", false, [],
            [new ServerInstance("preset", "new-build", new Dictionary<string, string> { ["en"] = "Build" }, new("new.example", 9000), "required")])
        {
            Presentation = new() { Motd = "Remote description" },
            ClientPolicy = new(1, [new("remote", "http", "https://store.example/", "catalog.toml")], [new("new.plugin", [new("need", "require", ["new-build"])])]),
        };
        var refreshed = ServerProfileRefresh.Apply(current, catalog);
        Assert.Equal(current.Id, refreshed.Id);
        Assert.Equal(current.ApiUrl, refreshed.ApiUrl);
        Assert.Equal(current.Endpoint, refreshed.Endpoint);
        Assert.Equal("Remote name", refreshed.Name);
        Assert.Equal(["new-build"], refreshed.Builds);
        Assert.Equal("Remote description", refreshed.Presentation!.Motd);
        Assert.Equal("new.plugin", Assert.Single(refreshed.ClientPolicy.Plugins).Id);
        Assert.Equal("remote", Assert.Single(refreshed.ClientPolicy.Sources).Name);
    }

    [Fact]
    public async Task SuccessBodiesAreParsedFromTheResponseStreamWithoutBufferingASecondPayload()
    {
        using var content = new StreamingContent("""
            {"schema":1,"enabled":true,"players":[{"name":"Fixture","level":50}]}
            """);
        using var handler = new Handler(() => new(HttpStatusCode.OK) { Content = content });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var result = await api.OnlinePlayersAsync(Server);
        Assert.Equal("Fixture", Assert.Single(result.Players).Name);
        Assert.True(content.StreamOpened);
    }

    private static HttpResponseMessage Reply(string fields) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"schema\":1,\"game\":\"test\",\"name\":\"Remote name\",\"registrationEnabled\":false,\"registrationFields\":[],\"instances\":[]"
            + (fields.Length == 0 ? "" : "," + fields) + "}", Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(response());
        }
    }

    private sealed class StreamingContent(string text) : HttpContent
    {
        internal bool StreamOpened { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("The response must be read directly from its stream.");
        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            StreamOpened = true;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }
    }
}
