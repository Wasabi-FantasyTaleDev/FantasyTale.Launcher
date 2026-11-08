using System.Net;
using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData(401, "serverAccess")]
    [InlineData(403, "serverAccess")]
    [InlineData(404, "feedMissing")]
    [InlineData(408, "timeout")]
    [InlineData(429, "serverBusy")]
    [InlineData(503, "serverError")]
    [InlineData(504, "timeout")]
    public void DiscoveryRetainsHttpReasonsWithoutExceptionMessages(int status, string reason)
    {
        var state = LauncherUpdateService.CheckFailure(new HttpRequestException("private-token?secret=value", null, (HttpStatusCode)status));
        Assert.Equal(reason, state.FailureReason);
        Assert.Equal(status, state.HttpStatus);
        Assert.Null(state.Version);
        Assert.Null(state.ManifestSha256);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(state));
    }

    [Fact]
    public void DiscoverySeparatesConnectionMetadataAndLocalFailures()
    {
        Assert.Equal("timeout", LauncherUpdateService.CheckFailure(new TaskCanceledException()).FailureReason);
        Assert.Equal("serverNotFound", LauncherUpdateService.CheckFailure(new HttpRequestException(HttpRequestError.NameResolutionError)).FailureReason);
        Assert.Equal("secureConnection", LauncherUpdateService.CheckFailure(new HttpRequestException(HttpRequestError.SecureConnectionError)).FailureReason);
        Assert.Equal("connection", LauncherUpdateService.CheckFailure(new HttpRequestException(HttpRequestError.ConnectionError)).FailureReason);
        Assert.Equal("invalidInformation", LauncherUpdateService.CheckFailure(new InvalidDataException()).FailureReason);
        Assert.Equal("localStorage", LauncherUpdateService.CheckFailure(new UnauthorizedAccessException()).FailureReason);
        Assert.Equal("unknown", LauncherUpdateService.CheckFailure(new InvalidOperationException("private")).FailureReason);
    }

    [Fact]
    public async Task AnUnpublishedNewerInstallationExplainsTheOlderFeedWithoutOfferingADownload()
    {
        Policy();
        using var handler = new FeedHandler(IndexBytes(Manifest(3)));
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal("available", (await service.CheckAsync()).Status);
        handler.Feed = IndexBytes(Manifest(2));
        var state = await service.CheckAsync();
        Assert.Equal("failed", state.Status);
        Assert.Equal("feedOutdated", state.FailureReason);
        Assert.Null(state.Version);
        Assert.Null(state.ManifestSha256);
    }
}
