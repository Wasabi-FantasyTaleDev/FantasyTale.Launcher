using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class StoreSourceSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingSourceRetriesBriefWindowsReadersAndPreservesPreviousOnPersistentLock(bool persistent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-source-save-").FullName;
        FileStream? reader = null;
        Task? saving = null;
        using var started = new ManualResetEventSlim();
        try
        {
            var store = new FantasyTale.Launcher.Core.Stores.StoreSourceStore(root);
            var source = new FantasyTale.Launcher.Sdk.StoreSource("fixture", "Before", "http", "https://example.test/", "source.toml", true, false);
            store.Save(source);
            reader = new FileStream(Path.Combine(root, "fixture.toml"), FileMode.Open, FileAccess.Read, FileShare.Read);
            saving = Task.Factory.StartNew(() => { started.Set(); store.Save(source with { Name = "After" }); },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            if (persistent)
            {
                var error = await Record.ExceptionAsync(() => saving.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.Equal("Before", store.List().Single(row => row.Id == source.Id).Name);
            }
            else
            {
                Thread.Sleep(40);
                reader.Dispose(); reader = null;
                await saving.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("After", store.List().Single(row => row.Id == source.Id).Name);
            }
            Assert.Empty(Directory.GetFiles(root, ".saving-*"));
        }
        finally
        {
            reader?.Dispose();
            if (saving is not null)
                try { await saving.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("store.downloadPlugin", "kind")]
    [InlineData("store.downloadPlugin", "baseUrl")]
    [InlineData("store.downloadPlugin", "catalogPath")]
    [InlineData("store.installPlugin", "kind")]
    [InlineData("store.installPlugin", "baseUrl")]
    [InlineData("store.installPlugin", "catalogPath")]
    [InlineData("store.installPluginTargets", "kind")]
    [InlineData("store.installPluginTargets", "baseUrl")]
    [InlineData("store.installPluginTargets", "catalogPath")]
    public async Task ChangedSourceIsRejectedBeforeDistributionOrServerReads(string method, string changedField)
    {
        var root = Directory.CreateTempSubdirectory("ft-source-selection-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); Directory.CreateDirectory(games);
            await using var original = new StoreWireTests.WireServer(new());
            await using var replacement = new StoreWireTests.WireServer(new());
            await using var backend = new LauncherBackend(games, Path.Combine(root, "data"));
            var replies = new ConcurrentQueue<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            var expected = new { kind = "http", baseUrl = original.Address, catalogPath = "source.toml" };
            var changed = new
            {
                id = "fixture", name = "Fixture", enabled = true,
                kind = changedField == "kind" ? "alist" : "http",
                baseUrl = changedField == "baseUrl" ? replacement.Address : original.Address,
                catalogPath = changedField == "catalogPath" ? "different.toml" : "source.toml"
            };
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 1, method = "store.saveSource", @params = changed }), default);
            Assert.Null(replies.Single(value => value["id"]?.GetValue<int>() == 1)["error"]);
            await bridge.HandleAsync(JsonSerializer.Serialize(new
            {
                id = 2, method,
                @params = new Dictionary<string, object>
                {
                    ["sourceId"] = "fixture", ["expectedSource"] = expected,
                    ["packageId"] = "fixture.plugin", ["version"] = "1.0.0", ["manifestSha256"] = new string('a', 64)
                }
            }), default);
            var error = replies.Single(value => value["id"]?.GetValue<int>() == 2)["error"];
            Assert.Contains("download source changed", error!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Empty(original.GetPaths); Assert.Empty(replacement.GetPaths);
            Assert.Equal(0, original.Resolutions); Assert.Equal(0, replacement.Resolutions);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
