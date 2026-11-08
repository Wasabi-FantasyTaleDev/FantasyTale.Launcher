using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task EarlyStartupDiscoveryPreservesAPausedSelectionBeforeTheFrontendRestoresTransfers()
    {
        Policy();
        using var handler = new FeedHandler(IndexBytes(Manifest()));
        using var http = new HttpClient(handler);
        string selectedHash;
        await using (var previous = new LauncherUpdateService(Installation, Data, http))
        {
            previous.SavePreferences(new(false, false));
            selectedHash = (await previous.CheckAsync()).ManifestSha256!;
        }
        var updateRoot = Path.Combine(Data, "launcher-update");
        File.WriteAllText(Path.Combine(updateRoot, "transfer.json"), JsonSerializer.Serialize(new { Hash = selectedHash, Stage = "paused" }));
        handler.Feed = IndexBytes(Manifest(3));
        var requests = handler.Requests;

        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        var startup = restarted.BeginStartupCheck();
        Assert.Same(startup, restarted.BeginStartupCheck());
        await startup;
        // The native window can finish initialization after the early metadata request.
        await restarted.RestoreTransferAsync(CancellationToken.None);

        Assert.Equal("downloading", restarted.State.Status);
        Assert.Null(restarted.State.FailureReason);
        Assert.Equal(selectedHash, restarted.State.ManifestSha256);
        Assert.Equal(2, restarted.State.Revision);
        Assert.Equal("paused", Assert.Single(restarted.List()).Stage);
        Assert.Equal(requests, handler.Requests);
        Assert.Equal(selectedHash, Hash(File.ReadAllBytes(Path.Combine(updateRoot, "release.toml"))));
    }

    [Fact]
    public async Task StartupRepairsAnInterruptedSelectionBeforePublishingFreshDiscoveryState()
    {
        Policy();
        using var handler = new FeedHandler(IndexBytes(Manifest()));
        using var http = new HttpClient(handler);
        await using (var previous = new LauncherUpdateService(Installation, Data, http))
            await previous.CheckAsync();
        var updateRoot = Path.Combine(Data, "launcher-update");
        File.WriteAllText(Path.Combine(updateRoot, "transfer.json"), JsonSerializer.Serialize(new { Hash = new string('a', 64), Stage = "queued" }));
        handler.Feed = IndexBytes(Manifest(3));

        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        var state = await restarted.BeginStartupCheck();

        Assert.Equal("available", state.Status);
        Assert.Equal(state, restarted.State);
        Assert.Equal(3, state.Revision);
        Assert.Null(state.FailureReason);
        Assert.Single(restarted.SourceResults);
        Assert.Empty(restarted.List());
    }
}
