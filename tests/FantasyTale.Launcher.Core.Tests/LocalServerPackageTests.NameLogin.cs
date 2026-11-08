using System.Diagnostics;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LocalServerPackageTests
{
    private sealed class NamePackageFactAttribute : FactAttribute
    {
        public NamePackageFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_DATA")))
                Skip = "Set FT_TEST_SERVER_PACKAGE and FT_TEST_CLIENT_DATA for an isolated resource and name-login smoke.";
        }
    }

    private sealed class IsolatedModule(string patch) : ILocalServerModule
    {
        public LocalServerPlan CreateServerPlan(LocalServerContext context)
        {
            var plan = new BubbleFighterModule().CreateServerPlan(context);
            return plan with { Arguments = [.. plan.Arguments, "--patch", patch] };
        }
    }

    [NamePackageFact]
    public async Task InstalledClientResourcesStartAnIsolatedNameOnlyServer()
    {
        var root = Directory.CreateTempSubdirectory("ft-name-host-").FullName;
        try
        {
            var package = Path.Combine(root, "package");
            CopyPackage(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")!, package);
            var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(game => game.Id == "bubblefighter")
                with { LocalHostingEnabled = true };
            var ports = LocalServerNetwork.AvailablePorts();
            var profile = new LocalServerProfile("name-smoke", game.Id, "cn-279", "Local verification", package,
                "private", "127.0.0.1", ports[0], ports[2], ports[3], true) { StorageMode = "temporary", RelayPort = ports[1] };
            var checks = Path.Combine(root, "tools", "local-checks"); Directory.CreateDirectory(checks);
            var isolation = Path.Combine(checks, "isolated-memory.patch.yaml");
            await File.WriteAllTextAsync(isolation, "- replace:\n    id: persistence.mysql\n    disabled: true\n- replace:\n    id: persistence.memory\n    disabled: false\n");
            await using var hosting = new LocalServerService(Path.Combine(root, "runs"));
            var status = await hosting.StartAsync(profile, game, new IsolatedModule(isolation), Environment.GetEnvironmentVariable("FT_TEST_CLIENT_DATA"));
            var timer = Stopwatch.StartNew();
            while (status.State == "starting" && timer.Elapsed < TimeSpan.FromSeconds(100))
            {
                await Task.Delay(100); status = Assert.Single(hosting.List());
            }
            var log = string.Join('\n', hosting.Log(profile.Id, status.RunId));
            Assert.DoesNotContain("MySQL persistence connected", log, StringComparison.OrdinalIgnoreCase);
            Assert.True(status.State == "ready", status.Failure + "\n" + log);
            var server = hosting.Destination(profile.Id, status.RunId)!;
            using var api = new LauncherApiClient();
            var catalog = await api.CatalogAsync(server);
            var instance = Assert.Single(catalog.Instances);
            Assert.Equal("none", instance.LauncherLogin);
            Assert.Equal(new ServerEndpoint("127.0.0.1", profile.GamePort), instance.Endpoint);
            var patch = await File.ReadAllTextAsync(Path.Combine(root, "runs", profile.Id, "hosting.patch.yaml"));
            Assert.Contains("authentication: open", patch, StringComparison.Ordinal);
            await hosting.StopAsync(profile.Id, status.RunId);
            AssertPortsReleased([.. ports]);
        }
        finally { Directory.Delete(root, true); }
    }
}
