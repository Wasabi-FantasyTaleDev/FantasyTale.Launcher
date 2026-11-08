using System.Net;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Downloads.AList;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class SourceDocumentRetryTests
{
    private sealed class Replies(Func<HttpRequestMessage, int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(reply(request, ++Calls));
        }
    }

    [Fact]
    public async Task GatewayRetryResolvesAFreshStorageUrlWithoutForwardingApiCredentials()
    {
        var resolutions = 0;
        using var handler = new Replies((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("token", Assert.Single(request.Headers.GetValues("Authorization")));
                resolutions++;
                return new(HttpStatusCode.OK) { Content = new StringContent($"{{\"code\":200,\"data\":{{\"size\":3,\"raw_url\":\"https://storage.example/{resolutions}\"}}}}") };
            }
            Assert.False(request.Headers.Contains("Authorization"));
            return request.RequestUri!.AbsolutePath == "/1" ? new(HttpStatusCode.BadGateway) :
                new(HttpStatusCode.OK) { Content = new ByteArrayContent("new"u8.ToArray()) };
        });
        using var http = new HttpClient(handler);
        Assert.Equal("new"u8.ToArray(), await SourceDocumentReader.ReadBytesAsync(http,
            new AListSource(http, new Uri("https://files.example/"), "token"), "index.toml", default));
        Assert.Equal(2, resolutions); Assert.Equal(4, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentFailuresDoNotRetry(HttpStatusCode status)
    {
        using var handler = new Replies((_, _) => new(status)); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => SourceDocumentReader.ReadBytesAsync(http,
            new HttpSource(new Uri("https://static.example/")), "index.toml", default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TransportRetriesStopAfterFourAttempts()
    {
        using var handler = new Replies((_, _) => throw new HttpRequestException("Fixture connection failure"));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => SourceDocumentReader.ReadBytesAsync(http,
            new HttpSource(new Uri("https://static.example/")), "index.toml", default));
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task CancellationDuringBackoffDoesNotSendAnotherRequest()
    {
        using var stop = new CancellationTokenSource();
        using var handler = new Replies((_, _) => { stop.CancelAfter(20); return new(HttpStatusCode.ServiceUnavailable); });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SourceDocumentReader.ReadBytesAsync(http,
            new HttpSource(new Uri("https://static.example/")), "index.toml", stop.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task MetadataLengthMismatchIsRejectedWithoutRetry(int declared)
    {
        using var handler = new Replies((request, _) => request.Method == HttpMethod.Post ?
            new(HttpStatusCode.OK) { Content = new StringContent($"{{\"code\":200,\"data\":{{\"size\":{declared},\"raw_url\":\"https://storage.example/file\"}}}}") } :
            new(HttpStatusCode.OK) { Content = new ByteArrayContent("new"u8.ToArray()) });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => SourceDocumentReader.ReadBytesAsync(http,
            new AListSource(http, new Uri("https://files.example/")), "index.toml", default));
        Assert.Equal(2, handler.Calls);
    }
}
