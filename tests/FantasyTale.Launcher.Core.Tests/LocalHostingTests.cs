using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.DirectoryService;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("fixed local hosting ports")]
public sealed class LocalHostingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-hosting-").FullName;
    private string Package => Path.Combine(_root, "package");
    // The fixture package has no SQLite provider, so these profiles name the package's own storage explicitly.
    private LocalServerProfile Profile => new("my-room", "bubblefighter", "cn-406", "My local server", Package, "private", "127.0.0.1", 17230, 18090, 18094, false) { StorageMode = "package" };
    private static GameDefinition Game()
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "games", "BubbleFighter", "builds"))) root = Directory.GetParent(root)!.FullName;
        return GameCatalog.Load(Path.Combine(root, "games")).Single(g => g.Id == "bubblefighter") with { LocalHostingEnabled = true, RoomsEnabled = true };
    }
    private void PreparePackage()
    {
        Directory.CreateDirectory(Package);
        Directory.CreateDirectory(Path.Combine(Package, "client-data", Profile.Build, "Data"));
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "test-client")))
            File.Copy(file, Path.Combine(Package, Path.GetFileName(file)), true);
    }
    private static readonly int StartupSeconds = new LocalServerPlan("", [], [], "", "").StartupSeconds;
    private sealed class Probe(string mode = "normal", Action<LocalServerContext>? inspect = null, int? startupSeconds = null) : ILocalServerModule
    {
        public LocalServerPlan CreateServerPlan(LocalServerContext context)
        {
            inspect?.Invoke(context);
            return new(
            Path.Combine(context.Profile.PackagePath, "FantasyTale.Launcher.TestClient.exe"), ["--hosting-probe", mode], [],
            "http://127.0.0.1:18090/", "http://127.0.0.1:18094/") { StartupSeconds = startupSeconds ?? StartupSeconds, ShutdownSeconds = 1 };
        }
    }
    private static async Task<LocalServerStatus> UntilAsync(LocalServerService service, Func<LocalServerStatus, bool> predicate, string? runId = null)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var state = service.List().Single(status => runId is null || status.RunId == runId); if (predicate(state)) return state;
            if (state.State == "failed")
                throw new InvalidOperationException($"Hosting fixture failed before the expected state: {state.Failure}, pid={state.ProcessId}, exit={state.ExitCode}, forced={state.ForcedStop}.\n"
                    + string.Join('\n', service.Log(state.ProfileId, state.RunId).TakeLast(12)));
            if (watch.Elapsed > TimeSpan.FromSeconds(StartupSeconds + 5)) throw new TimeoutException($"Hosting fixture remained {state.State}, pid={state.ProcessId}.\n"
                + string.Join('\n', service.Log(state.ProfileId, state.RunId).TakeLast(12)));
            await Task.Delay(20);
        }
    }

    [Fact]
    public void ProfilesRoundTripAndRemovalPreservesThePackage()
    {
        PreparePackage(); var store = new LocalServerProfiles(Path.Combine(_root, "profiles"));
        store.Save(Profile); Assert.Equal(Profile, Assert.Single(store.List()));
        var lan = Profile with { Mode = "lan", AdvertisedHost = "192.168.1.15", StorageMode = "local" };
        store.Save(lan); Assert.Equal(lan, Assert.Single(store.List()));
        store.Save(lan with { Mode = "private", AdvertisedHost = "127.0.0.1" });
        Assert.Equal("local", Assert.Single(store.List()).StorageMode);
        Assert.Throws<ArgumentException>(() => store.Save(lan with { AdvertisedHost = "0.0.0.0" }));
        Assert.Throws<ArgumentException>(() => store.Save(lan with { AdvertisedHost = "127.0.0.1" }));
        Assert.Throws<ArgumentException>(() => store.Save(lan with { AdvertisedHost = "8.8.8.8" }));
        store.Save(Profile with { Name = "Room", Mode = "room", AdvertisedHost = "192.168.1.15" });
        Assert.Equal("room", Assert.Single(store.List()).Mode);
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { ApiPort = Profile.GamePort }));
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { Mode = "room" }));
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { Mode = "private", AdvertisedHost = "example.org" }));
        store.Delete(Profile.Id); Assert.Empty(store.List()); Assert.True(Directory.Exists(Package));
    }

    [Fact]
    public void UnsetStorageSavesLocallyAndConflictsAreRejected()
    {
        PreparePackage(); var store = new LocalServerProfiles(Path.Combine(_root, "profiles"));
        // Single play and rooms save to SQLite unless the player picks another storage.
        store.Save(Profile with { StorageMode = null }); Assert.Equal("local", Assert.Single(store.List()).EffectiveStorageMode);
        store.Save(Profile with { StorageMode = null, TemporaryData = true }); Assert.Equal("temporary", Assert.Single(store.List()).EffectiveStorageMode);
        foreach (var mode in new[] { "package", "local", "temporary" })
        {
            var profile = Profile with { StorageMode = mode, TemporaryData = mode == "temporary" };
            store.Save(profile); Assert.Equal(profile, Assert.Single(store.List()));
        }
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { StorageMode = "local", TemporaryData = true }));
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { StorageMode = "unknown" }));
        Assert.Throws<ArgumentException>(() => store.Save(Profile with { StorageMode = "" }));
    }

    [Fact]
    public async Task ReadinessGracefulStopAndStaleStopAreScopedToAnExactInvocation()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var first = await service.StartAsync(Profile, Game(), new Probe());
        await UntilAsync(service, s => s.State == "ready"); Assert.NotNull(service.Destination(Profile.Id));
        Assert.NotNull(service.Destination(Profile.Id, first.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(Profile, Game(), new Probe()));
        var stopped = await service.StopAsync(Profile.Id, first.RunId);
        Assert.Equal("stopped", stopped.State); Assert.False(stopped.ForcedStop); Assert.Equal(0, stopped.ExitCode);
        Assert.Equal("stop", File.ReadAllText(Path.Combine(Package, "graceful-stop"))); Assert.Null(service.Destination(Profile.Id));
        Assert.Null(service.Destination(Profile.Id, first.RunId));
        var second = await service.StartAsync(Profile, Game(), new Probe());
        Assert.NotEqual(first.RunId, second.RunId);
        await UntilAsync(service, s => s.State == "ready");
        Assert.Null(service.Destination(Profile.Id, first.RunId));
        Assert.NotNull(service.Destination(Profile.Id, second.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(Profile.Id, first.RunId));
        await service.StopAsync(Profile.Id, second.RunId);
    }

    [Fact]
    public async Task StoppingOwnedProcessesKeepsTheServiceUsableUntilApplicationDisposal()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var first = await service.StartAsync(Profile, Game(), new Probe());
        await UntilAsync(service, s => s.State == "ready");
        await service.StopAllAsync();
        Assert.Equal("stopped", Assert.Single(service.List()).State);
        var second = await service.StartAsync(Profile, Game(), new Probe());
        Assert.NotEqual(first.RunId, second.RunId);
        await UntilAsync(service, s => s.State == "ready");
        await service.StopAllAsync();
        await service.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(Profile, Game(), new Probe()));
    }

    [Fact]
    public async Task StableProfileDataSurvivesRestartAndExcludesAnotherPackageProcess()
    {
        PreparePackage(); var dataRoot = Path.Combine(_root, "saved-data");
        var otherPackage = Path.Combine(_root, "other-package"); Directory.CreateDirectory(otherPackage);
        foreach (var file in Directory.GetFiles(Package)) File.Copy(file, Path.Combine(otherPackage, Path.GetFileName(file)));
        string? savedPath = null;
        var probe = new Probe(inspect: context =>
        {
            Assert.NotNull(context.DataDirectory); Assert.NotEqual(context.SessionDirectory, context.DataDirectory);
            savedPath = context.DataDirectory;
        });
        await using (var first = new LocalServerService(Path.Combine(_root, "runs-one"), dataRoot))
        {
            var run = await first.StartAsync(Profile, Game(), probe); await UntilAsync(first, s => s.State == "ready");
            File.WriteAllText(Path.Combine(savedPath!, "save.dat"), "persistent fixture data");
            await using var second = new LocalServerService(Path.Combine(_root, "runs-two"), dataRoot);
            await Assert.ThrowsAsync<IOException>(() => second.StartAsync(Profile with { PackagePath = otherPackage }, Game(), new Probe()));
            await first.StopAsync(Profile.Id, run.RunId);
        }
        await using (var restarted = new LocalServerService(Path.Combine(_root, "runs-three"), dataRoot))
        {
            var previousPath = savedPath;
            var run = await restarted.StartAsync(Profile with { PackagePath = otherPackage, Name = "Renamed" }, Game(), probe);
            await UntilAsync(restarted, s => s.State == "ready");
            Assert.Equal(previousPath, savedPath); Assert.Equal("persistent fixture data", File.ReadAllText(Path.Combine(savedPath!, "save.dat")));
            await restarted.StopAsync(Profile.Id, run.RunId);
        }
        var profiles = new LocalServerProfiles(Path.Combine(_root, "profiles")); profiles.Save(Profile); profiles.Delete(Profile.Id);
        Assert.True(File.Exists(Path.Combine(savedPath!, "save.dat")));
        var data = new LocalServerData(dataRoot);
        Assert.Throws<InvalidDataException>(() => data.Acquire(Profile with { Game = "different" }));
        Assert.Throws<InvalidDataException>(() => data.Acquire(Profile with { Build = "other-1" }));
        using var lease = data.Acquire(Profile); Assert.Equal(savedPath, lease.DirectoryPath);
    }

    [Fact]
    public void SaveOwnershipAndOverlappingRootsPreserveExistingFilesWithoutProfileCountLimits()
    {
        var dataRoot = Path.Combine(_root, "data"); var data = new LocalServerData(dataRoot); string path;
        using (var lease = data.Acquire(Profile)) path = lease.DirectoryPath;
        File.WriteAllText(Path.Combine(path, "save.dat"), "preserve"); File.Delete(Path.Combine(path, "identity.json"));
        Assert.Throws<InvalidDataException>(() => data.Acquire(Profile));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(path, "save.dat")));
        File.WriteAllText(Path.Combine(path, "identity.json"), "invalid");
        Assert.Throws<JsonException>(() => data.Acquire(Profile));
        Assert.Equal("invalid", File.ReadAllText(Path.Combine(path, "identity.json")));
        Assert.Throws<InvalidOperationException>(() => data.ValidatePackage(dataRoot));
        Assert.Throws<InvalidOperationException>(() => data.ValidatePackage(Path.Combine(dataRoot, "package")));
        Assert.Throws<ArgumentException>(() => new LocalServerService(Path.Combine(_root, "runs"), Path.Combine(_root, "runs", "data")));
        for (var i = 0; i < 31; i++) Directory.CreateDirectory(Path.Combine(dataRoot, "old-" + i));
        using var next = data.Acquire(Profile with { Id = "new-profile" });
        Assert.True(File.Exists(Path.Combine(next.DirectoryPath, "identity.json")));
    }

    [Theory]
    [InlineData("crash", "startup-failed")]
    [InlineData("timeout", "startup-timeout")]
    [InlineData("stderr", "startup-timeout")]
    public async Task StartupFailureAndStderrCannotPublishAReadyDestination(string mode, string failure)
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        await service.StartAsync(Profile, Game(), new Probe(mode, startupSeconds: 3));
        var state = await UntilAsync(service, s => s.State == "failed");
        Assert.Equal(failure, state.Failure); Assert.Null(service.Destination(Profile.Id));
    }

    [Fact]
    public async Task PackageLeaseIsExclusiveAcrossServiceInstancesAndReusableAfterShutdown()
    {
        PreparePackage(); await using var a = new LocalServerService(Path.Combine(_root, "a"));
        await using var b = new LocalServerService(Path.Combine(_root, "b"));
        var first = await a.StartAsync(Profile, Game(), new Probe());
        await Assert.ThrowsAsync<IOException>(() => b.StartAsync(Profile with { Id = "other" }, Game(), new Probe()));
        await a.StopAsync(Profile.Id, first.RunId);
        var second = await b.StartAsync(Profile, Game(), new Probe()); await b.StopAsync(Profile.Id, second.RunId);
    }

    [Fact]
    public async Task GraceDeadlineTerminatesOnlyTheOwnedProcessAndOutputRemainsBounded()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var run = await service.StartAsync(Profile, Game(), new Probe("hang"));
        await UntilAsync(service, s => s.State == "ready");
        var stopped = await service.StopAsync(Profile.Id, run.RunId); Assert.True(stopped.ForcedStop);
        run = await service.StartAsync(Profile, Game(), new Probe("flood"));
        await UntilAsync(service, s => s.State == "ready");
        Assert.InRange(service.Log(Profile.Id, run.RunId).Count, 1, 128);
        await service.StopAsync(Profile.Id, run.RunId);
        Assert.InRange(new FileInfo(run.LogPath).Length, 1, 2 * 1024 * 1024);
        Assert.InRange(new FileInfo(run.LogPath + ".1").Length, 1, 2 * 1024 * 1024);
    }

    [Fact]
    public async Task ParentExitClosesOwnedDescendants()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var run = await service.StartAsync(Profile, Game(), new Probe("descendant"));
        await UntilAsync(service, s => s.State == "ready");
        var pid = int.Parse(File.ReadAllText(Path.Combine(Package, "descendant.pid")), System.Globalization.CultureInfo.InvariantCulture);
        using var child = Process.GetProcessById(pid);
        var stopped = await service.StopAsync(Profile.Id, run.RunId);
        Assert.True(child.HasExited); Assert.True(stopped.ForcedStop);
        using (var packageLease = new FileStream(Path.Combine(Package, ".fantasytale-host.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.True(packageLease.CanWrite);
        var restarted = await service.StartAsync(Profile, Game(), new Probe());
        await UntilAsync(service, s => s.State == "ready");
        await service.StopAsync(Profile.Id, restarted.RunId);
    }

    [Fact]
    public async Task UnexpectedParentExitDrainsItsDescendantsBeforeReportingFailure()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        var run = await service.StartAsync(Profile, Game(), new Probe("descendant-exit"));
        await UntilAsync(service, s => s.State == "failed");
        var pid = int.Parse(File.ReadAllText(Path.Combine(Package, "descendant.pid")), System.Globalization.CultureInfo.InvariantCulture);
        try { using var child = Process.GetProcessById(pid); Assert.True(child.HasExited); }
        catch (ArgumentException) { /* The exited process may already be absent from the process table. */ }
        var stopped = await service.StopAsync(Profile.Id, run.RunId);
        Assert.True(stopped.ForcedStop);
        Assert.True(stopped.Failure is "startup-failed" or "unexpected-exit", stopped.Failure);
        var restarted = await service.StartAsync(Profile, Game(), new Probe());
        await UntilAsync(service, s => s.State == "ready");
        await service.StopAsync(Profile.Id, restarted.RunId);
    }

    [Theory]
    [InlineData("cn-279")]
    [InlineData("cn-324")]
    [InlineData("cn-404")]
    [InlineData("cn-406")]
    [InlineData("cn-706")]
    [InlineData("cn-717")]
    [InlineData("cn-774")]
    [InlineData("id-1809")]
    [InlineData("id-400")]
    [InlineData("kr-1256")]
    [InlineData("kr-812")]
    [InlineData("tw-454")]
    public void AdapterKeepsPrivateListenersOnLoopbackAndNeverChangesPackageProfiles(string buildId)
    {
        var profile = Profile with { Build = buildId };
        Directory.CreateDirectory(Path.Combine(Package, "client-data", buildId, "Data"));
        Directory.CreateDirectory(Path.Combine(Package, "profiles", buildId)); Directory.CreateDirectory(Path.Combine(Package, "packets"));
        var original = Path.Combine(Package, "profiles", buildId, "profile.yaml"); File.WriteAllText(original, "original");
        File.WriteAllText(Path.Combine(Package, "packets", "builds.yaml"), "");
        var game = Game(); var build = game.Builds.Single(b => b.Id == profile.Build); var module = new BubbleFighterModule();
        var plan = module.CreateServerPlan(new(game, build, profile, Path.Combine(_root, "runs")));
        Assert.Contains("launcher_login: none", plan.Files[0].Text, StringComparison.Ordinal);
        Assert.Contains("authentication: open", plan.Files[0].Text, StringComparison.Ordinal);
        if (build.HostingAdapter == "legacy")
        {
            Assert.Contains("${LEGACY_GATE_KEY:-0}", plan.Files[0].Text, StringComparison.Ordinal);
            Assert.Contains("${LEGACY_GATE_SUB_KEY:-0}", plan.Files[0].Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("127.0.0.1:17231", plan.Files[0].Text, StringComparison.Ordinal);
            Assert.DoesNotContain("center_gates:", plan.Files[0].Text, StringComparison.Ordinal);
        }
        var authenticated = module.CreateServerPlan(new(game, build, profile with { RequireAuthentication = true }, Path.Combine(_root, "runs")));
        Assert.Contains("launcher_login: required", authenticated.Files[0].Text, StringComparison.Ordinal);
        Assert.Contains("authentication: ticket", authenticated.Files[0].Text, StringComparison.Ordinal);
        Assert.Equal(buildId, build.ServerProfile);
        Assert.Equal(buildId, plan.Arguments[2]);
        Assert.Contains("--managed-stdio", plan.Arguments); Assert.Contains("127.0.0.1:17230", plan.Files[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("persistence.memory", plan.Files[0].Text, StringComparison.Ordinal);
        var temporary = module.CreateServerPlan(new(game, build, profile with { StorageMode = "temporary", TemporaryData = true }, Path.Combine(_root, "runs")));
        Assert.Contains("persistence.memory", temporary.Files[0].Text, StringComparison.Ordinal); Assert.Equal("original", File.ReadAllText(original));
        var room = module.CreateServerPlan(new(game, build, profile with { Mode = "room", AdvertisedHost = "192.168.1.20" }, Path.Combine(_root, "runs")));
        Assert.Contains("0.0.0.0:17230", room.Files[0].Text, StringComparison.Ordinal); Assert.Contains("192.168.1.20", room.Files[0].Text, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:18094", room.Files[0].Text, StringComparison.Ordinal);
        var lan = module.CreateServerPlan(new(game, build, profile with { Mode = "lan", AdvertisedHost = "192.168.1.20" }, Path.Combine(_root, "runs")));
        Assert.Contains("192.168.1.20:17230", lan.Files[0].Text, StringComparison.Ordinal);
        Assert.Contains("192.168.1.20:18090", lan.Files[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", lan.Files[0].Text, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:18094", lan.Files[0].Text, StringComparison.Ordinal);
        Assert.Equal("http://192.168.1.20:18090/", lan.ApiUrl);
        Assert.Equal("http://127.0.0.1:18094/", lan.AdminUrl);
        Assert.Throws<InvalidOperationException>(() => module.CreateServerPlan(new(game, build with { HostingAdapter = "" }, Profile, _root)));
    }

    [Fact]
    public void AdapterReadsTheRegisteredClientWithoutCopyingAndFailsBeforeStartingWhenMissing()
    {
        Directory.CreateDirectory(Path.Combine(Package, "profiles", Profile.Build));
        Directory.CreateDirectory(Path.Combine(Package, "packets"));
        File.WriteAllText(Path.Combine(Package, "profiles", Profile.Build, "profile.yaml"), "original");
        File.WriteAllText(Path.Combine(Package, "packets", "builds.yaml"), "");
        var game = Game(); var build = game.Builds.Single(build => build.Id == Profile.Build);
        var context = new LocalServerContext(game, build, Profile, Path.Combine(_root, "runs"));
        var module = new BubbleFighterModule();
        Assert.Equal("local_client_data_missing", Assert.Throws<InvalidOperationException>(() => module.CreateServerPlan(context)).Message);
        var client = Path.Combine(_root, "installed-client"); Directory.CreateDirectory(Path.Combine(client, "Data"));
        var plan = module.CreateServerPlan(context with { ClientDirectory = client });
        Assert.Contains(client.Replace('\\', '/'), plan.Files[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(client, "Data").Replace('\\', '/'), plan.Files[0].Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Package, "client-data")));
    }

    [Fact]
    public void NewProfilesHaveDistinctAvailablePortsAndPreserveExplicitAuthentication()
    {
        var ports = LocalServerNetwork.AvailablePorts(); Assert.Equal(4, ports.Count); Assert.Equal(4, ports.Distinct().Count());
        PreparePackage(); var store = new LocalServerProfiles(Path.Combine(_root, "profiles"));
        var profile = Profile with { RequireAuthentication = true, ClientInstanceId = "client-1", RelayPort = ports[0] };
        store.Save(profile); Assert.Equal(profile, Assert.Single(store.List()));
        Assert.Throws<ArgumentException>(() => store.Save(profile with { RelayPort = profile.GamePort }));
    }

    [Theory]
    [InlineData("10.0.0.2", true)]
    [InlineData("172.16.1.2", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.2.3", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    public void LanScopesNeverUseLoopbackWildcardOrPublicInterfaces(string address, bool accepted) =>
        Assert.Equal(accepted, LocalServerNetwork.IsLan(IPAddress.Parse(address)));

    [Fact]
    public async Task PublicRoomsStayUnavailableWhenOnlyLocalPlayIsEnabled()
    {
        PreparePackage(); await using var service = new LocalServerService(Path.Combine(_root, "runs"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            Profile with { Mode = "room", AdvertisedHost = "192.168.1.2" }, Game() with { RoomsEnabled = false }, new Probe()));
        Assert.Empty(service.List());
        var run = await service.StartAsync(Profile, Game() with { RoomsEnabled = false }, new Probe());
        await UntilAsync(service, s => s.State == "ready");
        Assert.Equal("room", service.Destination(Profile.Id, run.RunId)!.Kind);
        await service.StopAsync(Profile.Id, run.RunId);
    }

    [Fact]
    public void LocalStorageRequiresCompleteProviderAndAnOwnedSavePath()
    {
        Directory.CreateDirectory(Path.Combine(Package, "profiles", "cn-406"));
        Directory.CreateDirectory(Path.Combine(Package, "packets"));
        File.WriteAllText(Path.Combine(Package, "profiles", "cn-406", "profile.yaml"), "original");
        File.WriteAllText(Path.Combine(Package, "packets", "builds.yaml"), "");
        Directory.CreateDirectory(Path.Combine(Package, "client-data", Profile.Build, "Data"));
        var game = Game(); var build = game.Builds.Single(b => b.Id == Profile.Build);
        var module = new BubbleFighterModule(); var profile = Profile with { StorageMode = "local" };
        var context = new LocalServerContext(game, build, profile, Path.Combine(_root, "runs"));
        Assert.Throws<ArgumentException>(() => module.CreateServerPlan(context));
        context = context with { DataDirectory = Path.Combine(_root, "saved-data") };
        Assert.Throws<InvalidOperationException>(() => module.CreateServerPlan(context));
        var provider = Path.Combine(Package, "plugins", "BubbleFighter.Fantasy.Persistence.Sqlite");
        Directory.CreateDirectory(provider);
        File.WriteAllText(Path.Combine(provider, "BubbleFighter.Fantasy.Persistence.Sqlite.dll"), "fixture");
        Assert.Throws<InvalidOperationException>(() => module.CreateServerPlan(context));
        File.WriteAllText(Path.Combine(provider, "e_sqlite3.dll"), "fixture");
        var plan = module.CreateServerPlan(context);
        Assert.Contains(Path.Combine(context.DataDirectory!, "save.sqlite").Replace('\\', '/'), plan.Files[0].Text, StringComparison.Ordinal);
        Assert.Equal("unused-local-provider", plan.Environment["BFF_MYSQL_PASSWORD"]);
        Assert.Throws<ArgumentException>(() => module.CreateServerPlan(context with { DataDirectory = "relative" }));
        Assert.Throws<ArgumentException>(() => module.CreateServerPlan(context with { Profile = profile with { TemporaryData = true } }));
        Assert.Equal("original", File.ReadAllText(Path.Combine(Package, "profiles", "cn-406", "profile.yaml")));
    }

    [Theory]
    [InlineData("private", "room")]
    [InlineData("room", "private")]
    public async Task BridgePublishesReadyLocalServersAndFencesSettingsRestartAndRemoval(string startedMode, string savedMode)
    {
        PreparePackage(); Directory.CreateDirectory(Path.Combine(Package, "profiles", "cn-406")); Directory.CreateDirectory(Path.Combine(Package, "packets"));
        File.WriteAllText(Path.Combine(Package, "profiles", "cn-406", "profile.yaml"), "fixture");
        File.WriteAllText(Path.Combine(Package, "packets", "builds.yaml"), "fixture");
        File.WriteAllText(Path.Combine(Package, "hosting-probe"), "normal");
        File.Copy(Path.Combine(Package, "FantasyTale.Launcher.TestClient.exe"), Path.Combine(Package, "BubbleFighter.Fantasy.Host.exe"));
        var game = Game();
        var fixtureFolder = Path.Combine(_root, "games", "BubbleFighter");
        Directory.CreateDirectory(Path.Combine(fixtureFolder, "builds"));
        File.WriteAllText(Path.Combine(fixtureFolder, "game.toml"), "schema=1\n[game]\nid='bubblefighter'\nstatus='enabled'\nmodule='FantasyTale.Launcher.Games.BubbleFighter.dll'\nhook_host='external'\nname={en='Fixture'}\nrooms_enabled=true\nlocal_hosting_enabled=true\n");
        File.Copy(Path.Combine(game.Folder, "builds", "cn-406.toml"), Path.Combine(fixtureFolder, "builds", "cn-406.toml"));
        await using var backend = new LauncherBackend(Path.GetDirectoryName(fixtureFolder)!, Path.Combine(_root, "data"));
        var replies = new List<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge); long sequence = 0;
        async Task<JsonNode?> Call(string method, object? arguments = null, bool failure = false)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }, JsonSerializerOptions.Web), default);
            var reply = replies.Single(r => r["id"]!.GetValue<long>() == id);
            if (failure) Assert.NotNull(reply["error"]); else Assert.Null(reply["error"]);
            return reply["result"];
        }
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        var initialProfile = Profile with { Mode = startedMode, AdvertisedHost = startedMode == "private" ? "127.0.0.1" : "192.0.2.10" };
        await Call("hosting.save", initialProfile);
        await Call("hosting.destination", new { id = Profile.Id, runId = "not-started" }, failure: true);
        backend.SetUpdateStartup(true); await Call("hosting.start", new { id = Profile.Id }, failure: true); backend.SetUpdateStartup(false);
        var start = await Call("hosting.start", new { id = Profile.Id }); var runId = start!["runId"]!.GetValue<string>();
        var watch = Stopwatch.StartNew();
        while ((await Call("hosting.list"))!["sessions"]![0]!["state"]!.GetValue<string>() != "ready")
        {
            var status = (await Call("hosting.list"))!["sessions"]!.AsArray().Single(session => session!["runId"]!.GetValue<string>() == runId)!;
            if (status["state"]!.GetValue<string>() == "failed") throw new InvalidOperationException("Bridge hosting failed: " + status.ToJsonString());
            if (watch.Elapsed > TimeSpan.FromSeconds(StartupSeconds + 5)) throw new TimeoutException("Bridge hosting did not become ready: " + status.ToJsonString());
            await Task.Delay(25);
        }
        Assert.True(backend.HasRunningClients);
        var destination = await Call("hosting.destination", new { id = Profile.Id, runId });
        Assert.Equal("local-" + Profile.Id, destination!["id"]!.GetValue<string>());
        await Call("hosting.destination", new { id = Profile.Id, runId = "old-run" }, failure: true);
        await Call("hosting.destination", new { id = Profile.Id }, failure: true);
        var destinations = (await Call("servers.list"))!.AsArray();
        Assert.Contains(destinations, s => s!["id"]!.GetValue<string>() == "local-" + Profile.Id && s["transient"]!.GetValue<bool>() && s["localOnly"]!.GetValue<bool>() == (startedMode == "private"));
        Assert.All(destinations.Where(s => !s!["id"]!.GetValue<string>().StartsWith("local-", StringComparison.Ordinal)), s => Assert.False(s!["transient"]!.GetValue<bool>()));
        await Call("hosting.save", Profile with { Name = "Updated while running", Mode = savedMode, AdvertisedHost = savedMode == "private" ? "127.0.0.1" : "192.0.2.10" });
        Assert.Equal(runId, (await Call("hosting.list"))!["sessions"]![0]!["runId"]!.GetValue<string>());
        await Call("hosting.delete", new { id = Profile.Id }, failure: true);
        await Call("servers.save", new { id = "local-" + Profile.Id, game = "bubblefighter", name = "replacement" }, failure: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.PrepareUpdateRestartAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.PrepareUpdateRestartAsync(default, new string('a', 64), stopRunningClients: true));
        Assert.True(backend.HasRunningClients);
        await Call("hosting.stop", new { id = Profile.Id, runId = "old-run" }, failure: true);
        await Call("hosting.releasePrivate", new { keepServerId = "other" });
        Assert.Equal(startedMode != "private", backend.HasRunningClients);
        if (startedMode != "private") await Call("hosting.stop", new { id = Profile.Id, runId });
        Assert.False(backend.HasRunningClients);
        await Call("hosting.destination", new { id = Profile.Id, runId }, failure: true);
        Assert.Contains((await Call("servers.list"))!.AsArray(), s => s!["id"]!.GetValue<string>() == "local-" + Profile.Id && s["localOnly"]!.GetValue<bool>() == (savedMode == "private"));
        await Call("hosting.delete", new { id = Profile.Id }); Assert.Empty((await Call("hosting.list"))!["profiles"]!.AsArray());
    }

    [Fact]
    public async Task PublishedRoomRenewsInTheBackendAndWithdrawsWhenItsOwnedProcessStops()
    {
        PreparePackage(); await using var hosting = new LocalServerService(Path.Combine(_root, "runs"));
        await using var directory = await DirectoryHttp.StartAsync(new() { Listen = "http://127.0.0.1:0", DataPath = Path.Combine(_root, "directory"), AdminToken = Guid.NewGuid().ToString("N"), RoomLeaseSeconds = 30 });
        using var client = new DirectoryClient(); await using var publisher = new HostedRoomPublisher(hosting, client);
        var source = new DirectorySource("test", "Test", directory.Addresses.Single(), true, false);
        var profile = Profile with { Mode = "room", AdvertisedHost = "192.0.2.10" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(profile, "missing", source, ""));
        var first = await hosting.StartAsync(profile, Game(), new Probe()); await UntilAsync(hosting, s => s.State == "ready");
        var published = await publisher.PublishAsync(profile, first.RunId, source, "Friends"); Assert.True(publisher.UsesSource(source.Id));
        Assert.Single((await client.ListAsync(source)).Entries);
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(profile, first.RunId, source, "Duplicate"));
        var watch = Stopwatch.StartNew();
        while (publisher.List().Single().ExpiresAt <= published.ExpiresAt)
        { if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Backend heartbeat did not renew its room."); await Task.Delay(100); }
        using (var child = Process.GetProcessById(first.ProcessId)) child.Kill();
        await UntilAsync(hosting, s => s.State == "failed");
        watch.Restart();
        while (publisher.List().Single().State != "withdrawn")
        { if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Stopped server remained published."); await Task.Delay(50); }
        Assert.Empty((await client.ListAsync(source)).Entries); Assert.False(publisher.UsesSource(source.Id));
        var second = await hosting.StartAsync(profile, Game(), new Probe()); await UntilAsync(hosting, s => s.State == "ready");
        var next = await publisher.PublishAsync(profile, second.RunId, source, ""); Assert.NotEqual(published.RoomId, next.RoomId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.WithdrawAsync(profile.Id, first.RunId));
        await publisher.WithdrawAsync(profile.Id, second.RunId); Assert.Empty((await client.ListAsync(source)).Entries);
        await hosting.StopAsync(profile.Id, second.RunId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoomShutdownSharesOneDeadlineAcrossUnavailableOrMalformedDirectories(bool malformed)
    {
        PreparePackage();
        var secondPackage = Directory.CreateDirectory(Path.Combine(_root, "second-package")).FullName;
        foreach (var file in Directory.GetFiles(Package)) File.Copy(file, Path.Combine(secondPackage, Path.GetFileName(file)));
        await using var hosting = new LocalServerService(Path.Combine(_root, "shutdown-runs"));
        using var handler = new UnavailableWithdrawalHandler(malformed);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new DirectoryClient(http);
        await using var publisher = new HostedRoomPublisher(hosting, client);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        var firstProfile = Profile with { Mode = "room", AdvertisedHost = "192.0.2.10" };
        var first = await hosting.StartAsync(firstProfile, Game(), new Probe());
        await UntilAsync(hosting, status => status.State == "ready");
        var secondProfile = firstProfile with { Id = "second-room", PackagePath = secondPackage, GamePort = 17231, ApiPort = 18091, AdminPort = 18095 };
        var second = await hosting.StartAsync(secondProfile, Game(), new Probe());
        await UntilAsync(hosting, status => status.State == "ready", second.RunId);
        await publisher.PublishAsync(firstProfile, first.RunId, source, "");
        await publisher.PublishAsync(secondProfile, second.RunId, source, "");
        var expiries = publisher.List().ToDictionary(status => status.RoomId, status => status.ExpiresAt);
        var watch = Stopwatch.StartNew();
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.Equal(2, handler.Withdrawals);
        Assert.All(publisher.List(), status => Assert.Equal("withdrawing", status.State));
        Assert.All(publisher.List(), status => Assert.Equal(expiries[status.RoomId], status.ExpiresAt));
        Assert.Equal(2, hosting.List().Count(status => status.State == "ready"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledOrMalformedRoomRenewalDoesNotDelayOtherRenewalsOrWithdrawal(bool malformed)
    {
        PreparePackage();
        var secondPackage = Directory.CreateDirectory(Path.Combine(_root, "renewal-package")).FullName;
        foreach (var file in Directory.GetFiles(Package)) File.Copy(file, Path.Combine(secondPackage, Path.GetFileName(file)));
        await using var hosting = new LocalServerService(Path.Combine(_root, "renewal-runs"));
        using var handler = new StalledRenewalHandler(malformed);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new DirectoryClient(http);
        await using var publisher = new HostedRoomPublisher(hosting, client);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        var firstProfile = Profile with { Mode = "room", AdvertisedHost = "192.0.2.10" };
        var first = await hosting.StartAsync(firstProfile, Game(), new Probe());
        await UntilAsync(hosting, status => status.State == "ready");
        var secondProfile = firstProfile with { Id = "second-room", PackagePath = secondPackage, GamePort = 17231, ApiPort = 18091, AdminPort = 18095 };
        var second = await hosting.StartAsync(secondProfile, Game(), new Probe());
        await UntilAsync(hosting, status => status.State == "ready", second.RunId);
        var firstPublished = await publisher.PublishAsync(firstProfile, first.RunId, source, "");
        var published = await publisher.PublishAsync(secondProfile, second.RunId, source, "");
        await handler.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(malformed, handler.StallEnded.Task.IsCompleted);
        if (malformed)
        {
            var failedRoom = publisher.List().Single(status => status.ProfileId == firstProfile.Id);
            Assert.Equal("reconnecting", failedRoom.State);
            Assert.Equal("Directory connection failed.", failedRoom.Error);
            Assert.Equal(firstPublished.ExpiresAt, failedRoom.ExpiresAt);
        }
        var watch = Stopwatch.StartNew();
        while (publisher.List().Single(status => status.ProfileId == secondProfile.Id).ExpiresAt <= published.ExpiresAt)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(2)) throw new TimeoutException("Healthy room renewal was not published.");
            await Task.Delay(20);
        }
        await hosting.StopAsync(secondProfile.Id, second.RunId);
        await handler.Withdrawn.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(malformed, handler.StallEnded.Task.IsCompleted);
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.StallEnded.Task.IsCompleted);
        Assert.All(publisher.List(), status => Assert.Equal("withdrawn", status.State));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoomStoppedDuringRenewalWithdrawsWithoutRepublishing(bool malformed)
    {
        PreparePackage();
        await using var hosting = new LocalServerService(Path.Combine(_root, "late-renewal-runs"));
        using var handler = new DelayedRenewalHandler(malformed);
        using var http = new HttpClient(handler);
        using var client = new DirectoryClient(http);
        await using var publisher = new HostedRoomPublisher(hosting, client);
        var source = new DirectorySource("test", "Test", "http://directory.test/", true, false);
        var profile = Profile with { Mode = "room", AdvertisedHost = "192.0.2.10" };
        var run = await hosting.StartAsync(profile, Game(), new Probe());
        await UntilAsync(hosting, status => status.State == "ready");
        var published = await publisher.PublishAsync(profile, run.RunId, source, "");
        handler.ReadStatus = () => Assert.Single(publisher.List());
        try
        {
            await handler.Renewing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await hosting.StopAsync(profile.Id, run.RunId);
            handler.ReleaseRenewal.TrySetResult();
            var withdrawing = await handler.Withdrawing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("withdrawing", withdrawing.State);
            Assert.Null(hosting.Destination(profile.Id));
            if (malformed) Assert.Equal(published.ExpiresAt, withdrawing.ExpiresAt);
            else Assert.True(withdrawing.ExpiresAt > published.ExpiresAt);
            handler.ReleaseWithdrawal.TrySetResult();
            var watch = Stopwatch.StartNew();
            while (publisher.List().Single().State != "withdrawn")
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Stopped room was not withdrawn.");
                await Task.Delay(20);
            }
            Assert.False(publisher.UsesSource(source.Id));
            Assert.Equal(1, handler.Renewals);
        }
        finally
        {
            handler.ReleaseRenewal.TrySetResult();
            handler.ReleaseWithdrawal.TrySetResult();
        }
    }

    private sealed class DelayedRenewalHandler(bool malformed) : HttpMessageHandler
    {
        private int _renewals;
        internal int Renewals => Volatile.Read(ref _renewals);
        internal Func<HostedRoomStatus>? ReadStatus;
        internal readonly TaskCompletionSource Renewing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseRenewal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<HostedRoomStatus> Withdrawing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseWithdrawal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Withdrawing.TrySetResult(ReadStatus!());
                await ReleaseWithdrawal.Task.WaitAsync(ct);
                return new(HttpStatusCode.NoContent);
            }
            if (request.RequestUri!.AbsolutePath == "/v1/info")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryServiceInfo(1, "Test", 60, "domain", null)) };
            if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _renewals);
                Renewing.TrySetResult();
                await ReleaseRenewal.Task.WaitAsync(ct);
                if (malformed) return new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryLease("room-1", new string('1', 64), DateTimeOffset.UtcNow.AddSeconds(60), 1)) };
        }
    }

    private sealed class StalledRenewalHandler(bool malformed) : HttpMessageHandler
    {
        private int _published;
        private int _renewals;
        internal readonly TaskCompletionSource Stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource StallEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Renewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Withdrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                if (request.RequestUri!.AbsolutePath == "/v1/rooms/room-2") Withdrawn.TrySetResult();
                return new(HttpStatusCode.NoContent);
            }
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/info") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryServiceInfo(1, "Test", 60, "domain", null)) };
            if (path == "/v1/rooms/room-1/heartbeat")
            {
                Stalled.TrySetResult();
                if (malformed)
                {
                    StallEnded.TrySetResult();
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryLease("room-1", "invalid", DateTimeOffset.UtcNow.AddSeconds(60), 1)) };
                }
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { StallEnded.TrySetResult(); }
            }
            if (path == "/v1/rooms/room-2/heartbeat")
            {
                if (Interlocked.Increment(ref _renewals) >= 2) Renewed.TrySetResult();
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryLease("room-2", new string('1', 64), DateTimeOffset.UtcNow.AddSeconds(60), 1)) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryLease("room-" + Interlocked.Increment(ref _published), new string('1', 64), DateTimeOffset.UtcNow.AddSeconds(60), 1)) };
        }
    }

    private sealed class UnavailableWithdrawalHandler(bool malformed) : HttpMessageHandler
    {
        private int _published;
        private int _withdrawals;
        internal int Withdrawals => Volatile.Read(ref _withdrawals);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Interlocked.Increment(ref _withdrawals);
                if (malformed) return new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") };
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Withdrawal cancellation was not observed.");
            }
            if (request.RequestUri!.AbsolutePath == "/v1/info")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryServiceInfo(1, "Test", 90, "domain", null)) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryLease("room-" + Interlocked.Increment(ref _published), new string('1', 64), DateTimeOffset.UtcNow.AddSeconds(90), 30)) };
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
