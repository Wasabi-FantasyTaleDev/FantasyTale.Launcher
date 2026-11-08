using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerResolutionFailureTests
{
    private static readonly ServerDefinition Server = new("fixture", "test", "Fixture", "http://game.example/", null, true, false)
    { PreferIp = false };

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.TryAgain)]
    [InlineData(SocketError.NoData)]
    public async Task ResolverSocketFailuresExposeOnlyTheDiagnosticCode(SocketError socketError)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy(new SocketException((int)socketError)));

        var error = await Assert.ThrowsAsync<ServerFailureException>(() => LoginAsync(api));

        Assert.Equal("network_error", error.Code);
        Assert.Equal(socketError.ToString(), error.TransportCode);
        AssertSanitized(error);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task EmptyAddressResultsAreStructuredFailures()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, new NetworkPolicy(() => "all", (_, _) => Task.FromResult(Array.Empty<IPAddress>())));

        var error = await Assert.ThrowsAsync<ServerFailureException>(() => LoginAsync(api));

        Assert.Equal("network_error", error.Code);
        Assert.Equal("NameResolutionError", error.TransportCode);
        AssertSanitized(error);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ResolverIoFailuresDiscardTheResolverMessage()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy(new IOException("https://game.example/?token=private")));

        var error = await Assert.ThrowsAsync<ServerFailureException>(() => LoginAsync(api));

        Assert.Equal("network_error", error.Code);
        Assert.Equal("NameResolutionError", error.TransportCode);
        AssertSanitized(error);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task DirectGameEndpointResolutionUsesTheSameSafeFailure()
    {
        var error = await Assert.ThrowsAsync<ServerFailureException>(() =>
            Policy(new SocketException((int)SocketError.HostNotFound)).HostAsync("game.example", Server, "ip", null, default));

        Assert.Equal("network_error", error.Code);
        Assert.Equal("HostNotFound", error.TransportCode);
        AssertSanitized(error);
    }

    [Fact]
    public async Task PublicDnsHttpFailuresRetainStatusWithoutRequestContents()
    {
        var error = await Assert.ThrowsAsync<ServerFailureException>(() =>
            Policy(new HttpRequestException(HttpRequestError.ConnectionError, "https://game.example/?token=private", null,
                HttpStatusCode.ServiceUnavailable)).HostAsync("game.example", Server, "ip", null, default));

        Assert.Equal("network_error", error.Code);
        Assert.Equal("ConnectionError", error.TransportCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("game.example", error.ToString());
        Assert.DoesNotContain("private", error.ToString());
    }

    [Fact]
    public async Task CancellationDuringResolutionRemainsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var policy = new NetworkPolicy(() => "all", (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<IPAddress[]>(token);
        });
        using var api = new LauncherApiClient(http, policy);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoginAsync(api, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ResolverTimeoutRetainsTheTimeoutCategory()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy(new OperationCanceledException("https://game.example/?token=private")));

        var error = await Assert.ThrowsAsync<ServerFailureException>(() => LoginAsync(api));

        Assert.Equal("network_timeout", error.Code);
        Assert.Equal("timeout", error.TransportCode);
        AssertSanitized(error);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task DiscoveryFallbackDoesNotSendCredentialsAfterResolutionFails()
    {
        using var handler = new Handler(failDiscovery: true);
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, Policy(new SocketException((int)SocketError.HostNotFound)));

        var error = await Assert.ThrowsAsync<ServerFailureException>(() => api.LoginAsync(Server, "fixture-user", "fixture-password"));

        Assert.Equal("HostNotFound", error.TransportCode);
        AssertSanitized(error);
        Assert.Equal(2, handler.Requests);
        Assert.Null(api.Current(Server));
    }

    [Fact]
    public async Task InvalidApiDataIsNotReportedAsDnsFailure()
    {
        using var handler = new Handler(malformed: true);
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http, new NetworkPolicy(() => "disabled"));

        await Assert.ThrowsAsync<InvalidDataException>(() => api.CatalogAsync(Server with { ApiUrl = "https://game.example/" }));

        Assert.Equal(1, handler.Requests);
    }

    private static NetworkPolicy Policy(Exception error) => new(() => "all", (_, _) => Task.FromException<IPAddress[]>(error));

    private static Task<AccountProfile> LoginAsync(LauncherApiClient api, CancellationToken ct = default) =>
        api.LoginAsync(Server, "fixture-user", "fixture-password", ct);

    private static void AssertSanitized(ServerFailureException error)
    {
        Assert.Null(error.InnerException);
        Assert.Null(error.StatusCode);
        Assert.DoesNotContain("game.example", error.ToString());
        Assert.DoesNotContain("private", error.ToString());
        Assert.DoesNotContain("fixture-password", error.ToString());
    }

    private sealed class Handler(bool failDiscovery = false, bool malformed = false) : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Null(request.Headers.Authorization);
            Assert.Null(request.Content);
            if (failDiscovery)
            {
                Assert.Equal("/v1/catalog", request.RequestUri!.AbsolutePath);
                throw new HttpRequestException("Discovery unavailable.");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = malformed ? new StringContent("invalid JSON") :
                    JsonContent.Create(new LauncherCatalog(1, "test", "Fixture", false, [], []) { HostPolicy = "ip" })
            });
        }
    }
}
