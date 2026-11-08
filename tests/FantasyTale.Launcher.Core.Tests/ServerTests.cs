using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerTests
{
    private static readonly ServerDefinition Server = new("test", "bubblefighter", "Test", "http://game.example:8090", null, true, false);

    [Fact]
    public void OptionalLocalArtworkAndMultilineDescriptionSurviveReopeningAndCanBeCleared()
    {
        var root = Directory.CreateTempSubdirectory("ft-local-presentation-").FullName;
        try
        {
            var game = new GameDefinition("bubblefighter", GameStatus.Enabled,
                new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" }), "Fixture", "external", "#0078d4", null, root, []);
            var store = new ServerStore(root);
            store.Save(Server);
            Assert.Null(Assert.Single(new ServerStore(root).List([game])).Presentation);
            store.Save(Server with { Presentation = new()
            {
                Motd = "Welcome \"friends\"\nSecond line", IconUrl = "https://images.example/icon.png",
                Images = ["https://images.example/one.png", "https://images.example/two.png"], RosterEnabled = false,
            } });
            var restored = Assert.Single(new ServerStore(root).List([game])).Presentation!;
            Assert.Equal("Welcome \"friends\"\nSecond line", restored.Motd);
            Assert.Equal("https://images.example/icon.png", restored.IconUrl);
            Assert.Equal(["https://images.example/one.png", "https://images.example/two.png"], restored.Images);
            Assert.False(restored.RosterEnabled);
            Assert.Null(restored.OnlinePlayers);
            store.Save(Server with { Presentation = null });
            Assert.Null(Assert.Single(new ServerStore(root).List([game])).Presentation);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidLocalArtworkCannotReplaceAnExistingSavedServer()
    {
        var root = Directory.CreateTempSubdirectory("ft-local-presentation-invalid-").FullName;
        try
        {
            var store = new ServerStore(root); store.Save(Server);
            var original = File.ReadAllText(Path.Combine(root, "test.toml"));
            Assert.Throws<ArgumentException>(() => store.Save(Server with { Presentation = new() { Images = ["file:///private/image.png"] } }));
            Assert.Throws<ArgumentException>(() => store.Save(Server with { Presentation = new() { IconUrl = "https://user:secret@images.example/icon.png" } }));
            Assert.Equal(original, File.ReadAllText(Path.Combine(root, "test.toml")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("http://203.0.113.7:8090/v1/catalog", true)]
    [InlineData("http://[2001:db8::7]:8090/v1/catalog", true)]
    [InlineData("http://game.example:8090/v1/catalog", false)]
    [InlineData("https://203.0.113.7/v1/catalog", false)]
    [InlineData("https://game.example/v1/catalog", false)]
    public void ApiTransportOnlyBypassesTheProxyForPlainHttpIpAuthorities(string address, bool direct)
    {
        var upstream = new WebProxy("http://proxy.example:3128");
        using var handler = NetworkPolicy.CreateHttpHandler(upstream);
        var destination = new Uri(address);
        Assert.Equal(direct, handler.Proxy!.IsBypassed(destination));
        Assert.Equal(direct ? destination : upstream.Address, handler.Proxy.GetProxy(destination));
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Equal(4, handler.MaxConnectionsPerServer);
    }

    [Theory]
    [InlineData("disabled", null, "ip", null, "203.0.113.7")]
    [InlineData("all", true, "domain", "CN", "game.example")]
    [InlineData("disabled", true, null, null, "203.0.113.7")]
    [InlineData("all", false, null, "CN", "game.example")]
    [InlineData("china", null, null, "CN", "203.0.113.7")]
    [InlineData("china", null, null, "US", "game.example")]
    [InlineData("disabled", null, null, "CN", "game.example")]
    [InlineData("all", null, null, "US", "203.0.113.7")]
    public async Task ExplicitServerPolicyOverridesLocalPreferences(string global, bool? local, string? required, string? region, string expected)
    {
        var policy = new NetworkPolicy(() => global, (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }));
        var uri = await policy.RequestUriAsync(new Uri(Server.ApiUrl! + "/v1/catalog"), Server with { PreferIp = local }, required, region, default);
        Assert.Equal(expected, uri.Host); Assert.Equal(8090, uri.Port); Assert.Equal("/v1/catalog", uri.AbsolutePath);
        var secure = await policy.RequestUriAsync(new Uri("https://game.example/v1/catalog"), Server, "ip", region, default);
        Assert.Equal("game.example", secure.Host);
    }

    [Theory]
    [InlineData("http://game.example:8091", "CN", "http://203.0.113.7:8091")]
    [InlineData("http://game.example:8090/", "CN", "http://203.0.113.7:8090/")]
    [InlineData("http://GAME.example:8091/v1", "CN", "http://203.0.113.7:8091/v1")]
    [InlineData("http://game.example:8091/v1?next=http://game.example:8091/#part", "CN", "http://203.0.113.7:8091/v1?next=http://game.example:8091/#part")]
    [InlineData("http://game.example:80/v1", "CN", "http://203.0.113.7/v1")]
    [InlineData("http://game.example:8091", "US", "http://game.example:8091")]
    [InlineData("http://198.51.100.4:8091", "CN", "http://198.51.100.4:8091")]
    [InlineData("https://game.example:8091", "CN", "https://game.example:8091")]
    public async Task AddressesHandedToTheGameTakeTheLauncherRouteAndKeepTheirText(string url, string region, string expected)
    {
        var policy = new NetworkPolicy(() => "china", (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }));
        Assert.Equal(expected, await policy.RouteAsync(url, Server, null, region, default));
    }

    [Fact]
    public void PreferencesAndServerDefinitionsPersistWithoutInstanceBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "ft-server-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new NetworkSettings(root); Assert.Equal("china", settings.IpPreference);
            settings.Save("disabled"); Assert.Equal("disabled", new NetworkSettings(root).IpPreference);
            Assert.Throws<ArgumentException>(() => settings.Save("unknown"));
            var store = new ServerStore(Path.Combine(root, "servers")); store.Save(Server with { PreferIp = false });
            var file = File.ReadAllText(Path.Combine(root, "servers", "test.toml")); Assert.Contains("prefer_ip = false", file); Assert.DoesNotContain("password", file);
            store.Save(Server with { PreferIp = null }); Assert.DoesNotContain("prefer_ip", File.ReadAllText(Path.Combine(root, "servers", "test.toml")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryFallsBackWithoutCredentialsAndServerPolicyControlsLogin()
    {
        var handler = new StubHandler(); using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, new NetworkPolicy(() => "disabled", (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") })));
        var profile = await api.LoginAsync(Server, "test-user", "local-password");
        Assert.Equal("test-user", profile.Username); Assert.Equal(["game.example", "203.0.113.7", "203.0.113.7"], handler.Hosts);
        var other = Server with { Id = "other-server" };
        Assert.Null(api.Current(other));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CredentialsAsync(other, "kr-1256", true));
        Assert.Equal(3, handler.Hosts.Count);
        await api.LoginAsync(other, "test-user", "local-password");
        Assert.NotNull(api.Current(Server)); Assert.Null(api.Current(Server with { ApiUrl = "http://other.example/" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CredentialsAsync(Server, "kr-1256", true));
        Assert.NotNull(api.Current(other));
        api.Forget(Server.Id);
        Assert.NotNull(api.Current(other));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        internal List<string> Hosts { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri.Host == "game.example") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            object body = request.RequestUri.AbsolutePath.EndsWith("catalog", StringComparison.Ordinal)
                ? new LauncherCatalog(1, "bubblefighter", "Test", true, [], []) { HostPolicy = "ip", Region = "CN" }
                : new { accessToken = "test-session", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), refreshToken = "test-refresh", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = new AccountProfile("test-user", "Test", new Dictionary<string, long?> { ["lucci"] = 12 }, null) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
