using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AccountRoutingTests
{
    private static readonly ServerDefinition Server = new("test", "bubblefighter", "Test", "http://game.example:8090", null, true, false);
    private static readonly AccountProfile Profile = new("alice", "Alice", new Dictionary<string, long?>(), null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredRefreshAndLogoutDiscoverIpPolicyBeforeTransmittingCredentials(bool logout)
    {
        var store = new Store();
        using var handler = new Handler("ip", rejectDomain: true);
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy("disabled"), sessionStore: store);
        if (logout)
        {
            await api.LogoutAsync(Server);
            Assert.Empty(store.Load());
        }
        else
        {
            Assert.Equal("alice", (await api.RefreshIfNeededAsync(Server))!.Username);
            Assert.Equal("renewed-refresh", Assert.Single(store.Load()).RefreshToken);
        }
        Assert.Equal(["game.example/v1/catalog", "203.0.113.7/v1/catalog",
            "203.0.113.7/v1/auth/" + (logout ? "logout" : "refresh")], handler.Requests);
    }

    [Fact]
    public async Task RestoredRefreshHonorsExplicitDomainPolicyOverGlobalIpPreference()
    {
        using var handler = new Handler("domain", rejectDomain: false);
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy("all"), sessionStore: new Store());
        Assert.Equal("alice", (await api.RefreshIfNeededAsync(Server))!.Username);
        Assert.Equal(["203.0.113.7/v1/catalog", "game.example/v1/auth/refresh"], handler.Requests);
    }

    [Fact]
    public async Task RejectedDiscoveryDoesNotTransmitOrDiscardRestoredRefreshCredentials()
    {
        var store = new Store();
        using var handler = new Handler("ip", rejectDomain: false) { RejectCatalog = true };
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy("disabled"), sessionStore: store);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => api.RefreshIfNeededAsync(Server));
        Assert.Equal(["game.example/v1/catalog"], handler.Requests);
        Assert.Equal("saved-refresh", Assert.Single(store.Load()).RefreshToken);
        Assert.Equal("alice", api.Current(Server)!.Username);
        handler.RejectCatalog = false;
        Assert.Equal("alice", (await api.RefreshIfNeededAsync(Server))!.Username);
        Assert.Equal("renewed-refresh", Assert.Single(store.Load()).RefreshToken);
    }

    private static NetworkPolicy Policy(string preference) => new(() => preference,
        (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") }));

    private sealed class Handler(string policy, bool rejectDomain) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal bool RejectCatalog { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.Host + uri.AbsolutePath);
            if (uri.AbsolutePath == "/v1/catalog")
            {
                Assert.Null(request.Content);
                Assert.Null(request.Headers.Authorization);
                if (RejectCatalog) return new(HttpStatusCode.Unauthorized);
                if (rejectDomain && uri.Host == "game.example") return new(HttpStatusCode.Forbidden);
                return Reply(new LauncherCatalog(1, "bubblefighter", "Test", false, [], []) { HostPolicy = policy, Region = "CN" });
            }
            Assert.Contains(Requests, path => path.EndsWith("/v1/catalog", StringComparison.Ordinal));
            Assert.Equal(policy == "ip" ? "203.0.113.7" : "game.example", uri.Host);
            Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(ct);
            Assert.Equal("saved-refresh", body!["refreshToken"]);
            return uri.AbsolutePath == "/v1/auth/logout" ? Reply(new { signedOut = true }) : Reply(new
            {
                accessToken = "renewed-access", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                refreshToken = "renewed-refresh", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30), account = Profile
            });
        }
        private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class Store : IAccountSessionStore
    {
        private IReadOnlyList<PersistedAccountSession> _entries = [new(Server.Id, Server.ApiUrl!, "alice", "saved-refresh",
            DateTimeOffset.UtcNow.AddDays(30), Profile, true)];
        public IReadOnlyList<PersistedAccountSession> Load() => _entries;
        public void Save(string serverId, string apiUrl, string? selected, IReadOnlyList<PersistedAccountSession> sessions) => _entries = sessions.ToArray();
        public void Remove(string serverId) => _entries = [];
    }
}
