using System.Diagnostics;
using System.Text.Json;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("fixed local hosting ports")]
public sealed class LocalServerPlanTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-hosting-plan-").FullName;
    private string Package => Path.Combine(_root, "package");
    private LocalServerProfile Profile => new("fixture", "fixture", "test-1", "Fixture", Package,
        "private", "127.0.0.1", 17230, 18090, 18094, false);
    private GameDefinition Game
    {
        get
        {
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "client.exe", "", "", null, null, "none", [], []);
            return new("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, _root, [build]);
        }
    }

    private sealed class Module(LocalServerPlan plan) : ILocalServerModule
    {
        public LocalServerPlan CreateServerPlan(LocalServerContext context) => plan;
    }

    private LocalServerPlan Plan()
    {
        Directory.CreateDirectory(Package);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "test-client")))
            File.Copy(file, Path.Combine(Package, Path.GetFileName(file)), true);
        return new(Path.Combine(Package, "FantasyTale.Launcher.TestClient.exe"), ["--hosting-probe", "plan"], [],
            "http://127.0.0.1:18090/", "http://127.0.0.1:18094/") { ShutdownSeconds = 1 };
    }

    private static async Task WaitReady(LocalServerService service, int startupSeconds)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var status = Assert.Single(service.List());
            if (status.State == "ready") return;
            Assert.True(status.State != "failed", $"Hosting plan failed: {status.Failure}, started={status.StartedAt:O}, exit={status.ExitCode}, forced={status.ForcedStop}.\n"
                + string.Join('\n', service.Log(status.ProfileId, status.RunId).TakeLast(12)));
            if (watch.Elapsed > TimeSpan.FromSeconds(startupSeconds + 5)) throw new TimeoutException("Hosting plan fixture did not become ready.");
            await Task.Delay(20);
        }
    }

    [Theory]
    [InlineData("file-count")]
    [InlineData("file-bytes")]
    [InlineData("file-name")]
    [InlineData("argument-count")]
    [InlineData("argument-bytes")]
    [InlineData("environment-count")]
    [InlineData("environment-bytes")]
    [InlineData("environment-key")]
    public async Task AdapterInputReachesTheChildWithoutArbitraryMetadataCeilings(string scenario)
    {
        var plan = Plan();
        plan = scenario switch
        {
            "file-count" => plan with { Files = Enumerable.Range(0, 33).Select(i => new LocalServerFile($"config-{i}.yaml", $"index: {i}\n")).ToArray() },
            "file-bytes" => plan with { Files = [new("large.yaml", "# " + new string('x', 2 * 1024 * 1024))] },
            "file-name" => plan with { Files = [new(new string('x', 120) + ".yaml", "configuration\n")] },
            "argument-count" => plan with { Arguments = [.. Enumerable.Range(0, 100).Select(i => $"argument-{i}"), .. plan.Arguments] },
            "argument-bytes" => plan with { Arguments = [new string('x', 19000), .. plan.Arguments] },
            "environment-count" => plan with { Environment = Enumerable.Range(0, 100).ToDictionary(i => $"FT_HOSTING_TEST_{i}", i => $"value-{i}") },
            "environment-bytes" => plan with { Environment = new Dictionary<string, string> { ["FT_HOSTING_TEST_LARGE"] = new string('x', 19000) } },
            "environment-key" => plan with { Environment = new Dictionary<string, string> { ["FT_HOSTING_TEST_" + new string('X', 150)] = "long-key-value" } },
            _ => throw new ArgumentException("Unknown fixture scenario.", nameof(scenario)),
        };
        await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var run = await service.StartAsync(Profile, Game, new Module(plan));
        await WaitReady(service, plan.StartupSeconds);
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Package, "hosting-input.json")));
        Assert.Equal(plan.Arguments, input.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray());
        foreach (var (key, value) in plan.Environment)
            Assert.Equal(value, input.RootElement.GetProperty("environment").GetProperty(key).GetString());
        foreach (var file in plan.Files)
            Assert.Equal(file.Text, await File.ReadAllTextAsync(Path.Combine(_root, "runs", Profile.Id, file.Name)));
        Assert.DoesNotContain(service.Log(Profile.Id, run.RunId), line => line.Contains("long-key-value", StringComparison.Ordinal));
        var stopped = await service.StopAsync(Profile.Id, run.RunId);
        Assert.Equal("stopped", stopped.State);
        Assert.False(stopped.ForcedStop);
        Assert.Equal("stop", await File.ReadAllTextAsync(Path.Combine(Package, "graceful-stop")));
    }

    [Theory]
    [InlineData("file-traversal")]
    [InlineData("file-device")]
    [InlineData("file-log")]
    [InlineData("file-duplicate")]
    [InlineData("file-null")]
    [InlineData("argument-null")]
    [InlineData("argument-nul")]
    [InlineData("environment-nul")]
    [InlineData("environment-null")]
    [InlineData("environment-name")]
    [InlineData("environment-duplicate")]
    public async Task InvalidAdapterInputCannotWriteFilesOrRetainStartupLeases(string scenario)
    {
        var plan = Plan();
        var invalid = scenario switch
        {
            "file-traversal" => plan with { Files = [new("../outside.yaml", "changed")] },
            "file-device" => plan with { Files = [new("CON.yaml", "changed")] },
            "file-log" => plan with { Files = [new("server.log", "changed")] },
            "file-duplicate" => plan with { Files = [new("one.yaml", "first"), new("ONE.yaml", "second")] },
            "file-null" => plan with { Files = [new("one.yaml", null!)] },
            "argument-null" => plan with { Arguments = [null!] },
            "argument-nul" => plan with { Arguments = ["invalid\0argument"] },
            "environment-nul" => plan with { Environment = new Dictionary<string, string> { ["FT_HOSTING_TEST_INVALID"] = "value\0suffix" } },
            "environment-null" => plan with { Environment = new Dictionary<string, string> { ["FT_HOSTING_TEST_INVALID"] = null! } },
            "environment-name" => plan with { Environment = new Dictionary<string, string> { ["invalid=name"] = "value" } },
            "environment-duplicate" => plan with { Environment = new Dictionary<string, string> { ["FT_HOSTING_TEST_CASE"] = "first", ["ft_hosting_test_case"] = "second" } },
            _ => throw new ArgumentException("Unknown fixture scenario.", nameof(scenario)),
        };
        await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var error = await Record.ExceptionAsync(() => service.StartAsync(Profile, Game, new Module(invalid)));
        Assert.True(error is InvalidOperationException or InvalidDataException, error?.ToString() ?? "Invalid plan was accepted.");
        Assert.Empty(service.List());
        Assert.False(File.Exists(Path.Combine(_root, "runs", "outside.yaml")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "runs", Profile.Id)));
        var run = await service.StartAsync(Profile, Game, new Module(plan));
        await WaitReady(service, plan.StartupSeconds);
        Assert.Equal("stopped", (await service.StopAsync(Profile.Id, run.RunId)).State);
    }

    [Fact]
    public void ProfilesAndSaveDirectoriesHaveNoFixedCountCeiling()
    {
        _ = Plan();
        var profiles = new LocalServerProfiles(Path.Combine(_root, "profiles"));
        var data = new LocalServerData(Path.Combine(_root, "data"));
        for (var index = 0; index < 65; index++)
        {
            var profile = Profile with { Id = $"profile-{index}" };
            profiles.Save(profile);
            using var lease = data.Acquire(profile);
            File.WriteAllText(Path.Combine(lease.DirectoryPath, "save.dat"), $"save-{index}");
        }
        var reopened = new LocalServerProfiles(Path.Combine(_root, "profiles"));
        Assert.Equal(65, reopened.List().Count);
        foreach (var profile in reopened.List())
        {
            using var lease = data.Acquire(profile);
            Assert.Equal("save-" + profile.Id[8..], File.ReadAllText(Path.Combine(lease.DirectoryPath, "save.dat")));
        }
    }

    [Fact]
    public async Task IndependentPackagesCanRunTogetherWithoutTheFormerTwoProcessCeiling()
    {
        // Capacity acceptance uses the adapter's normal readiness budget for parallel cold starts.
        var plan = Plan();
        await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        for (var index = 0; index < 3; index++)
        {
            var package = Directory.CreateDirectory(Path.Combine(_root, $"package-{index}")).FullName;
            foreach (var file in Directory.EnumerateFiles(Package)) File.Copy(file, Path.Combine(package, Path.GetFileName(file)));
            var profile = Profile with { Id = $"profile-{index}", PackagePath = package };
            await service.StartAsync(profile, Game, new Module(plan with { Executable = Path.Combine(package, Path.GetFileName(plan.Executable)) }));
        }
        var watch = Stopwatch.StartNew();
        while (service.List().Any(status => status.State != "ready"))
        {
            var failed = service.List().Where(status => status.State == "failed").ToArray();
            Assert.True(failed.Length == 0, string.Join('\n', failed.Select(status =>
                $"{status.ProfileId}: {status.Failure}, exit={status.ExitCode}, forced={status.ForcedStop}\n"
                + string.Join('\n', service.Log(status.ProfileId, status.RunId).TakeLast(12)))));
            if (watch.Elapsed > TimeSpan.FromSeconds(plan.StartupSeconds + 5)) throw new TimeoutException("Concurrent hosting plans did not become ready.");
            await Task.Delay(20);
        }
        var ready = service.List();
        Assert.Equal(3, ready.Count);
        Assert.Equal(3, ready.Select(status => status.ProcessId).Distinct().Count());
        Assert.All(ready, status => Assert.NotNull(service.Destination(status.ProfileId, status.RunId)));
        await service.StopAllAsync();
        Assert.All(service.List(), status => { Assert.Equal("stopped", status.State); Assert.False(status.ForcedStop); });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
