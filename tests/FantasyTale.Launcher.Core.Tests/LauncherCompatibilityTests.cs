using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LauncherCompatibilityTests
{
    private static readonly ServerDefinition Server = new("test", "bubblefighter", "Test server", "https://game.example/", null, true, false);

    [Theory]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("1.0.9", "1.0.10", false)]
    [InlineData("1.0.10-rc.1", "1.0.10", false)]
    [InlineData("1.0.10", "1.0.10-rc.1", true)]
    [InlineData("1.0.10+source", "1.0.10", true)]
    [InlineData("0.1.14", "", true)]
    public void MinimumUsesNumericSemVerAndPrereleasePrecedence(string current, string minimum, bool accepted) =>
        Assert.Equal(accepted, LauncherCompatibility.Supports(minimum, current));

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData(">=1.0.0")]
    [InlineData("9999999999999.0.0")]
    public void MalformedMinimumCannotSilentlyDisableTheGate(string minimum) =>
        Assert.Throws<InvalidDataException>(() => LauncherCompatibility.Supports(minimum));

    [Fact]
    public async Task LoginReportsExactReleaseAndTheAccountPolicyReplacesTheCatalogs()
    {
        using var handler = new VersionHandler(); using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, launcherVersion: "1.0.10");
        await api.LoginAsync(Server, "tester", "fixture-password");
        Assert.Equal("1.0.10", handler.BodyVersion); Assert.Equal(1, handler.Logins);
        var items = ServerPluginPolicy.ForBuild(api.ClientPolicy(Server), "kr-812");
        Assert.Equal(2, items.Count); Assert.True(items.Single(item => item.Id == "test.required").Required);
        // The sign-in policy scopes everything to kr-812; the catalog's all-build recommendation no longer applies.
        Assert.Empty(ServerPluginPolicy.ForBuild(api.ClientPolicy(Server), "cn-404"));
        Assert.Empty(api.ClientPolicy(Server with { Id = "other", ApiUrl = "https://other.example/" }).Plugins);
        handler.CatalogMinimum = "2.0.0";
        await Assert.ThrowsAsync<LauncherUpgradeRequiredException>(() => api.CatalogAsync(Server));
        await api.LogoutAsync(Server); Assert.Null(api.Current(Server));
    }

    [Fact]
    public async Task OutdatedCatalogBlocksBeforeSendingCredentials()
    {
        using var handler = new VersionHandler { CatalogMinimum = "1.0.11" }; using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, launcherVersion: "1.0.10");
        var error = await Assert.ThrowsAsync<LauncherUpgradeRequiredException>(() => api.LoginAsync(Server, "tester", "fixture-password"));
        Assert.Equal("1.0.11", error.MinimumVersion); Assert.Equal(0, handler.Logins); Assert.Null(api.Current(Server));
    }

    [Fact]
    public async Task UpgradeRejectionKeepsSessionAndAllowsLogout()
    {
        using var handler = new VersionHandler(); using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, launcherVersion: "1.0.10");
        await api.LoginAsync(Server, "tester", "fixture-password");
        handler.RejectAccount = true;
        var error = await Assert.ThrowsAsync<LauncherUpgradeRequiredException>(() => api.RefreshAsync(Server));
        Assert.Equal("2.0.0", error.MinimumVersion); Assert.Equal("tester", api.Current(Server)?.Username);
        await api.LogoutAsync(Server); Assert.Null(api.Current(Server));
    }

    [Fact]
    public async Task RestoredOutdatedAccountCanLogoutWithoutSendingItsPassword()
    {
        var store = new SessionStore();
        using var handler = new VersionHandler(); using var http = new HttpClient(handler);
        using (var first = new LauncherApiClient(http, sessionStore: store, launcherVersion: "1.0.10"))
            await first.LoginAsync(Server, "tester", "fixture-password");
        handler.CatalogMinimum = "2.0.0";
        using var restored = new LauncherApiClient(http, sessionStore: store, launcherVersion: "1.0.10");
        await restored.LogoutAsync(Server);
        Assert.Equal(1, handler.Logouts); Assert.Empty(store.Load()); Assert.Equal(1, handler.Logins);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("2.0.0")]
    public async Task RejectedRefreshPolicyStillPersistsRotatedCredentials(string minimum)
    {
        var store = new SessionStore();
        using var handler = new VersionHandler(); using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, sessionStore: store, launcherVersion: "1.0.10");
        await api.LoginAsync(Server, "tester", "fixture-password");
        handler.ExpireAccess = true; handler.RefreshMinimum = minimum;
        if (minimum == "malformed") await Assert.ThrowsAsync<InvalidDataException>(() => api.RefreshAsync(Server));
        else await Assert.ThrowsAsync<LauncherUpgradeRequiredException>(() => api.RefreshAsync(Server));
        Assert.Equal("fixture-refresh-1", Assert.Single(store.Load()).RefreshToken);
        Assert.Equal(1, handler.Refreshes);
        handler.RefreshMinimum = "1.0.9";
        using var restored = new LauncherApiClient(http, sessionStore: store, launcherVersion: "1.0.10");
        Assert.Equal("tester", (await restored.RefreshAsync(Server))?.Username);
        Assert.Equal(2, handler.Refreshes);
    }

    [Fact]
    public async Task NullPluginIdentityIsRejectedAsInvalidData()
    {
        using var handler = new VersionHandler { MalformedRecommendation = true }; using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, launcherVersion: "1.0.10");
        await Assert.ThrowsAsync<InvalidDataException>(() => api.LoginAsync(Server, "tester", "fixture-password"));
        Assert.Null(api.Current(Server));
    }

    private sealed class SessionStore : IAccountSessionStore
    {
        private IReadOnlyList<PersistedAccountSession> _entries = [];
        public IReadOnlyList<PersistedAccountSession> Load() => _entries;
        public void Save(string serverId, string apiUrl, string? selected, IReadOnlyList<PersistedAccountSession> sessions) => _entries = sessions;
        public void Remove(string serverId) => _entries = [];
    }

    private sealed class VersionHandler : HttpMessageHandler
    {
        internal string CatalogMinimum = "1.0.9";
        internal string? BodyVersion;
        internal int Logins;
        internal bool RejectAccount;
        internal bool ExpireAccess, MalformedRecommendation;
        internal string RefreshMinimum = "1.0.9";
        internal int Refreshes, Logouts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("1.0.10", request.Headers.GetValues("X-FantasyTale-Launcher-Version").Single());
            var path = request.RequestUri!.AbsolutePath;
            object value;
            var profile = new AccountProfile("tester", "Tester", new Dictionary<string, long?>(), null);
            if (path == "/v1/catalog") value = new LauncherCatalog(1, "bubblefighter", "Test server", false, [], [])
            { MinimumLauncherVersion = CatalogMinimum, ClientPolicy = new(1, [], [new("test.required", [new("offer", "recommend", [], Sources: [])])]) };
            else if (path is "/v1/auth/login" or "/v1/auth/refresh")
            {
                if (path.EndsWith("login", StringComparison.Ordinal)) Logins++; else Refreshes++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                BodyVersion = body.RootElement.GetProperty("launcherVersion").GetString();
                if (Refreshes > 0) Assert.Equal(Refreshes == 1 ? "fixture-refresh" : "fixture-refresh-" + (Refreshes - 1), body.RootElement.GetProperty("refreshToken").GetString());
                value = new { accessToken = Refreshes == 0 ? "fixture-access" : "fixture-access-" + Refreshes, accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                    refreshToken = Refreshes == 0 ? "fixture-refresh" : "fixture-refresh-" + Refreshes, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30), account = profile,
                    minimumLauncherVersion = Refreshes == 0 ? "1.0.9" : RefreshMinimum, clientPolicy = new ClientPolicy(1, [],
                    [new(MalformedRecommendation ? null! : "test.required", [new("need", "require", ["kr-812"])]), new("test.optional", [new("offer", "recommend", ["kr-812"], Sources: [])])]) };
            }
            else if (path == "/v1/account" && ExpireAccess && request.Headers.Authorization?.Parameter == "fixture-access")
                return new(HttpStatusCode.Unauthorized);
            else if (path == "/v1/account" && RejectAccount)
                return new((HttpStatusCode)426) { Content = JsonContent.Create(new { error = "launcher_update_required", minimumLauncherVersion = "2.0.0" }) };
            else { if (path == "/v1/auth/logout") Logouts++; value = path == "/v1/account" ? profile : new { signedOut = true }; }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        }
    }
}
