using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task UnconfiguredInstallationPreservesUnrelatedTransferWithoutReportingInvalidRemoteMetadata()
    {
        Directory.CreateDirectory(Installation);
        var cache = Directory.CreateDirectory(Path.Combine(Data, "launcher-update")).FullName;
        var intentPath = Path.Combine(cache, "transfer.json");
        var intent = "{\"Hash\":\"" + new string('a', 64) + "\",\"Stage\":\"queued\"}";
        File.WriteAllText(intentPath, intent);
        using var handler = new FeedHandler(IndexBytes(Manifest()));
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);

        Assert.Equal("unconfigured", (await service.BeginStartupCheck()).Status);
        await service.RestoreTransferAsync(CancellationToken.None);

        Assert.Equal("unconfigured", service.State.Status);
        Assert.Null(service.State.Failure);
        Assert.Empty(service.List());
        Assert.Empty(service.SourceResults);
        Assert.Equal(intent, File.ReadAllText(intentPath));

        Policy();
        Assert.Equal("available", (await service.CheckAsync()).Status);
    }
}
