using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Verifies catalog policy validation, effective API routing and anonymous game-login grants.</summary>
public sealed class ServerGameLoginTests
{
    private static ServerDefinition Server => new("fixture", "bubblefighter", "Fixture", "http://203.0.113.10/api/", null, true, false);
    private static LauncherCatalog Catalog => new(1, "bubblefighter", "Fixture", false, [], []) { HostPolicy = "domain", LauncherLogin = "game" };
    private static GameLoginLaunch Launch => new(new string('a', 64), new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(5));
    private static BuildDefinition Build => GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games"))
        .Single(game => game.Id == "bubblefighter").Builds.Single(build => build.Id == "id-400");
    private static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    /// <summary>Applies the local HTTPS preference without losing API paths or nondefault ports.</summary>
    /// <param name="entered">The stored API address.</param>
    /// <param name="useHttps">Whether the local profile requests HTTPS.</param>
    /// <param name="expected">The normalized effective address.</param>
    [Theory]
    [InlineData("http://game.example/api", false, "http://game.example/api/")]
    [InlineData("http://game.example/api/", true, "https://game.example/api/")]
    [InlineData("http://game.example:80/api/", true, "https://game.example/api/")]
    [InlineData("http://game.example:8090/api/", true, "https://game.example:8090/api/")]
    [InlineData("https://game.example:8443/api/", false, "https://game.example:8443/api/")]
    [InlineData("https://game.example/api/", true, "https://game.example/api/")]
    public void LocalTransportPreferencePreservesPathsAndExplicitNondefaultPorts(string entered, bool useHttps, string expected)
    {
        using var api = new LauncherApiClient();
        Assert.Equal(expected, api.EffectiveApiUrl(Server with { ApiUrl = entered, UseHttps = useHttps }));
    }

    /// <summary>Uses an advertised secure address only when the local HTTPS preference permits it.</summary>
    /// <param name="useHttps">The local fallback transport preference.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvertisedSecureAddressRespectsTheLocalSwitchAndARefreshCanRemoveIt(bool useHttps)
    {
        string? secure = "https://secure.example:8443/service/";
        using var handler = new Handler((_, _) => Task.FromResult(Reply(Catalog with { SecureUrl = secure })));
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        var server = Server with { UseHttps = useHttps };
        await api.CatalogAsync(server);
        Assert.Equal(useHttps ? secure : Server.ApiUrl, api.EffectiveApiUrl(server));
        Assert.Equal("http://203.0.113.11/api/", api.EffectiveApiUrl(Server with { ApiUrl = "http://203.0.113.11/api/" }));
        secure = null;
        await api.CatalogAsync(server);
        Assert.Equal(useHttps ? "https://203.0.113.10/api/" : Server.ApiUrl, api.EffectiveApiUrl(server));
    }

    /// <summary>Retains the last validated catalog and routing when a refreshed secure address is invalid.</summary>
    /// <param name="address">The invalid advertised secure address.</param>
    [Theory]
    [InlineData("http://game.example/")]
    [InlineData("https://game.example/?option=1")]
    [InlineData("https://game.example/#section")]
    [InlineData("not-a-url")]
    public async Task InvalidCatalogAddressesDoNotReplaceTheLastValidatedRouting(string address)
    {
        var catalog = Catalog with { SecureUrl = "https://secure.example/api/" };
        using var handler = new Handler((_, _) => Task.FromResult(Reply(catalog)));
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        var server = Server with { UseHttps = true };
        var accepted = await api.CatalogAsync(server);
        catalog = catalog with { SecureUrl = address };
        await Assert.ThrowsAsync<InvalidDataException>(() => api.CatalogAsync(server));
        Assert.Same(accepted, api.CachedCatalog(server));
        Assert.Equal("https://secure.example/api/", api.EffectiveApiUrl(server));
    }

    /// <summary>Accepts game-entered login at both the server and individual instance policy levels.</summary>
    [Fact]
    public async Task CatalogAcceptsGameLoginForTheServerAndItsIndividualInstances()
    {
        var catalog = Catalog with { Instances = [new("id-400", "id-400", new Dictionary<string, string> { ["en"] = "Fixture" }, null, "game")] };
        using var handler = new Handler((_, _) => Task.FromResult(Reply(catalog)));
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        var discovered = await api.CatalogAsync(Server);
        Assert.Equal("game", discovered.LauncherLogin);
        Assert.Equal("game", Assert.Single(discovered.Instances).LauncherLogin);
        Assert.Equal("game", LauncherLoginPolicy.Resolve(null, "required", discovered.LauncherLogin));
    }

    /// <summary>Rejects an unknown login policy without caching the malformed catalog.</summary>
    /// <param name="instance">Whether the unknown policy belongs to an instance instead of the server.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownLoginPoliciesAreRejectedAtEitherCatalogLevel(bool instance)
    {
        var catalog = instance
            ? Catalog with { Instances = [new("fixture", "id-400", new Dictionary<string, string>(), null, "unknown")] }
            : Catalog with { LauncherLogin = "unknown" };
        using var handler = new Handler((_, _) => Task.FromResult(Reply(catalog)));
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.CatalogAsync(Server));
        Assert.Null(api.CachedCatalog(Server));
    }

    /// <summary>Discovers the server once and sends installed metadata in each anonymous launch request.</summary>
    /// <param name="useHttps">The local transport preference.</param>
    /// <param name="secureUrl">The optional catalog override.</param>
    /// <param name="expectedUrl">The launch endpoint selected from those settings.</param>
    [Theory]
    [InlineData(false, null, "http://203.0.113.10/api/v1/launch")]
    [InlineData(true, null, "https://203.0.113.10/api/v1/launch")]
    [InlineData(false, "https://secure.example/service/", "http://203.0.113.10/api/v1/launch")]
    [InlineData(true, "https://secure.example/service/", "https://secure.example/service/v1/launch")]
    public async Task AnonymousLaunchUsesDiscoveryAndSendsTheInstalledVersionsAndLanguages(bool useHttps, string? secureUrl, string expectedUrl)
    {
        var discoveries = 0;
        var launches = 0;
        using var handler = new Handler(async (request, ct) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("0.1.32", Assert.Single(request.Headers.GetValues("X-FantasyTale-Launcher-Version")));
            if (request.Method == HttpMethod.Get)
            {
                discoveries++;
                Assert.Equal("http://203.0.113.10/api/v1/catalog", request.RequestUri!.AbsoluteUri);
                return Reply(Catalog with { SecureUrl = secureUrl });
            }
            launches++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(expectedUrl, request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var json = body.RootElement;
            Assert.Equal(8, json.EnumerateObject().Count());
            Assert.Equal("id-400", json.GetProperty("build").GetString());
            Assert.Equal("0.1.32", json.GetProperty("launcherVersion").GetString());
            Assert.Equal(400, json.GetProperty("protocol_version").GetInt32());
            Assert.Equal("release 2014", json.GetProperty("release_version").GetString());
            Assert.Equal("patch 1", json.GetProperty("patch_version").GetString());
            Assert.Equal("en", json.GetProperty("launcher_language").GetString());
            Assert.Equal("id", json.GetProperty("native_language").GetString());
            Assert.Equal("zh-CN", json.GetProperty("client_language").GetString());
            return Reply(Launch);
        });
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, launcherVersion: "0.1.32");
        var server = Server with { UseHttps = useHttps };
        for (var i = 0; i < 2; i++)
        {
            var launch = await api.OpenGameLoginAsync(server, Build, new(" release 2014 ", " patch 1 "), new("en", "id", "zh-CN"));
            Assert.Equal(Launch.LaunchId, launch.LaunchId);
            Assert.Equal(Launch.LaunchKey, launch.LaunchKey);
        }
        Assert.Equal(1, discoveries);
        Assert.Equal(2, launches);
        Assert.Null(api.Current(server));
    }

    /// <summary>Rejects malformed server grants without creating a signed-in session or exposing the key.</summary>
    /// <param name="scenario">The malformed identity or key case to exercise.</param>
    [Theory]
    [InlineData("short-id")]
    [InlineData("uppercase-id")]
    [InlineData("missing-id")]
    [InlineData("short-key")]
    [InlineData("uppercase-key")]
    [InlineData("missing-key")]
    public async Task MalformedAnonymousLaunchCredentialsAreRefusedWithoutBeingExposed(string scenario)
    {
        var launch = scenario switch
        {
            "short-id" => Launch with { LaunchId = new string('a', 32) },
            "uppercase-id" => Launch with { LaunchId = new string('A', 64) },
            "missing-id" => Launch with { LaunchId = null! },
            "short-key" => Launch with { LaunchKey = new string('b', 63) },
            "uppercase-key" => Launch with { LaunchKey = new string('B', 64) },
            "missing-key" => Launch with { LaunchKey = null! },
            _ => throw new ArgumentException(scenario),
        };
        using var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Reply(Catalog) : Reply(launch)));
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => api.OpenGameLoginAsync(Server, Build, new(null, null), new("", "", "")));
        Assert.DoesNotContain(Launch.LaunchKey, error.ToString(), StringComparison.Ordinal);
        Assert.Null(api.Current(Server));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(request, ct);
    }
}
