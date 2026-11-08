using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class MultiAccountTests
{
    private static readonly ServerDefinition Server = new("one", "bubblefighter", "One", "https://one.example/", null, true, false);

    [Theory]
    [InlineData("switch")]
    [InlineData("switch-back")]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("remove")]
    [InlineData("endpoint")]
    [InlineData("other-server")]
    public async Task LaunchPreparationSnapshotRejectsChangedSelectionBeforeAnyTicketRequest(string change)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "bob", "password");
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var selection = fixture.Api.CaptureLaunchSelection(Server.Id);
        var destination = Server;
        switch (change)
        {
            case "switch": fixture.Api.Select(Server, "bob"); break;
            case "switch-back": fixture.Api.Select(Server, "bob"); fixture.Api.Select(Server, "alice"); break;
            case "login": await fixture.Api.LoginAsync(Server, "alice", "password"); break;
            case "logout": await fixture.Api.LogoutAsync(Server, "alice"); break;
            case "remove": fixture.Api.Forget(Server.Id); await fixture.Api.LoginAsync(Server, "alice", "password"); break;
            case "endpoint": destination = Server with { ApiUrl = "https://changed.example/" }; break;
            case "other-server": destination = Server with { Id = "other" }; break;
        }
        var requests = 0;
        fixture.Handler.Intercept = _ => { requests++; return Task.FromResult<HttpResponseMessage?>(null); };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Api.CredentialsAsync(destination,
            ClientVersionTests.Build(), new(null, null), new("en", "ko", "ko"), true, selection));
        Assert.Contains("selected account changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, requests);
        Assert.Equal("{}", JsonSerializer.Serialize(selection));
    }

    [Fact]
    public async Task InitiallySignedOutLaunchDoesNotAdoptALaterLogin()
    {
        using var fixture = new Fixture();
        var selection = fixture.Api.CaptureLaunchSelection(Server.Id);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var requests = 0;
        fixture.Handler.Intercept = _ => { requests++; return Task.FromResult<HttpResponseMessage?>(null); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(),
            new(null, null), new("en", "ko", "ko"), false, selection));
        Assert.Equal(0, requests);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
    }

    [Fact]
    public async Task LaunchSnapshotAllowsRenewalAndIgnoresUnrelatedAccountChanges()
    {
        var clock = new TestClock();
        using var fixture = new Fixture(clock);
        await fixture.Api.LoginAsync(Server, "bob", "password");
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var selection = fixture.Api.CaptureLaunchSelection(Server.Id);
        var other = Server with { Id = "other", ApiUrl = "https://other.example/" };
        await fixture.Api.LoginAsync(other, "carol", "password");
        await fixture.Api.LogoutAsync(Server, "bob");
        clock.Advance(TimeSpan.FromMinutes(29));
        var requests = new List<string>();
        fixture.Handler.Intercept = request => { requests.Add(request.RequestUri!.AbsolutePath); return Task.FromResult<HttpResponseMessage?>(null); };
        fixture.Api.RequireLaunchSelection(Server, selection);
        var credentials = await fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(null, null),
            new("en", "ko", "ko"), true, selection);
        Assert.Equal("alice", credentials!.Account);
        Assert.Equal(["/v1/auth/refresh", "/v1/launch"], requests);
        fixture.Api.RequireLaunchSelection(Server, selection);
        using var otherClient = new Fixture();
        Assert.Throws<InvalidOperationException>(() => otherClient.Api.RequireLaunchSelection(Server, selection));
    }

    [Fact]
    public async Task LanguagesTravelWithTheSameLaunchTicketRequest()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var requests = new List<string>();
        fixture.Handler.Intercept = async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath != "/v1/launch") return null;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("zh-CN", json.RootElement.GetProperty("launcher_language").GetString());
            Assert.Equal("ko", json.RootElement.GetProperty("native_language").GetString());
            Assert.Equal("en", json.RootElement.GetProperty("client_language").GetString());
            Assert.Equal("kr-1256", json.RootElement.GetProperty("build").GetString());
            Assert.Equal(1256, json.RootElement.GetProperty("protocol_version").GetInt32());
            Assert.Equal("1304", json.RootElement.GetProperty("patch_version").GetString());
            return Reply(new LaunchCredentials("alice", "ticket", DateTimeOffset.UtcNow.AddMinutes(1)));
        };
        await fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(null, "1304"), new("zh-CN", "ko", "en"), true);
        Assert.Equal(["/v1/launch"], requests);
    }

    [Theory]
    [InlineData("http://203.0.113.10:8091", "v1.claims.signature", true)]
    [InlineData("ftp://203.0.113.10/", "v1.claims.signature", false)]
    [InlineData("http://user@203.0.113.10/", "v1.claims.signature", false)]
    [InlineData("http://203.0.113.10/?next=1", "v1.claims.signature", false)]
    [InlineData("http://203.0.113.10/", "has space", false)]
    public async Task MessengerSignInTravelsWithTheLaunchTicketOnlyWhenWellFormed(string url, string ticket, bool accepted)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath != "/v1/launch" ? null
            : Reply(new { account = "alice", ticket = "ticket", expiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                messenger = new { url, ticket, expiresAt = DateTimeOffset.UtcNow.AddMinutes(1) } }));
        if (accepted)
        {
            var credentials = await fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(null, "1304"), true);
            Assert.Equal(url, credentials?.Messenger?.Url);
            Assert.Equal(ticket, credentials?.Messenger?.Ticket);
            return;
        }
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(null, "1304"), true));
        Assert.DoesNotContain(ticket, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ws://203.0.113.10:8092/companion/v1", "ticket-1", "launch-1", true)]
    [InlineData("wss://link.example.test/companion/v1", "ticket-1", "launch-1", true)]
    [InlineData("http://203.0.113.10:8092/companion/v1", "ticket-1", "launch-1", false)]
    [InlineData("ws://203.0.113.10:8092/companion/v1?t=1", "ticket-1", "launch-1", false)]
    [InlineData("ws://203.0.113.10:8092/companion/v1", "has space", "launch-1", false)]
    [InlineData("ws://203.0.113.10:8092/companion/v1", "ticket-1", "launch/1", false)]
    public async Task ANirinsouGrantTravelsWithTheLaunchOnlyWhenWellFormedAndNeverBlocksIt(string url, string ticket, string launchId, bool accepted)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath != "/v1/launch" ? null
            : Reply(new { account = "alice", ticket = "game-ticket", expiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                companion = new { url, ticket, launchId, expiresAt = DateTimeOffset.UtcNow.AddMinutes(2) } }));
        var credentials = await fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(null, "1304"), true);
        Assert.Equal("game-ticket", credentials?.Ticket);
        Assert.Equal(accepted ? launchId : null, credentials?.Companion?.LaunchId);
    }

    [Fact]
    public async Task NirinsouRenewalAsksForTheSameLaunchAndTreatsAnEndedLaunchAsGone()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        string? requested = null;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/companion/tickets") return null;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            requested = json.RootElement.GetProperty("launchId").GetString();
            return requested == "launch-1"
                ? Reply(new { url = "ws://203.0.113.10:8092/companion/v1", ticket = "renewed", launchId = "launch-1", expiresAt = DateTimeOffset.UtcNow.AddMinutes(2) })
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"launch_not_found\"}", System.Text.Encoding.UTF8, "application/json") };
        };
        Assert.Equal("renewed", (await fixture.Api.RenewCompanionAsync(Server, "launch-1"))?.Ticket);
        Assert.Equal("launch-1", requested);
        Assert.Null(await fixture.Api.RenewCompanionAsync(Server, "ended"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0.2.61.001", "1304")]
    [InlineData("", "458")]
    public async Task LaunchUsesWireIdentityAndSendsOnlyKnownDescriptiveVersions(string? release, string? patch)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var launches = 0;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/launch") return null;
            launches++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("kr-1256", json.RootElement.GetProperty("build").GetString());
            Assert.Equal(!string.IsNullOrEmpty(release), json.RootElement.TryGetProperty("release_version", out var releaseValue));
            Assert.Equal(patch is not null, json.RootElement.TryGetProperty("patch_version", out var patchValue));
            if (!string.IsNullOrEmpty(release)) Assert.Equal(release, releaseValue.GetString());
            if (patch is not null) Assert.Equal(patch, patchValue.GetString());
            return Reply(new LaunchCredentials("alice", "ticket", DateTimeOffset.UtcNow.AddMinutes(1)));
        };
        await fixture.Api.CredentialsAsync(Server, ClientVersionTests.Build(), new(release, patch), true);
        Assert.Equal(1, launches);
    }

    [Theory]
    [InlineData(-600)]
    [InlineData(600)]
    [InlineData(1200)]
    public async Task FreshLaunchTicketsDoNotDependOnThePlayersClock(int expirySeconds)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/v1/launch"
            ? Reply(new LaunchCredentials("alice", "server-issued-ticket", DateTimeOffset.UtcNow.AddSeconds(expirySeconds))) : null);
        var ticket = await fixture.Api.CredentialsAsync(Server, "cn-406", true);
        Assert.Equal("server-issued-ticket", ticket!.Ticket);
    }

    [Theory]
    [InlineData("bob", "ticket")]
    [InlineData("alice", "")]
    [InlineData("alice", "ticket\n")]
    public async Task FreshLaunchTicketsStillRequireMatchingIdentityAndValidContents(string account, string token)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/v1/launch"
            ? Reply(new LaunchCredentials(account, token, DateTimeOffset.UtcNow.AddMinutes(10))) : null);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.CredentialsAsync(Server, "cn-406", true));
    }

    [Fact]
    public async Task SwitchingAndLogoutKeepOtherAccountsAndServersIsolated()
    {
        using var fixture = new Fixture(); var api = fixture.Api;
        await api.LoginAsync(Server, "alice", "password");
        await api.LoginAsync(Server, "bob", "password");
        var other = Server with { Id = "two", ApiUrl = "https://two.example/" };
        await api.LoginAsync(other, "alice", "password");
        Assert.Equal(2, api.Accounts(Server).Accounts.Count);
        Assert.Equal("bob", api.Current(Server)!.Username);
        Assert.Equal("alice", api.Select(Server, "alice").Current!.Username);
        Assert.Equal("alice", (await api.CredentialsAsync(Server, "cn-406", true))!.Account);
        await api.LogoutAsync(Server, "bob");
        Assert.Equal("alice", api.Current(Server)!.Username);
        await api.LogoutAsync(Server);
        Assert.Null(api.Current(Server)); Assert.Empty(api.Accounts(Server).Accounts);
        Assert.Equal("alice", api.Current(other)!.Username);
        Assert.Throws<InvalidOperationException>(() => api.Select(Server, "bob"));
        var json = JsonSerializer.Serialize(api.Accounts(other));
        Assert.DoesNotContain("Bearer", json); Assert.DoesNotContain("session-", json);
    }

    [Fact]
    public async Task FailedNewLoginDoesNotDiscardSignedInAccounts()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("login", StringComparison.Ordinal)
            ? new(HttpStatusCode.Unauthorized) : null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Api.LoginAsync(Server, "bob", "wrong"));
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
        Assert.Single(fixture.Api.Accounts(Server).Accounts);
    }

    [Fact]
    public async Task StaleUnauthorizedResponseCannotSignOutNewLoginOrDifferentSelection()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/account") return null;
            started.SetResult(); await release.Task; return new(HttpStatusCode.Unauthorized);
        };
        var oldRefresh = fixture.Api.RefreshAsync(Server); await started.Task;
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await fixture.Api.LoginAsync(Server, "bob", "password");
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => oldRefresh);
        Assert.Equal("bob", fixture.Api.Current(Server)!.Username);
        Assert.Equal(2, fixture.Api.Accounts(Server).Accounts.Count);
        Assert.Equal("alice", fixture.Api.Select(Server, "alice").Current!.Username);
    }

    [Fact]
    public async Task RevocationRemovesOnlyTheRejectedAccount()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "bob", "password");
        await fixture.Api.LoginAsync(Server, "alice", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath is "/v1/account" or "/v1/auth/refresh" ? new(HttpStatusCode.Unauthorized) : null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Api.RefreshAsync(Server));
        Assert.Null(fixture.Api.Current(Server));
        Assert.Equal("bob", Assert.Single(fixture.Api.Accounts(Server).Accounts).Account.Username);
    }

    [Fact]
    public async Task ServerRemovalFencesAPendingLogin()
    {
        using var fixture = new Fixture(); var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/login") return null;
            started.SetResult(); await release.Task; return null;
        };
        var login = fixture.Api.LoginAsync(Server, "alice", "password"); await started.Task;
        fixture.Api.Forget(Server.Id); release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => login);
        Assert.Empty(fixture.Api.Accounts(Server).Accounts);
    }

    [Fact]
    public async Task TicketCannotCrossAnAccountSwitchEvenWhenSwitchedBack()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await fixture.Api.LoginAsync(Server, "bob", "password");
        fixture.Api.Select(Server, "alice");
        var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/launch") return null;
            started.SetResult(); await release.Task; return null;
        };
        var ticket = fixture.Api.CredentialsAsync(Server, "cn-406", true); await started.Task;
        fixture.Api.Select(Server, "bob"); fixture.Api.Select(Server, "alice"); release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ticket);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
    }

    [Fact]
    public async Task LateProfileCannotReplaceTheCurrentAccount()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await fixture.Api.LoginAsync(Server, "bob", "password");
        fixture.Api.Select(Server, "alice");
        var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/account") return null;
            started.SetResult(); await release.Task; return Reply(Profile("alice") with { DisplayName = "Updated Alice" });
        };
        var refresh = fixture.Api.RefreshAsync(Server); await started.Task;
        fixture.Api.Select(Server, "bob"); release.SetResult();
        Assert.Equal("bob", (await refresh)!.Username);
        Assert.Equal("Updated Alice", fixture.Api.Select(Server, "alice").Current!.DisplayName);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("logout")]
    [InlineData("reauthentication")]
    [InlineData("removal")]
    [InlineData("endpoint")]
    public async Task InFlightTicketCannotOutliveItsExactAccountSession(string change)
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        var other = Server with { Id = "other" };
        await fixture.Api.LoginAsync(other, "bob", "password");
        var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/launch") return null;
            started.TrySetResult(); await release.Task;
            return Reply(new LaunchCredentials("alice", "stale-ticket", clock.GetUtcNow().AddMinutes(1)));
        };
        var pending = fixture.Api.CredentialsAsync(Server, "cn-406", true);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (change)
            {
                case "expiry": clock.Advance(TimeSpan.FromDays(8)); break;
                case "logout": await fixture.Api.LogoutAsync(Server); break;
                case "reauthentication": await fixture.Api.LoginAsync(Server, "alice", "password"); break;
                case "removal": fixture.Api.Forget(Server.Id); break;
                case "endpoint": Assert.Null(fixture.Api.Current(Server with { ApiUrl = "https://replacement.example/" })); break;
                default: throw new InvalidOperationException("Unknown session change.");
            }
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        if (change != "expiry") Assert.Equal("bob", fixture.Api.Current(other)!.Username);
        if (change == "reauthentication") Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
        else Assert.Null(fixture.Api.Current(Server));
    }

    [Fact]
    public async Task AccountsAndServerPoliciesRemainAvailableBeyondFormerCapacityLimits()
    {
        var store = new MemorySessionStore();
        using var fixture = new Fixture(store: store);
        for (var i = 0; i < 145; i++) await fixture.Api.LoginAsync(Server, "user" + i, "password");
        await fixture.Api.LoginAsync(Server, "user0", "password");
        Assert.Equal(145, fixture.Api.Accounts(Server).Accounts.Count);
        Assert.Equal("user0", fixture.Api.Current(Server)!.Username);
        for (var i = 0; i < 145; i++)
            await fixture.Api.LoginAsync(Server with { Id = "server-" + i, ApiUrl = "https://server-" + i + ".example/" }, "alice", "password");
        using var reopened = new Fixture(store: store);
        Assert.Equal(145, reopened.Api.Accounts(Server).Accounts.Count);
        Assert.Equal("user0", reopened.Api.Current(Server)!.Username);
        for (var i = 0; i < 145; i++)
            Assert.Equal("alice", reopened.Api.Current(Server with { Id = "server-" + i, ApiUrl = "https://server-" + i + ".example/" })!.Username);
    }

    [Fact]
    public async Task CatalogAndRegistrationAcceptAllDeclaredRowsAndChoices()
    {
        using var fixture = new Fixture();
        var fields = Enumerable.Range(0, 130).Select(i => new RegistrationField("field" + i,
            new Dictionary<string, string> { ["en"] = "Field " + i }, "select", false, 2048,
            Enumerable.Range(0, 50).Select(n => "choice" + n).ToArray())).ToArray();
        var presets = Enumerable.Range(0, 130).Select(i => new ServerInstance("preset" + i, "kr-1256",
            new Dictionary<string, string> { ["en"] = "Client " + i }, null, null)
            { ProtocolVersion = 1256, PatchVersion = "1304" }).ToArray();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/catalog")
                return Reply(new LauncherCatalog(1, Server.Game, "Fixture", true, fields, presets));
            if (request.RequestUri.AbsolutePath == "/v1/auth/register")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(130, body.RootElement.GetProperty("fields").EnumerateObject().Count());
                return Reply(new { created = true });
            }
            return null;
        };
        var catalog = await fixture.Api.CatalogAsync(Server);
        Assert.Equal(130, catalog.Instances.Count);
        Assert.All(catalog.Instances, preset => { Assert.Equal(1256, preset.ProtocolVersion); Assert.Equal("1304", preset.PatchVersion); });
        Assert.Equal(130, catalog.RegistrationFields.Count);
        await fixture.Api.RegisterAsync(Server, "alice", "password", fields.ToDictionary(f => f.Key, _ => "choice49"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaunchCannotChangeAccountsWhileItsTokenIsBeingRenewed(bool switchBack)
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await fixture.Api.LoginAsync(Server, "bob", "password");
        fixture.Api.Select(Server, "alice");
        clock.Advance(TimeSpan.FromMinutes(29));
        var started = Signal(); var release = Signal(); var launches = 0;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/auth/refresh")
            {
                started.TrySetResult(); await release.Task;
                return Reply(new { accessToken = "rotated-access", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                    refreshToken = "rotated-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice") });
            }
            if (request.RequestUri.AbsolutePath == "/v1/launch") Interlocked.Increment(ref launches);
            return null;
        };
        var launch = fixture.Api.CredentialsAsync(Server, "cn-406", true);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Api.Select(Server, "bob");
            if (switchBack) fixture.Api.Select(Server, "alice");
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => launch);
        Assert.Equal(0, launches);
        Assert.Equal(switchBack ? "alice" : "bob", fixture.Api.Current(Server)!.Username);
    }

    [Fact]
    public async Task ExpiredSessionsArePrunedUsingTheInjectedClock()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        Assert.Single(fixture.Api.Accounts(Server).Accounts);
        clock.Advance(TimeSpan.FromDays(8));
        Assert.Empty(fixture.Api.Accounts(Server).Accounts);
        Assert.Null(fixture.Api.Current(Server));
    }

    [Fact]
    public async Task AccessTokenRenewsThroughRotatingRefreshTokenBeforeExpiry()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock); var refreshCalls = 0;
        fixture.Handler.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/auth/login")
                return Task.FromResult<HttpResponseMessage?>(Reply(new
                {
                    accessToken = "access-one", accessExpiresAt = clock.GetUtcNow().AddMinutes(1),
                    refreshToken = "refresh-one", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice")
                }));
            if (request.RequestUri.AbsolutePath == "/v1/auth/refresh")
            {
                Interlocked.Increment(ref refreshCalls);
                return Task.FromResult<HttpResponseMessage?>(Reply(new
                {
                    accessToken = "access-two", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                    refreshToken = "refresh-two", refreshExpiresAt = clock.GetUtcNow().AddDays(7)
                }));
            }
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await Task.WhenAll(fixture.Api.RefreshIfNeededAsync(Server), fixture.Api.RefreshIfNeededAsync(Server));
        Assert.Equal(1, refreshCalls);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
        Assert.Single(fixture.Api.Accounts(Server).Accounts);
    }

    [Fact]
    public async Task ProtectedSessionStateRestoresAfterLauncherRestartAndRefreshesWithoutLogin()
    {
        var clock = new TestClock();
        var store = new MemorySessionStore();
        using (var first = new Fixture(clock, store))
        {
            await first.Api.LoginAsync(Server, "alice", "password");
            Assert.Single(store.Load());
        }

        var handler = new Handler();
        handler.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/refresh") return Task.FromResult<HttpResponseMessage?>(null);
            handler.Remember("access-after-restart", "alice");
            return Task.FromResult<HttpResponseMessage?>(Reply(new
            {
                accessToken = "access-after-restart",
                accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                refreshToken = "refresh-after-restart",
                refreshExpiresAt = clock.GetUtcNow().AddDays(7)
            }));
        };
        using var second = new Fixture(clock, store, handler);
        Assert.Equal("alice", second.Api.Current(Server)!.Username);
        Assert.Equal("alice", (await second.Api.RefreshIfNeededAsync(Server))!.Username);
        Assert.Equal("alice", (await second.Api.CredentialsAsync(Server, "cn-406", true))!.Account);
        Assert.Equal("refresh-after-restart", Assert.Single(store.Load()).RefreshToken);
    }

    [Fact]
    public async Task RejectedRefreshTokenRemovesOnlyThatAccount()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        fixture.Handler.Intercept = request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult<HttpResponseMessage?>(Reply(new
            {
                accessToken = "access-one", accessExpiresAt = clock.GetUtcNow().AddMinutes(1),
                refreshToken = "refresh-one", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice")
            })),
            "/v1/auth/refresh" => Task.FromResult<HttpResponseMessage?>(new(HttpStatusCode.Unauthorized)),
            _ => Task.FromResult<HttpResponseMessage?>(null),
        };
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Api.RefreshIfNeededAsync(Server));
        Assert.Empty(fixture.Api.Accounts(Server).Accounts);
    }

    [Fact]
    public async Task UnauthorizedAccessRetriesOnceAfterRefresh()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock); var accountCalls = 0; var refreshCalls = 0;
        fixture.Handler.Intercept = request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/auth/login" => Task.FromResult<HttpResponseMessage?>(Reply(new
            {
                accessToken = "access-one", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                refreshToken = "refresh-one", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice")
            })),
            "/v1/account" when Interlocked.Increment(ref accountCalls) == 1 => Task.FromResult<HttpResponseMessage?>(new(HttpStatusCode.Unauthorized)),
            "/v1/auth/refresh" when Interlocked.Increment(ref refreshCalls) == 1 => Task.FromResult<HttpResponseMessage?>(Reply(new
            {
                accessToken = "access-two", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                refreshToken = "refresh-two", refreshExpiresAt = clock.GetUtcNow().AddDays(7)
            })),
            "/v1/account" when request.Headers.Authorization?.Parameter == "access-two" => Task.FromResult<HttpResponseMessage?>(Reply(Profile("alice"))),
            "/v1/account" => Task.FromResult<HttpResponseMessage?>(new(HttpStatusCode.Unauthorized)),
            _ => Task.FromResult<HttpResponseMessage?>(null),
        };
        await fixture.Api.LoginAsync(Server, "alice", "password");
        Assert.Equal("alice", (await fixture.Api.RefreshAsync(Server))!.Username);
        Assert.Equal(2, accountCalls);
        Assert.Equal(1, refreshCalls);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
    }

    [Fact]
    public async Task RestoredSelectionsRemainServerScopedAndLogoutDoesNotReturnAfterRestart()
    {
        var store = new MemorySessionStore();
        var noSlash = Server with { ApiUrl = "https://one.example" };
        var other = Server with { Id = "two", ApiUrl = "https://two.example/" };
        using (var first = new Fixture(store: store))
        {
            await first.Api.LoginAsync(noSlash, "alice", "password");
            await first.Api.LoginAsync(noSlash, "bob", "password");
            await first.Api.LoginAsync(other, "charlie", "password");
            first.Api.Select(noSlash, "alice");
        }
        using (var second = new Fixture(store: store))
        {
            Assert.Equal("alice", second.Api.Current(noSlash)!.Username);
            Assert.Equal("charlie", second.Api.Current(other)!.Username);
            await second.Api.LogoutAsync(noSlash, "bob");
            second.Api.Forget(other.Id);
        }
        using var third = new Fixture(store: store);
        Assert.Equal("alice", Assert.Single(third.Api.Accounts(noSlash).Accounts).Account.Username);
        Assert.Null(third.Api.Current(other));
    }

    [Fact]
    public async Task TemporaryRefreshFailureRetainsProtectedSessionForRetryAfterRestart()
    {
        var store = new MemorySessionStore();
        using (var first = new Fixture(store: store)) await first.Api.LoginAsync(Server, "alice", "password");
        using (var offline = new Fixture(store: store))
        {
            offline.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/v1/auth/refresh" ? new(HttpStatusCode.ServiceUnavailable) : null);
            await Assert.ThrowsAsync<ServerFailureException>(() => offline.Api.RefreshIfNeededAsync(Server));
            Assert.Single(store.Load());
            Assert.Equal("alice", offline.Api.Current(Server)!.Username);
        }
        using var restarted = new Fixture(store: store);
        restarted.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/v1/auth/refresh"
            ? Reply(new { accessToken = "recovered", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "rotated", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = Profile("alice") }) : null);
        Assert.Equal("alice", (await restarted.Api.RefreshIfNeededAsync(Server))!.Username);
        Assert.Equal("rotated", Assert.Single(store.Load()).RefreshToken);
    }

    [Fact]
    public async Task RejectedRestoredRefreshIsRemovedFromPersistentStorage()
    {
        var store = new MemorySessionStore();
        using (var first = new Fixture(store: store)) await first.Api.LoginAsync(Server, "alice", "password");
        using (var restored = new Fixture(store: store))
        {
            restored.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(
                request.RequestUri!.AbsolutePath == "/v1/auth/refresh" ? new(HttpStatusCode.Unauthorized) : null);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restored.Api.RefreshIfNeededAsync(Server));
        }
        using var final = new Fixture(store: store);
        Assert.Empty(final.Api.Accounts(Server).Accounts);
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task DelayedRenewalCannotPersistAnAccountAfterServerRemoval()
    {
        var store = new MemorySessionStore();
        using (var first = new Fixture(store: store)) await first.Api.LoginAsync(Server, "alice", "password");
        using var restored = new Fixture(store: store);
        var started = Signal(); var release = Signal();
        restored.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/refresh") return null;
            started.TrySetResult(); await release.Task;
            return Reply(new { accessToken = "late", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "late-refresh", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7) });
        };
        var pending = restored.Api.RefreshIfNeededAsync(Server);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        restored.Api.Forget(Server.Id);
        release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task FailedRemoteLogoutStillRemovesOnlyItsLocalAccount()
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password"); await fixture.Api.LoginAsync(Server, "bob", "password");
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath.EndsWith("logout", StringComparison.Ordinal) ? new(HttpStatusCode.ServiceUnavailable) : null);
        await Assert.ThrowsAsync<ServerFailureException>(() => fixture.Api.LogoutAsync(Server, "alice"));
        Assert.Equal("bob", fixture.Api.Current(Server)!.Username);
        Assert.Single(fixture.Api.Accounts(Server).Accounts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutWaitsForRotationAndRevokesTheLatestSessionWithoutRemovingAnotherAccount(bool switchAccount)
    {
        var clock = new TestClock(); var store = new MemorySessionStore();
        using var fixture = new Fixture(clock, store);
        await fixture.Api.LoginAsync(Server, "bob", "password");
        await fixture.Api.LoginAsync(Server, "alice", "password");
        clock.Advance(TimeSpan.FromMinutes(29));
        var started = Signal(); var release = Signal(); var rotated = false; var logouts = 0;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/auth/refresh")
            {
                started.TrySetResult(); await release.Task;
                rotated = true;
                return Reply(new { accessToken = "rotated-access", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                    refreshToken = "rotated-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice") });
            }
            if (request.RequestUri.AbsolutePath == "/v1/auth/logout")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.True(rotated);
                Assert.Equal("rotated-refresh", body.RootElement.GetProperty("refreshToken").GetString());
                Assert.Equal("rotated-access", request.Headers.Authorization!.Parameter);
                Interlocked.Increment(ref logouts);
                return Reply(new { signedOut = true });
            }
            return null;
        };
        var refresh = fixture.Api.RefreshIfNeededAsync(Server);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var logout = fixture.Api.LogoutAsync(Server);
        try { if (switchAccount) fixture.Api.Select(Server, "bob"); }
        finally { release.TrySetResult(); }
        await Task.WhenAll(refresh, logout).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, logouts);
        Assert.Equal("bob", Assert.Single(fixture.Api.Accounts(Server).Accounts).Account.Username);
        Assert.Equal(switchAccount ? "bob" : null, fixture.Api.Current(Server)?.Username);
        using var reopened = new Fixture(clock, store);
        Assert.Equal("bob", Assert.Single(reopened.Api.Accounts(Server).Accounts).Account.Username);
    }

    [Fact]
    public async Task CancelingLogoutWhileRotationIsPendingLeavesTheAccountAvailable()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        clock.Advance(TimeSpan.FromMinutes(29));
        var started = Signal(); var release = Signal(); var logouts = 0;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/auth/refresh")
            {
                started.TrySetResult(); await release.Task;
                return Reply(new { accessToken = "rotated-access", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                    refreshToken = "rotated-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice") });
            }
            if (request.RequestUri.AbsolutePath == "/v1/auth/logout") Interlocked.Increment(ref logouts);
            return null;
        };
        var refresh = fixture.Api.RefreshIfNeededAsync(Server);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        var logout = fixture.Api.LogoutAsync(Server, ct: stop.Token);
        stop.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => logout); }
        finally { release.TrySetResult(); }
        Assert.Equal("alice", (await refresh.WaitAsync(TimeSpan.FromSeconds(5)))!.Username);
        Assert.Equal(0, logouts);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
    }

    [Fact]
    public async Task WaitingLogoutCannotRemoveANewerLoginForTheSameUsername()
    {
        var clock = new TestClock(); var store = new MemorySessionStore();
        using var fixture = new Fixture(clock, store);
        await fixture.Api.LoginAsync(Server, "alice", "password");
        clock.Advance(TimeSpan.FromMinutes(29));
        var started = Signal(); var release = Signal(); var logouts = 0;
        fixture.Handler.Intercept = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/auth/refresh") { started.TrySetResult(); await release.Task; }
            if (path is "/v1/auth/refresh" or "/v1/auth/login")
                return Reply(new { accessToken = "new-access", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                    refreshToken = "new-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice") });
            if (path == "/v1/auth/logout") Interlocked.Increment(ref logouts);
            return null;
        };
        var refresh = fixture.Api.RefreshIfNeededAsync(Server);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var logout = fixture.Api.LogoutAsync(Server);
        try { await fixture.Api.LoginAsync(Server, "alice", "password"); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh);
        await logout.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, logouts);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
        Assert.Equal("new-refresh", Assert.Single(store.Load()).RefreshToken);
    }

    [Fact]
    public async Task ExpiredAccountsOnOtherServersReleaseTheGlobalCapacity()
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        for (var serverIndex = 0; serverIndex < 8; serverIndex++)
        {
            var server = Server with { Id = "server" + serverIndex };
            for (var account = 0; account < 16; account++)
                await fixture.Api.LoginAsync(server, "user" + account, "password");
        }
        clock.Advance(TimeSpan.FromDays(8));
        fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/v1/auth/login"
            ? Reply(new { accessToken = "fresh-session", accessExpiresAt = clock.GetUtcNow().AddMinutes(30), refreshToken = "fresh-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("fresh") }) : null);
        var target = Server with { Id = "new-server" };
        Assert.Equal("fresh", (await fixture.Api.LoginAsync(target, "fresh", "password")).Username);
        Assert.Equal("fresh", fixture.Api.Current(target)!.Username);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyServerGroupsReleaseCapacityWithoutEvictingPendingLogins(bool expired)
    {
        var clock = new TestClock(); using var fixture = new Fixture(clock);
        if (!expired) fixture.Handler.Intercept = request => Task.FromResult<HttpResponseMessage?>(
            request.RequestUri!.AbsolutePath == "/v1/auth/login" ? new(HttpStatusCode.Unauthorized) : null);
        for (var i = 0; i < 128; i++)
        {
            var server = Server with { Id = "server-" + i };
            if (expired) await fixture.Api.LoginAsync(server, "alice", "password");
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Api.LoginAsync(server, "alice", "password"));
        }
        if (expired) clock.Advance(TimeSpan.FromDays(8));
        var started = Signal(); var release = Signal();
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/login") return null;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var username = body.RootElement.GetProperty("username").GetString()!;
            if (username == "pending") { started.TrySetResult(); await release.Task; }
            return Reply(new { accessToken = "fresh-" + username, accessExpiresAt = clock.GetUtcNow().AddMinutes(30), refreshToken = "fresh-refresh-" + username, refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile(username) });
        };
        var pendingServer = Server with { Id = "pending-server" };
        var pending = fixture.Api.LoginAsync(pendingServer, "pending", "password");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Api.LoginAsync(Server, "fresh", "password");
        }
        finally { release.TrySetResult(); }
        Assert.Equal("pending", (await pending).Username);
        Assert.Equal("pending", fixture.Api.Current(pendingServer)!.Username);
        Assert.Equal("fresh", fixture.Api.Current(Server)!.Username);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task InactiveAndRestoredAccountsExposeTheirRenewableDeadlineWithoutCredentials()
    {
        var clock = new TestClock();
        var store = new MemorySessionStore();
        using (var first = new Fixture(clock, store))
        {
            await first.Api.LoginAsync(Server, "alice", "password");
            await first.Api.LoginAsync(Server, "bob", "password");
            clock.Advance(TimeSpan.FromHours(1));
            var accounts = first.Api.Accounts(Server);
            Assert.Equal(2, accounts.Accounts.Count);
            Assert.All(accounts.Accounts, entry =>
            {
                Assert.True(entry.ExpiresAt < clock.GetUtcNow());
                Assert.True(entry.RefreshExpiresAt > clock.GetUtcNow());
            });
        }
        var refreshed = new List<string>();
        var handler = new Handler();
        handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/refresh") return null;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var token = body.RootElement.GetProperty("refreshToken").GetString();
            Assert.Equal(store.Load().Single(entry => entry.Username == "alice").RefreshToken, token);
            refreshed.Add("alice");
            return Reply(new
            {
                accessToken = "renewed-alice-access", accessExpiresAt = clock.GetUtcNow().AddMinutes(30),
                refreshToken = "renewed-alice-refresh", refreshExpiresAt = clock.GetUtcNow().AddDays(7), account = Profile("alice")
            });
        };
        using var reopened = new Fixture(clock, store, handler);
        var restored = reopened.Api.Accounts(Server);
        Assert.Equal("bob", restored.Current!.Username);
        Assert.Equal(2, restored.Accounts.Count);
        Assert.Empty(refreshed);
        foreach (var entry in restored.Accounts)
        {
            Assert.Equal(clock.GetUtcNow(), entry.ExpiresAt);
            Assert.Equal(store.Load().Single(saved => saved.Username == entry.Account.Username).RefreshExpiresAt, entry.RefreshExpiresAt);
        }
        var serialized = JsonSerializer.Serialize(restored, JsonSerializerOptions.Web);
        Assert.Contains("refreshExpiresAt", serialized);
        foreach (var saved in store.Load()) Assert.DoesNotContain(saved.RefreshToken, serialized);

        reopened.Api.Select(Server, "alice");
        Assert.Equal("alice", (await reopened.Api.RefreshIfNeededAsync(Server))!.Username);
        Assert.Equal(["alice"], refreshed);
        Assert.Equal(2, reopened.Api.Accounts(Server).Accounts.Count);
        await reopened.Api.LogoutAsync(Server, "alice");
        var remaining = Assert.Single(reopened.Api.Accounts(Server).Accounts);
        Assert.Equal("bob", remaining.Account.Username);
        Assert.True(remaining.RefreshExpiresAt > clock.GetUtcNow());
        Assert.Equal("bob", Assert.Single(store.Load()).Username);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedLoginCannotReplaceANewerSelectionOrSameAccountSession(bool sameAccount)
    {
        using var fixture = new Fixture();
        await fixture.Api.LoginAsync(Server, "alice", "password");
        await fixture.Api.LoginAsync(Server, "bob", "password");
        var started = Signal(); var release = Signal();
        var held = 0;
        fixture.Handler.Intercept = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/auth/login" || Interlocked.Increment(ref held) != 1) return null;
            started.SetResult(); await release.Task;
            return Reply(new { accessToken = "delayed", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "delayed-refresh", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = Profile(sameAccount ? "alice" : "charlie") });
        };
        var pending = fixture.Api.LoginAsync(Server, sameAccount ? "alice" : "charlie", "password");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (sameAccount) await fixture.Api.LoginAsync(Server, "alice", "password");
            else fixture.Api.Select(Server, "alice");
        }
        finally { release.TrySetResult(); }
        if (sameAccount) await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        else Assert.Equal("charlie", (await pending).Username);
        Assert.Equal("alice", fixture.Api.Current(Server)!.Username);
        Assert.Equal(sameAccount ? 2 : 3, fixture.Api.Accounts(Server).Accounts.Count);
        Assert.Equal("alice", (await fixture.Api.CredentialsAsync(Server, "cn-406", true))!.Account);
    }
    private static AccountProfile Profile(string name) => new(name, name, new Dictionary<string, long?> { ["level"] = 12, ["lucci"] = 100 }, null);
    private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    private sealed class Fixture : IDisposable
    {
        internal Handler Handler { get; private set; } = new();
        private readonly HttpClient _http;
        internal LauncherApiClient Api { get; }
        internal Fixture(TimeProvider? clock = null, IAccountSessionStore? store = null, Handler? handler = null)
        {
            if (handler is not null) Handler = handler;
            _http = new HttpClient(Handler); Api = new LauncherApiClient(_http, clock: clock, sessionStore: store);
        }
        public void Dispose() { Api.Dispose(); _http.Dispose(); }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, Task<HttpResponseMessage?>>? Intercept { get; set; }
        private readonly Dictionary<string, string> _identities = new(StringComparer.Ordinal);
        private int _counter;
        internal void Remember(string token, string username) => _identities[token] = username;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Intercept is { } intercept && await intercept(request) is { } response) return response;
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/catalog") return Reply(new LauncherCatalog(1, "bubblefighter", "Test", true, [], []) { HostPolicy = "domain" });
            if (path == "/v1/auth/login")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var name = json.RootElement.GetProperty("username").GetString()!;
                var token = "session-" + Interlocked.Increment(ref _counter);
                _identities[token] = name;
                return Reply(new { accessToken = token, accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "refresh-" + token, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = Profile(name) });
            }
            if (path == "/v1/auth/logout") return Reply(new { });
            if (path == "/v1/auth/refresh")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var oldToken = json.RootElement.GetProperty("refreshToken").GetString()!["refresh-".Length..];
                var name = _identities[oldToken];
                var token = "session-" + Interlocked.Increment(ref _counter);
                _identities[token] = name;
                return Reply(new { accessToken = token, accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30), refreshToken = "refresh-" + token, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = Profile(name) });
            }
            var username = _identities[request.Headers.Authorization!.Parameter!];
            return path switch
            {
                "/v1/account" => Reply(Profile(username)),
                "/v1/launch" => Reply(new LaunchCredentials(username, "ticket", DateTimeOffset.UtcNow.AddMinutes(1))),
                "/v1/auth/logout" => Reply(new { }),
                _ => throw new InvalidOperationException("Unexpected test route."),
            };
        }
    }

    private sealed class MemorySessionStore : IAccountSessionStore
    {
        private readonly List<PersistedAccountSession> _sessions = [];
        public IReadOnlyList<PersistedAccountSession> Load() => _sessions.ToArray();
        public void Save(string serverId, string apiUrl, string? selected, IReadOnlyList<PersistedAccountSession> sessions)
        {
            _sessions.RemoveAll(session => session.ServerId == serverId);
            _sessions.AddRange(sessions);
        }
        public void Remove(string serverId) => _sessions.RemoveAll(session => session.ServerId == serverId);
    }
}
