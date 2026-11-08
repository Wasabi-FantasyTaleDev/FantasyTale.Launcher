using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public void ManualDownloadPageIsIndependentOfAnUnavailableOrOlderUpdateFeed()
    {
        Directory.CreateDirectory(Installation);
        Assert.Null(LauncherManualDownload.Load(Installation));
        File.WriteAllText(Path.Combine(Installation, LauncherManualDownload.FileName), "schema = 1\nurl = 'https://downloads.example/latest'\n");
        Assert.Equal("https://downloads.example/latest", LauncherManualDownload.Load(Installation));
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), "malformed automatic update settings");
        Assert.Equal("https://downloads.example/latest", LauncherManualDownload.Load(Installation));
    }

    [Theory]
    [InlineData("schema = 2\nurl = 'https://downloads.example/latest'")]
    [InlineData("schema = 1\nurl = 'file:///private'")]
    [InlineData("schema = 1\nurl = 'javascript:run()'")]
    [InlineData("schema = 1\nurl = 'https://account:password@downloads.example/latest'")]
    public void ManualDownloadDefinitionsRejectUnsupportedFormatsAndUnsafeAddresses(string text)
    {
        Directory.CreateDirectory(Installation);
        File.WriteAllText(Path.Combine(Installation, LauncherManualDownload.FileName), text);
        Assert.Throws<DefinitionException>(() => LauncherManualDownload.Load(Installation));
    }
}
