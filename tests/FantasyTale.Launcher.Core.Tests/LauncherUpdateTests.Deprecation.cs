using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    private static string DeprecatedManifest(string boundary = "0.1.37") => Manifest()
        .Replace("version = \"Next\"", $"version = \"0.1.38\"\ndeprecated_max_version = \"{boundary}\"", StringComparison.Ordinal);

    [Theory]
    [InlineData("0.1.36", true)]
    [InlineData("0.1.37", true)]
    [InlineData("0.1.37+test", true)]
    [InlineData("0.1.38", false)]
    [InlineData("0.1.39", false)]
    public async Task DeprecatedBoundaryIsInclusiveAndUsesSemanticVersions(string current, bool mandatory)
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(DeprecatedManifest())); using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http, current);
        Assert.Equal(mandatory, (await service.CheckAsync()).Mandatory);
        Assert.Equal(mandatory, service.State.Mandatory);
    }

    [Theory]
    [InlineData("0.1.38")]
    [InlineData("0.1.39")]
    [InlineData("not-a-version")]
    public void InvalidDeprecatedBoundaryCannotDisableTheCurrentRelease(string boundary)
    {
        var policy = Policy();
        Assert.ThrowsAny<Exception>(() => LauncherRelease.Parse(policy, IndexBytes(DeprecatedManifest(boundary))));
    }

    [Fact]
    public void AdditiveIndexAndFileMetadataDoesNotWeakenKnownFieldValidation()
    {
        var policy = Policy();
        var text = Manifest().Replace("[[files]]", "future_label = { text = 'optional' }\n[[files]]", StringComparison.Ordinal) + "\nfuture_file_field = ['one', 'two']\n";
        Assert.Equal(3, LauncherRelease.Parse(policy, IndexBytes(text)).Files.Count);
        Assert.ThrowsAny<Exception>(() => LauncherRelease.Parse(policy, IndexBytes(text.Replace("size = 3", "size = '3'", StringComparison.Ordinal))));
        Assert.ThrowsAny<Exception>(() => LauncherRelease.Parse(policy, IndexBytes(text.Replace("wwwroot/index.html", "../escape", StringComparison.Ordinal))));
        Assert.ThrowsAny<Exception>(() => LauncherRelease.Parse(policy, IndexBytes(text.Replace("schema = 1", "schema = 99", StringComparison.Ordinal))));
    }

    [Fact]
    public async Task MandatoryRequirementSurvivesNetworkFailureAndOfflineRestart()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(DeprecatedManifest())); using var http = new HttpClient(handler);
        await using (var service = new LauncherUpdateService(Installation, Data, http, "0.1.37"))
        {
            var selected = await service.CheckAsync();
            handler.Unavailable = true;
            var failed = await service.CheckAsync();
            Assert.True(failed.Mandatory); Assert.Equal(selected.ManifestSha256, failed.ManifestSha256);
            Assert.Equal("failed", failed.Status); Assert.Equal("network", failed.Failure);
        }
        await using var restarted = new LauncherUpdateService(Installation, Data, http, "0.1.37");
        var offline = await restarted.BeginStartupCheck();
        Assert.True(offline.Mandatory); Assert.Equal("0.1.38", offline.Version);
    }

    [Fact]
    public async Task RetiredLocalPolicyCannotOverrideTheIndexOrPreventOfflineRequirementRestoration()
    {
        Policy();
        var retiredPolicy = "invalid retired policy"u8.ToArray();
        var manifest = DeprecatedManifest() + $"\n[[files]]\npath = 'launcher-release-policy.toml'\nsource_path = 'objects/retired-policy'\nsize = {retiredPolicy.Length}\nsha256 = '{Hash(retiredPolicy)}'\n";
        using var handler = new FeedHandler(IndexBytes(manifest)); using var http = new HttpClient(handler);
        Directory.CreateDirectory(Path.Combine(Data, "launcher-update"));
        await File.WriteAllTextAsync(Path.Combine(Data, "launcher-update/launcher-release-policy.toml"), "invalid retired policy");
        await using (var service = new LauncherUpdateService(Installation, Data, http, "0.1.37"))
        {
            Assert.True((await service.CheckAsync()).Mandatory);
            Assert.Empty(handler.PayloadPaths);
        }
        handler.Unavailable = true;
        await using var restarted = new LauncherUpdateService(Installation, Data, http, "0.1.37");
        Assert.True((await restarted.BeginStartupCheck()).Mandatory);
        Assert.Equal("0.1.38", restarted.State.Version);
    }

    [Fact]
    public async Task MandatoryTransferCannotBeCanceledOrPausedAndStillStagesVerifiedFiles()
    {
        Policy(); using var handler = new FeedHandler(IndexBytes(DeprecatedManifest())); using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http, "0.1.37");
        var selected = await service.CheckAsync();
        await service.DownloadAsync(selected.ManifestSha256!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActionAsync("cancel"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActionAsync("pause"));
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.State.Status); Assert.True(service.State.Mandatory);
    }
}
