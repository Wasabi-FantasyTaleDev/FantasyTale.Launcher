using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginUpdateMonitorTests
{
    private static readonly PluginUpdateState Available = new("optional", "1.0.0", "available", "2.0.0", new string('a', 64), "Fixture");

    [Fact]
    public async Task DiscoveryIsVisibleWhileSchedulingAndSurvivesInstallationFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")],
            (_, _) => Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available]), () => true,
            autoUpdate: () => true, install: async (_, _, ct) =>
            {
                entered.TrySetResult(); await release.Task.WaitAsync(ct); throw new HttpRequestException("source unavailable");
            });
        var checking = monitor.CheckAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(monitor.Read("client").Checking);
            Assert.Equal(Available, Assert.Single(monitor.Read("client").Updates));
        }
        finally { release.TrySetResult(); }
        await checking;
        Assert.Equal("network", monitor.Read("client").Error);
        Assert.Equal(Available, Assert.Single(monitor.Read("client").Updates));
    }

    [Fact]
    public async Task PerPluginSourceFailureRetainsKnownVersionOnlyForTheSameBuildAndBaseline()
    {
        PluginUpdateTarget[] targets = [new("client", "game/build")];
        await using var monitor = new PluginUpdateMonitor(() => targets,
            (_, _) => Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available with { Status = "network", Version = null }]), () => true);
        monitor.Remember(targets[0], [Available]);
        await monitor.CheckAsync();
        Assert.Equal(Available, Assert.Single(monitor.Read("client").Updates));
        targets = [new("client", "game/other")];
        await monitor.CheckAsync();
        Assert.Null(Assert.Single(monitor.Read("client").Updates).Version);
    }

    [Fact]
    public async Task BackgroundChecksShareBuildResultsWithoutInstallingAnything()
    {
        var calls = new List<string>();
        PluginUpdateTarget[] targets = [new("first", "game/build"), new("second", "game/build"), new("third", "game/other")];
        await using var monitor = new PluginUpdateMonitor(() => targets, (id, _) =>
        {
            calls.Add(id); return Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available]);
        }, () => true);
        await monitor.CheckAsync();
        Assert.Equal(["first", "third"], calls);
        foreach (var target in targets)
        {
            var snapshot = monitor.Read(target.InstanceId);
            Assert.False(snapshot.Checking); Assert.NotNull(snapshot.CheckedAt); Assert.Null(snapshot.Error);
            Assert.Equal(Available, Assert.Single(snapshot.Updates));
        }
        targets = [new("first", "game/build")];
        await monitor.CheckAsync();
        Assert.Empty(monitor.Read("second").Updates);
    }

    [Fact]
    public async Task ForegroundResultFencesAnOlderBackgroundPass()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<PluginUpdateState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")], async (_, ct) =>
        {
            entered.SetResult(); return await release.Task.WaitAsync(ct);
        }, () => true);
        var checking = monitor.CheckAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = Available with { Version = "3.0.0" };
        monitor.Remember(new("client", "game/build"), [newer]); release.SetResult([Available]); await checking;
        Assert.Equal(newer, Assert.Single(monitor.Read("client").Updates));
    }

    [Fact]
    public async Task RemovedOrChangedClientsCannotAcquireOldBackgroundResults()
    {
        PluginUpdateTarget[] targets = [new("client", "game/build")];
        await using var monitor = new PluginUpdateMonitor(() => targets, (_, _) =>
        {
            targets = [new("client", "game/changed")];
            return Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available]);
        }, () => true);
        await monitor.CheckAsync(); Assert.Empty(monitor.Read("client").Updates);
    }

    [Fact]
    public async Task ShutdownDrainsDiscoveryWhenTheClientWasRemovedDuringItsFinalRead()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")], async (_, ct) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            { throw new FileNotFoundException("The client was removed during discovery."); }
            return [];
        }, () => true);
        monitor.StartMonitoring();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task DisabledChecksCanBeEnabledAndShutdownDrainsTheActiveRequest()
    {
        var enabled = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")], async (_, ct) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return []; }
            finally { exited.TrySetResult(); }
        }, () => Volatile.Read(ref enabled), TimeSpan.FromHours(1));
        try
        {
            monitor.StartMonitoring(); monitor.StartMonitoring();
            await Task.Delay(50); Assert.False(entered.Task.IsCompleted);
            Volatile.Write(ref enabled, true); monitor.Wake(); monitor.Wake();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.True(monitor.Read("client").Checking);
        }
        finally { await monitor.DisposeAsync(); }
        Assert.True(exited.Task.IsCompleted); Assert.False(monitor.Read("client").Checking);
    }

    [Fact]
    public async Task FailedDiscoveryDoesNotExposeSourceSecretsAndLargeClientListsShareOneScope()
    {
        await using var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")],
            (_, _) => throw new HttpRequestException("https://source.example/?secret=fixture"), () => true);
        await monitor.CheckAsync();
        Assert.Equal("network", monitor.Read("client").Error);
        Assert.Empty(monitor.Read("client").Updates);
        var calls = 0;
        await using var excessive = new PluginUpdateMonitor(() => Enumerable.Range(0, 257)
            .Select(i => new PluginUpdateTarget(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "game/build")).ToArray(), (_, _) =>
            { calls++; return Task.FromResult<IReadOnlyList<PluginUpdateState>>([]); }, () => true);
        await excessive.CheckAsync(); Assert.Equal(1, calls); Assert.Null(excessive.Read("0").Error); Assert.NotNull(excessive.Read("256").CheckedAt);
    }

    [Fact]
    public async Task FailedScopeRetainsItsPreviousResultsWithoutBlockingHealthyScopes()
    {
        PluginUpdateTarget[] targets = [new("first", "game/first"), new("second", "game/second")];
        var failFirst = false;
        var newer = Available with { Version = "3.0.0" };
        await using var monitor = new PluginUpdateMonitor(() => targets, (id, _) =>
        {
            if (failFirst && id == "first") throw new HttpRequestException("https://source.example/?secret=fixture");
            return Task.FromResult<IReadOnlyList<PluginUpdateState>>([failFirst ? newer : Available]);
        }, () => true);
        await monitor.CheckAsync();
        var checkedAt = monitor.Read("first").CheckedAt;
        failFirst = true;
        await monitor.CheckAsync();
        Assert.Equal("network", monitor.Read("first").Error);
        Assert.Equal(checkedAt, monitor.Read("first").CheckedAt);
        Assert.Equal(Available, Assert.Single(monitor.Read("first").Updates));
        Assert.Null(monitor.Read("second").Error);
        Assert.Equal(newer, Assert.Single(monitor.Read("second").Updates));

        targets = [new("first", "game/changed"), new("second", "game/second")];
        await monitor.CheckAsync();
        Assert.Equal("network", monitor.Read("first").Error);
        Assert.Null(monitor.Read("first").CheckedAt);
        Assert.Empty(monitor.Read("first").Updates);
        failFirst = false;
        await monitor.CheckAsync();
        Assert.Null(monitor.Read("first").Error);
        Assert.Equal(Available, Assert.Single(monitor.Read("first").Updates));
    }

    [Fact]
    public void ExistingPreferencesGainAnIndependentPersistentPluginChoice()
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-preferences-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "client-updates.toml"), "auto_check = false\nauto_download = false\n");
            var settings = new ClientUpdateSettings(root);
            Assert.True(settings.Value.AutoCheckPlugins);
            Assert.True(settings.Value.AutoUpdatePlugins);
            Assert.True(settings.Value.RemoveOldPluginVersions);
            settings.Save(settings.Value with { AutoCheckPlugins = false, AutoUpdatePlugins = false, RemoveOldPluginVersions = false });
            Assert.Equal(new(false, false, false, false, false), new ClientUpdateSettings(root).Value);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AutomaticInstallationVisitsEachCurrentTargetAndRespectsItsSwitch()
    {
        var enabled = true;
        var installed = new List<string>();
        PluginUpdateTarget[] targets = [new("first", "game/build"), new("second", "game/build")];
        await using var monitor = new PluginUpdateMonitor(() => targets,
            (_, _) => Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available]), () => true,
            autoUpdate: () => enabled, install: (target, updates, _) =>
            {
                Assert.Equal(Available, Assert.Single(updates)); installed.Add(target.InstanceId); return Task.CompletedTask;
            });
        await monitor.CheckAsync();
        Assert.Equal(["first", "second"], installed);
        enabled = false;
        await monitor.CheckAsync();
        Assert.Equal(2, installed.Count);
        Assert.Equal(Available, Assert.Single(monitor.Read("second").Updates));
    }

    [Fact]
    public async Task ReadingAfterBuildChangeOrRemovalImmediatelyDiscardsCachedDiscovery()
    {
        PluginUpdateTarget[] targets = [new("client", "game/build")];
        await using var monitor = new PluginUpdateMonitor(() => targets,
            (_, _) => Task.FromResult<IReadOnlyList<PluginUpdateState>>([Available]), () => true);
        await monitor.CheckAsync();
        Assert.Single(monitor.Read("client").Updates);
        targets = [new("client", "game/changed")];
        Assert.Empty(monitor.Read("client").Updates);
        Assert.Null(monitor.Read("client").CheckedAt);
        targets = [new("client", "game/build")];
        Assert.Empty(monitor.Read("client").Updates);
        await monitor.CheckAsync();
        targets = [];
        Assert.Empty(monitor.Read("client").Updates);
    }

    [Fact]
    public async Task ForegroundDiscoverySurvivesSameBuildNetworkFailure()
    {
        await using var monitor = new PluginUpdateMonitor(() => [new("client", "game/build")],
            (_, _) => throw new HttpRequestException("Unavailable source"), () => true);
        monitor.Remember(new("client", "game/build"), [Available]);
        var checkedAt = monitor.Read("client").CheckedAt;
        await monitor.CheckAsync();
        var result = monitor.Read("client");
        Assert.Equal("network", result.Error);
        Assert.Equal(checkedAt, result.CheckedAt);
        Assert.Equal(Available, Assert.Single(result.Updates));
    }

    [Fact]
    public async Task LateForegroundResultCannotReplaceDiscoveryForANewBuild()
    {
        PluginUpdateTarget[] targets = [new("client", "game/old")];
        var original = targets[0];
        var newer = Available with { Version = "3.0.0" };
        await using var monitor = new PluginUpdateMonitor(() => targets,
            (_, _) => Task.FromResult<IReadOnlyList<PluginUpdateState>>([newer]), () => true);
        targets = [new("client", "game/new")];
        await monitor.CheckAsync();
        monitor.Remember(original, [Available]);
        Assert.Equal(newer, Assert.Single(monitor.Read("client").Updates));
        targets = [];
        monitor.Remember(new("client", "game/new"), [Available]);
        Assert.Empty(monitor.Read("client").Updates);
    }
}
