using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AutomaticSelectionApiTests
{
    private static readonly ServerDefinition Server = new("issuer", "bubblefighter", "Issuer", AutomaticServerSelectionTests.ApiUrl, null, true, false);

    [Fact]
    public async Task OptInLaunchAndRefreshUseSameAccountTicketGrantAndVersionHeader()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.LoginAsync(Server, "alice", "fixture-password");
        var account = api.CaptureLaunchSelection(Server.Id);
        var build = ClientVersionTests.Build() with { Id = "kr-812", Region = "kr", ProtocolVersion = 812 };
        var credentials = (await api.SelectionCredentialsAsync(Server, build, new(null, null), new("en", "ko", "ko"), account))!;
        Assert.True(handler.SelectionRequested);
        Assert.NotNull(credentials.ServerSelection!.Servers[0].Targets[0].ObservedAtUtc);
        var refreshed = await api.RefreshServerSelectionAsync(Server, account, credentials, AutomaticServerSelectionTests.ApiUrl);
        Assert.Equal(credentials.ServerSelection.GrantId, refreshed!.GrantId);
        Assert.Equal(1, handler.Launches);
        Assert.Equal(1, handler.Refreshes);
    }

    [Fact]
    public async Task ClosedAdmissionReturnsNormallyWithoutRequestingAnotherLaunch()
    {
        using var handler = new Handler { Closed = true }; using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.LoginAsync(Server, "alice", "fixture-password");
        var account = api.CaptureLaunchSelection(Server.Id);
        var credentials = new LaunchCredentials("alice", "launch-ticket", handler.Grant.ExpiresAtUtc) { ServerSelection = handler.Grant };
        Assert.Null(await api.RefreshServerSelectionAsync(Server, account, credentials, AutomaticServerSelectionTests.ApiUrl));
        Assert.Equal(1, handler.Refreshes);
        Assert.Equal(0, handler.Launches);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("origin")]
    public async Task ChangedAccountOrOriginCannotReceiveTheOriginalTicket(string fault)
    {
        using var handler = new Handler(); using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.LoginAsync(Server, "alice", "fixture-password");
        var account = api.CaptureLaunchSelection(Server.Id);
        var credentials = new LaunchCredentials("alice", "launch-ticket", handler.Grant.ExpiresAtUtc) { ServerSelection = handler.Grant };
        if (fault == "account") api.Forget(Server.Id);
        var destination = fault == "origin" ? Server with { ApiUrl = "http://other.example:8090/" } : Server;
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.RefreshServerSelectionAsync(destination, account, credentials, AutomaticServerSelectionTests.ApiUrl));
        Assert.Equal(0, handler.Refreshes);
        Assert.Equal(0, handler.Launches);
    }

    [Fact]
    public async Task OrdinaryLaunchDoesNotEnableAutomaticSelection()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.LoginAsync(Server, "alice", "fixture-password");
        await api.CredentialsAsync(Server, "kr-812", true);
        Assert.False(handler.SelectionRequested);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal LaunchServerSelection Grant { get; } = AutomaticServerSelectionTests.Grant(DateTimeOffset.UtcNow);
        internal bool Closed { get; init; }
        internal bool SelectionRequested { get; private set; }
        internal int Launches { get; private set; }
        internal int Refreshes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.Headers.Contains("X-FantasyTale-Launcher-Version"));
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/v1/catalog": return Reply(new LauncherCatalog(1, "bubblefighter", "Issuer", true, [], []) { HostPolicy = "domain" });
                case "/v1/auth/login": return Reply(new { accessToken = "session", accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "refresh-session", refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    account = new AccountProfile("alice", "Alice", new Dictionary<string, long?>(), null) });
                case "/v1/launch":
                {
                    Assert.Equal("session", request.Headers.Authorization!.Parameter);
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    SelectionRequested = body.RootElement.TryGetProperty("serverSelection", out var value) && value.GetBoolean();
                    Launches++;
                    return Reply(new LaunchCredentials("alice", "launch-ticket", Grant.ExpiresAtUtc) { ServerSelection = Grant });
                }
                case "/v1/launch/servers":
                {
                    Assert.Equal("session", request.Headers.Authorization!.Parameter);
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    Assert.Equal("launch-ticket", body.RootElement.GetProperty("ticket").GetString());
                    Assert.Equal("kr-812", body.RootElement.GetProperty("build").GetString());
                    Assert.Equal(Grant.GrantId, body.RootElement.GetProperty("grantId").GetString());
                    Refreshes++;
                    return Closed ? new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { error = "admission_closed" }) }
                        : Reply(new { serverSelection = Grant });
                }
                default: throw new InvalidOperationException("Unexpected selection API test route.");
            }
        }

        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
