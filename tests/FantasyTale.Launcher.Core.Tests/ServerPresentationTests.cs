using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerPresentationTests
{
    [Fact]
    public async Task PublicRosterPreservesCompletenessAndTruncationIndependently()
    {
        using var handler = new Handler(_ => Json(new OnlinePlayerList(1, true, [new("Fixture", 1)]) { Complete = false, Truncated = true }));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var result = await api.OnlinePlayersAsync(Server);
        Assert.False(result.Complete);
        Assert.True(result.Truncated);
        Assert.Single(result.Players);
        var legacy = JsonSerializer.Deserialize<OnlinePlayerList>("""{"schema":1,"enabled":true,"players":[]}""", JsonSerializerOptions.Web)!;
        Assert.Null(legacy.Complete);
        Assert.False(legacy.Truncated);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("cn-279", true)]
    [InlineData("future-build", true)]
    [InlineData("../invalid", false)]
    [InlineData("", false)]
    public async Task PublicRosterAcceptsOptionalBuildIdsWithoutGuessingUnsupportedVersions(string? buildId, bool valid)
    {
        using var handler = new Handler(_ => Json(new OnlinePlayerList(1, true, [new("Fixture", 1) { BuildId = buildId }])));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        if (!valid) { await Assert.ThrowsAsync<InvalidDataException>(() => api.OnlinePlayersAsync(Server)); return; }
        Assert.Equal(buildId, Assert.Single((await api.OnlinePlayersAsync(Server)).Players).BuildId);
    }

    private static readonly ServerDefinition Server = new("fixture", "test", "Fixture", "https://game.example/", null, true, false);
    private static readonly BuildDefinition Build = new("kr-1", "kr", 1, new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" }), "game.exe", "", "", null, null, "test", [], []);
    private static PluginDefinition Package(string id, string kind) => new(id, "1.0.0", "test", kind, "", "", Build.Name, null, [], "", "", ["kr-1"], [], [], [], null, null);

    [Fact]
    public void UnifiedStoreCarriesPresentationPoliciesAndPluginSources()
    {
        var catalog = StoreCatalogFile.Parse("source.toml", """
            schema=2
            [catalog]
            id="fixture"
            name={en="Fixture"}
            [[servers]]
            id="community"
            kind="server"
            game="test"
            name="Community"
            api_url="https://game.example/"
            builds=["kr-1"]
            [servers.presentation]
            motd="Welcome\nHave fun"
            online_players=0
            peak_online_players=-1
            state="maintenance"
            whitelist=true
            [[servers.client_policy_rules]]
            plugin="test.optional"
            id="off"
            type="disable"
            [[servers.client_policy_sources]]
            name="community"
            kind="github"
            base_url="https://github.com/fixture/plugins/tree/main/public"
            """);
        var details = Assert.Single(catalog.Servers).Details;
        Assert.Equal("Welcome\nHave fun", details.Presentation!.Motd);
        Assert.Equal(0, details.Presentation.OnlinePlayers);
        Assert.Equal(-1, details.Presentation.PeakOnlinePlayers);
        Assert.Null(details.Presentation.PlayerLimit);
        Assert.True(details.Presentation.RosterEnabled);
        Assert.True(details.Presentation.Whitelist);
        Assert.Equal("disable", Assert.Single(Assert.Single(details.ClientPolicy.Plugins).Rules).Type);
        Assert.Equal("source.toml", Assert.Single(details.ClientPolicy.Sources).CatalogPath);
    }

    [Fact]
    public void InvalidCountersAndPolicyConflictsCannotMasqueradeAsHiddenData()
    {
        ServerMetadataPolicy.Validate(new ServerPresentation { OnlinePlayers = -1, PeakOnlinePlayers = 0, PlayerLimit = null });
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ServerPresentation { OnlinePlayers = -2 }));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ServerPresentation { PlayerLimit = 0 }));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ClientPolicy(1, [], [new("test.plugin", [new("need", "require", []), new("off", "disable", [])])])));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ClientPolicy(1, [], [new(null!, [])])));
    }

    [Theory]
    [InlineData("patch")]
    [InlineData("native")]
    [InlineData("files")]
    [InlineData("overlay")]
    [InlineData("service")]
    public void DisabledPluginsOnlyChangeThisLaunchAndRespectBuildScope(string kind)
    {
        var package = Package("test.optional", kind);
        var original = new InstanceDefinition("client", "Fixture", "test", Build.Id, null,
            [new("test.optional", "1.0.0", true, false, new Dictionary<string, object>())]);
        var snapshot = JsonSerializer.Serialize(original);
        var result = ServerPluginPolicy.ForLaunch(original, Build, [package], [new ServerPluginRule("test.optional") { Disabled = true }]);
        Assert.False(Assert.Single(result.Plugins).Enabled);
        Assert.True(Assert.Single(original.Plugins).Enabled);
        Assert.Equal(snapshot, JsonSerializer.Serialize(original));
        Assert.Same(original, ServerPluginPolicy.ForLaunch(original, Build, [package], ServerPluginPolicy.ForBuild(new ClientPolicy(1, [], [new("test.optional", [new("off", "disable", ["cn-1"])])]), Build.Id)));
        Assert.Same(original, ServerPluginPolicy.ForLaunch(original, Build, [package], []));
        if (kind == "files") Assert.Empty(FilePluginResolver.Resolve(result, Build, [package]));
    }

    [Fact]
    public void RequiredAndPermanentFilesCannotBeSilentlySuppressed()
    {
        var selection = new InstancePlugin("test.optional", "1.0.0", true, false, new Dictionary<string, object>());
        var instance = new InstanceDefinition("client", "Fixture", "test", Build.Id, null, [selection]);
        ServerPluginRule[] disabled = [new("test.optional") { Disabled = true }];
        Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.ForLaunch(instance with { Plugins = [selection with { Required = true }] }, Build, [Package(selection.Id, "native")], disabled));
        Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.ForLaunch(instance, Build with { ClientPolicy = PolicyFixture.Rules(selection.Id, "require") }, [Package(selection.Id, "native")], disabled));
        Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.ForLaunch(instance, Build, [Package(selection.Id, "files") with { RestoreFiles = false }], disabled));
    }

    [Theory]
    [InlineData("wrong_password")]
    [InlineData("account_not_found")]
    [InlineData("account_banned")]
    [InlineData("server_full")]
    [InlineData("server_maintenance")]
    [InlineData("server_not_ready")]
    [InlineData("not_whitelisted")]
    public async Task LoginRejectionsRetainPublicReasonsButNeverRequestSecrets(string code)
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/v1/catalog"
            ? Json(new LauncherCatalog(1, "test", "Fixture", false, [], []))
            : Json(new { error = code, reason = "Public reason", maintenanceStart = "2026-10-22T12:00:00Z", maintenanceDescription = "Scheduled work", password = "fixture-password" }, HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var error = await Assert.ThrowsAsync<ServerFailureException>(() => api.LoginAsync(Server, "fixture", "fixture-password"));
        Assert.Equal(code, error.Code);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("Public reason", error.Reason);
        Assert.Equal("Scheduled work", error.MaintenanceDescription);
        Assert.NotNull(error.MaintenanceStart);
        Assert.DoesNotContain("fixture-password", error.ToString());
        Assert.DoesNotContain("game.example", error.ToString());
        Assert.Null(api.Current(Server));
    }

    [Fact]
    public async Task NetworkFailuresAreSanitizedAndCallerCancellationIsPreserved()
    {
        using var handler = new Handler(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "Secret URL https://game.example/?token=private"));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var error = await Assert.ThrowsAsync<ServerFailureException>(() => api.OnlinePlayersAsync(Server));
        Assert.Equal("network_error", error.Code);
        Assert.Equal("NameResolutionError", error.TransportCode);
        Assert.DoesNotContain("private", error.ToString());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.OnlinePlayersAsync(Server, cancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicRosterHasNoBearerAndHiddenListsDiscardNames(bool enabled)
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("/v1/players", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Json(new OnlinePlayerList(1, enabled, Enumerable.Range(1, 1250).Select(index => new OnlinePlayer("Player" + index, index % 2 == 0 ? index : null)).ToArray()));
        });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var result = await api.OnlinePlayersAsync(Server);
        Assert.Equal(enabled ? 1250 : 0, result.Players.Count);
        if (enabled) { Assert.Null(result.Players[0].Level); Assert.Equal(2, result.Players[1].Level); }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
}
