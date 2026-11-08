using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task RolledBackReleaseCannotBecomeReadyOrAutomaticallyRetryAfterRestart()
    {
        var policy = Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        string hash;
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(true, true));
            hash = (await first.CheckAsync()).ManifestSha256!;
            await first.DownloadAsync(hash); await UntilAsync(() => first.List()[0].Stage == "ready");
        }
        File.WriteAllText(Path.Combine(Data, "launcher-update", "outcome.json"), JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome("rolled-back", policy.Identity, hash)));
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        restarted.StartMonitoring(); await UntilAsync(() => restarted.State.Failure == "startup");
        Assert.Equal("failed", restarted.List()[0].Stage);
        Assert.Throws<InvalidOperationException>(restarted.ReadyDirectory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ActionAsync("retry"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.DownloadAsync(hash));
        Assert.Equal("startup", (await restarted.CheckAsync()).Failure);
        await restarted.ActionAsync("cancel");
        Assert.Equal("failed", restarted.State.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.DownloadAsync(hash));
        restarted.ClearFinished();
        Assert.Equal("startup", (await restarted.CheckAsync()).Failure);
        handler.Feed = IndexBytes(Manifest(3));
        var newer = await restarted.CheckAsync(); Assert.Equal("available", newer.Status); Assert.Empty(restarted.List());
        await restarted.DownloadAsync(newer.ManifestSha256!); await UntilAsync(() => restarted.List()[0].Stage == "ready");
        Assert.Null(restarted.State.Failure);
    }

    [Theory]
    [InlineData("repaired")]
    [InlineData("no-recovery-needed")]
    public async Task OfflineRepairOutcomeCannotResetARejectedCandidate(string status)
    {
        var policy = Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        var updateRoot = Path.Combine(Data, "launcher-update"); Directory.CreateDirectory(updateRoot);
        File.WriteAllText(Path.Combine(updateRoot, "rejected.json"), JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome("rolled-back", policy.Identity, Hash(IndexBytes(Manifest())))));
        File.WriteAllText(Path.Combine(updateRoot, "outcome.json"), JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome(status)));
        await using var service = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal("startup", (await service.CheckAsync()).Failure);
        Assert.Equal("failed", service.List()[0].Stage);
        handler.Feed = IndexBytes(Manifest(3));
        var newer = await service.CheckAsync();
        Assert.Equal("available", newer.Status);
        await service.DownloadAsync(newer.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
    }

    [Theory]
    [InlineData("other-source", "outcome.json")]
    [InlineData("other-manifest", "outcome.json")]
    [InlineData("legacy", "outcome.json")]
    [InlineData("completed", "outcome.json")]
    [InlineData("other-source", "rejected.json")]
    [InlineData("other-manifest", "rejected.json")]
    [InlineData("legacy", "rejected.json")]
    [InlineData("completed", "rejected.json")]
    public async Task UnrelatedAndLegacyOutcomesDoNotBlockTheSelectedRelease(string scenario, string recordFile)
    {
        var policy = Policy(); using var handler = new FeedHandler(IndexBytes(Manifest())); using var http = new HttpClient(handler);
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(false, false));
            await first.DownloadAsync((await first.CheckAsync()).ManifestSha256!);
            await UntilAsync(() => first.List()[0].Stage == "ready");
        }
        var hash = Hash(IndexBytes(Manifest()));
        var outcome = new LauncherUpdateHandoff.Outcome(scenario == "completed" ? "completed" : "rolled-back",
            scenario == "other-source" ? "other" : scenario == "legacy" ? null : policy.Identity,
            scenario == "other-manifest" ? new string('a', 64) : scenario == "legacy" ? null : hash);
        File.WriteAllText(Path.Combine(Data, "launcher-update", recordFile), JsonSerializer.Serialize(outcome));
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        restarted.StartMonitoring(); await UntilAsync(() => restarted.List().Count > 0 && restarted.List()[0].Stage == "ready");
        Assert.Null(restarted.State.Failure);
    }
}
