using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ApiReadResilienceTests
{
    private static readonly ServerDefinition Server = new("fixture", "fixture", "Fixture", "http://192.0.2.1/", null, true, false);

    [Theory]
    [InlineData("all", null, "203.0.113.7")]
    [InlineData("disabled", true, "203.0.113.7")]
    [InlineData("china", true, "203.0.113.7")]
    [InlineData("china", null, "game.example")]
    [InlineData("disabled", null, "game.example")]
    [InlineData("all", false, "game.example")]
    public async Task InitialDiscoveryHonorsRoutingPreferencesWithoutADomainProbe(string preference, bool? preferIp, string host)
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(host, request.RequestUri!.Host); Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json(new LauncherCatalog(1, "fixture", "Fixture", false, [], [])));
        });
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, new NetworkPolicy(() => preference, (_, _) => Task.FromResult(new[] { IPAddress.Parse("203.0.113.7") })));
        await api.CatalogAsync(Server with { ApiUrl = "http://game.example/", PreferIp = preferIp });
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("timeout")]
    [InlineData("gateway")]
    public async Task TransientReadFailureRecoversWithinOneCallerRequest(string failure)
    {
        var attempts = 0;
        using var handler = new Handler((_, _) =>
        {
            if (++attempts == 1)
            {
                if (failure == "timeout") throw new TaskCanceledException("Private transport detail");
                if (failure == "connection") throw new HttpRequestException("Private transport detail");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
            return Task.FromResult(Json(new OnlinePlayerList(1, true, [])));
        });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        Assert.Empty((await api.OnlinePlayersAsync(Server)).Players); Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task PersistentReadsStopAfterOneRetryAndDoNotExposeTransportText()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("Private transport detail"));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var failure = await Assert.ThrowsAsync<ServerFailureException>(() => api.OnlinePlayersAsync(Server));
        Assert.Equal("network_error", failure.Code); Assert.Equal(2, handler.Requests);
        Assert.DoesNotContain("Private", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticationPostIsNeverReplayedAfterATransportFailure()
    {
        var posts = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Json(new LauncherCatalog(1, "fixture", "Fixture", false, [], [])));
            posts++; throw new HttpRequestException("The request may have reached the server");
        });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await Assert.ThrowsAsync<ServerFailureException>(() => api.LoginAsync(Server, "fixture", "fixture"));
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task DirectoryInternalTimeoutIsStructuredWhileCallerCancellationRemainsCancellation()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(); });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var failure = await Assert.ThrowsAsync<ServerFailureException>(() => api.ServerDirectoryAsync(Server, "fixture-build"));
        Assert.Equal("network_timeout", failure.Code); Assert.Equal("timeout", failure.TransportCode);
        Assert.Equal(1, handler.Requests);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ServerDirectoryAsync(Server, "fixture-build", caller.Token));
        Assert.Equal(2, handler.Requests);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return send(request, cancellationToken); }
    }
}
