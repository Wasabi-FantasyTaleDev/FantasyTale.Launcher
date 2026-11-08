using System.Net;
using System.Text;
using FantasyTale.Launcher.Downloads.AList;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AListResolutionTests
{
    [Fact]
    public async Task WrappedMissingObjectIsNotRetriedAsAnUpstreamOutage()
    {
        using var handler = new Replies((_, _, _) => Task.FromResult(Reply(HttpStatusCode.OK,
            "{\"code\":500,\"message\":\"failed get parent list: failed get dir: object not found\"}")));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("missing.zip", default));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }
    private const string Success = """
        {"code":200,"data":{"size":12,"is_dir":false,"raw_url":"https://storage.example/object"}}
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransientHttpAndProviderFailuresResolveAgainWithoutLosingCredentials(bool envelope)
    {
        using var handler = new Replies(async (request, attempt, cancellation) =>
        {
            Assert.Equal("private-token", request.Headers.GetValues("Authorization").Single());
            var body = await request.Content!.ReadAsStringAsync(cancellation);
            Assert.Contains("/FantasyTale/object", body);
            Assert.Contains("folder-password", body);
            return attempt == 1
                ? Reply(envelope ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, "{\"code\":500}")
                : Reply(HttpStatusCode.OK, Success);
        });
        using var http = new HttpClient(handler);
        var source = new AListSource(http, new Uri("https://alist.example/"), "private-token", "folder-password");
        var resolved = await source.ResolveAsync("FantasyTale/object", default);
        Assert.Equal(2, handler.Attempts);
        Assert.Equal(12, resolved.Size);
        Assert.Equal("https://storage.example/object", resolved.Url.AbsoluteUri);
        Assert.Empty(resolved.Headers);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task PermanentProviderRejectionDoesNotRetry(int code)
    {
        using var handler = new Replies((_, _, _) => Task.FromResult(Reply(HttpStatusCode.OK, $"{{\"code\":{code}}}")));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("missing", default));
        Assert.Equal((HttpStatusCode)code, error.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task PersistentProviderFailureStopsAfterFourAttempts()
    {
        using var handler = new Replies((_, _, _) => Task.FromResult(Reply(HttpStatusCode.OK, "{\"code\":503}")));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("object", default));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(4, handler.Attempts);
    }

    [Fact]
    public async Task CallerCancellationStopsBeforeRetry()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Replies((_, _, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("object", cancellation.Token));
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task MalformedMetadataDoesNotRetry()
    {
        using var handler = new Replies((_, _, _) => Task.FromResult(Reply(HttpStatusCode.OK,
            "{\"code\":200,\"data\":{\"size\":12,\"is_dir\":true}}")));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("folder", default));
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureAndRequestTimeoutCanRecover(bool timeout)
    {
        using var handler = new Replies((_, attempt, _) =>
        {
            if (attempt == 1)
            {
                if (timeout) throw new TaskCanceledException("HTTP timeout");
                throw new HttpRequestException("Connection interrupted");
            }
            return Task.FromResult(Reply(HttpStatusCode.OK, Success));
        });
        using var http = new HttpClient(handler);
        await new AListSource(http, new Uri("https://alist.example/")).ResolveAsync("object", default);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task HttpAuthenticationFailureDoesNotRetry()
    {
        using var handler = new Replies((_, _, _) => Task.FromResult(Reply(HttpStatusCode.Forbidden, "")));
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new AListSource(http, new Uri("https://alist.example/"))
            .ResolveAsync("object", default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task StalledHeadersAndBodyRecoverWithInfiniteHttpTimeout()
    {
        await Task.WhenAll(ResolveStalledAsync(false), ResolveStalledAsync(true)).WaitAsync(TimeSpan.FromSeconds(45));

        static async Task ResolveStalledAsync(bool body)
        {
            using var handler = new Replies(async (_, attempt, cancellation) =>
            {
                if (attempt != 1) return Reply(HttpStatusCode.OK, Success);
                if (body) return new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                throw new InvalidOperationException("A stalled request unexpectedly resumed.");
            });
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var resolved = await new AListSource(http, new Uri("https://alist.example/")).ResolveAsync("object", default);
            Assert.Equal(2, handler.Attempts);
            Assert.Equal("https://storage.example/object", resolved.Url.AbsoluteUri);
        }
    }

    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("A stalled response unexpectedly resumed.");
        }
    }

    private sealed class Replies(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => reply(request, ++Attempts, cancellationToken);
    }
}
