using System.Net;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerCenterRoutingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ServerDirectory Directory(string mode) => new(1, "kr-812",
    [
        new(1, "Automatic", true, [Node("first", "192.0.2.1")]),
        new(2, "First", false, [Node("first", "192.0.2.1")]),
        new(3, "Manual only", false, [Node("manual-only", "192.0.2.2")]),
    ]) { Center = new(true, mode) };

    private static ServerDirectoryTarget Node(string id, string host) => new(id, host, 17110, "shared", "", true, false, 1, 100);

    /// <summary>
    /// The Gateway's Center list offers only nodes that serve the client's build: a node that states other builds is
    /// dropped, a manual server left without a node goes with it, and nodes or reserved servers that state nothing stay.
    /// </summary>
    [Fact]
    public void CenterListKeepsOnlyNodesServingTheBuild()
    {
        var legacyOnly = Node("legacy-only", "192.0.2.3") with { ClientBuilds = ["kr-812"] };
        var modernOnly = Node("modern-only", "192.0.2.4") with { ClientBuilds = ["kr-1256"] };
        var directory = new ServerDirectory(1, "kr-812",
        [
            new(1, "Automatic", true, [legacyOnly, modernOnly]),
            new(2, "Legacy", false, [legacyOnly]),
            new(3, "Modern", false, [modernOnly]),
            new(4, "Unstated", false, [Node("unstated", "192.0.2.5")]),
            new(5, "Reserved", false, []),
        ]) { Center = new(true, "gateway") };
        var served = ServerCenterRouting.ServingBuild(directory);
        Assert.Equal(["Automatic", "Legacy", "Unstated", "Reserved"], served.Servers.Select(row => row.Name));
        Assert.Equal(["legacy-only"], served.Servers[0].Targets.Select(target => target.NodeId));
    }

    private static ServerDirectoryResolver NoNetwork() => new(
        (_, _) => throw new InvalidOperationException("Native handling must not resolve Gateway destinations."),
        (_, _) => throw new InvalidOperationException("This route must not measure automatic candidates."));

    [Theory]
    [InlineData("absent")]
    [InlineData("disabled")]
    [InlineData("disabled-unknown-mode")]
    [InlineData("mode-only")]
    [InlineData("native")]
    public async Task CatalogWithoutEnabledGatewayDoesNotRequestAnyDirectory(string scenario)
    {
        var center = scenario switch
        {
            "absent" => null,
            "disabled" => new ServerCenterConfiguration(false, "gateway"),
            // A future mode on a disabled Center must not block ordinary login.
            "disabled-unknown-mode" => new ServerCenterConfiguration(false, "future-mode"),
            "mode-only" => JsonSerializer.Deserialize<ServerCenterConfiguration>("{\"mode\":\"gateway\"}", Json),
            _ => new ServerCenterConfiguration(true, "native"),
        };
        var route = await ServerCenterRouting.PrepareAsync(center,
            _ => throw new InvalidOperationException("This server does not implement a Launcher directory API."),
            NoNetwork(), true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Empty(route.Targets);
        Assert.Empty(route.Choices);
    }

    [Fact]
    public async Task EnabledGatewayCatalogReadsItsDeclaredDirectoryOnce()
    {
        var calls = 0;
        var route = await ServerCenterRouting.PrepareAsync(new(true, "gateway"), _ =>
        {
            calls++;
            return Task.FromResult<ServerDirectory?>(Directory("gateway"));
        }, NoNetwork(), true, "required");
        Assert.Equal(1, calls);
        Assert.True(route.AutomaticRequested);
        Assert.Equal(Directory("gateway").Servers.Count, route.Directory!.Servers.Count);
    }

    [Fact]
    public async Task ExplicitEnablementWithoutModeDefaultsToGateway()
    {
        var center = JsonSerializer.Deserialize<ServerCenterConfiguration>("{\"enabled\":true}", Json)!;
        Assert.True(center.Enabled);
        Assert.Equal("gateway", center.Mode);
        var route = await ServerCenterRouting.PrepareAsync(center,
            _ => Task.FromResult<ServerDirectory?>(Directory("gateway")), NoNetwork(), true, "required");
        Assert.True(route.AutomaticRequested);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("native")]
    [InlineData("undeclared")]
    public async Task EnabledGatewayCannotUseAnAbsentOrContradictingDirectory(string scenario)
    {
        ServerDirectory? directory = scenario switch
        {
            "missing" => null,
            "disabled" => Directory("gateway") with { Center = new(false, "gateway") },
            "undeclared" => Directory("gateway") with { Center = null },
            _ => Directory("native"),
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => ServerCenterRouting.PrepareAsync(new(true, "gateway"),
            _ => Task.FromResult(directory), NoNetwork(), true, "required"));
    }

    [Fact]
    public async Task MissingCenterConfigurationDoesNotEnableAnyCenterHandling()
    {
        var json = JsonSerializer.Serialize(Directory("native"), Json);
        using var source = JsonDocument.Parse(json);
        var fields = source.RootElement.EnumerateObject().Where(row => row.Name != "center")
            .ToDictionary(row => row.Name, row => row.Value.Clone());
        var directory = JsonSerializer.Deserialize<ServerDirectory>(JsonSerializer.Serialize(fields), Json)!;
        Assert.Null(directory.Center);
        var route = await ServerCenterRouting.PrepareAsync(directory, NoNetwork(), true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Empty(route.Targets);
        Assert.Empty(route.Choices);
    }

    [Fact]
    public async Task NativeDirectoryWithAutomaticMetadataDoesNotPrepareGatewayOrChangeEndpoint()
    {
        var route = await ServerCenterRouting.PrepareAsync(Directory("native"), NoNetwork(), true, "required");
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var original = new ServerEndpoint("192.0.2.100", 17100);
        var context = new LaunchContext(game, game.Builds.Single(row => row.Id == "kr-812"), "unused", null, "player")
        {
            GameEndpoint = original, GameTargets = route.Targets, GameChoices = route.Choices,
        };
        Assert.False(route.AutomaticRequested);
        Assert.Null(await new BubbleFighterModule().PrepareBootstrapAsync(context, CancellationToken.None));
        Assert.Equal(original, context.GameEndpoint);
    }

    [Fact]
    public async Task ServerOwnedNativeAutomaticDoesNotRequireLauncherPoolAdmission()
    {
        var directory = Directory("native");
        directory = directory with
        {
            Servers = [new(1, "Server automatic", true,
                [Node("first", "192.0.2.1") with { Online = false, DataScope = "" },
                 Node("second", "192.0.2.2") with { DataScope = "separate" }])],
        };
        var route = await ServerCenterRouting.PrepareAsync(directory, NoNetwork(), true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Empty(route.Targets);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("gateway")]
    [InlineData(null)]
    public async Task DisabledCenterDoesNotPrepareGatewayEvenWithModeAndAutomaticMetadata(string? mode)
    {
        var directory = Directory("gateway") with { Center = new(false, mode) };
        var route = await ServerCenterRouting.PrepareAsync(directory, NoNetwork(), true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Empty(route.Targets);
        Assert.Empty(route.Choices);
    }

    [Fact]
    public async Task ModeWithoutExplicitEnablementDoesNotActivateCenter()
    {
        var directory = Directory("gateway") with
        {
            Center = JsonSerializer.Deserialize<ServerCenterConfiguration>("{\"mode\":\"gateway\"}", Json),
        };
        Assert.False(directory.Center!.Enabled);
        var route = await ServerCenterRouting.PrepareAsync(directory, NoNetwork(), true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Empty(route.Targets);
    }

    [Fact]
    public async Task GatewayAutomaticRequiresServerRowAndAuditedAuthenticatedSupport()
    {
        var route = await ServerCenterRouting.PrepareAsync(Directory("gateway"), NoNetwork(), true, "required");
        Assert.True(route.AutomaticRequested);
        Assert.Empty(route.Targets);
        Assert.Empty(route.Choices);
        Assert.Equal([1, 2, 3], route.Directory!.Servers.Select(row => row.Id));
    }

    [Theory]
    [InlineData(false, "required")]
    [InlineData(true, "none")]
    [InlineData(true, "game")]
    public async Task UnsupportedAutomaticDoesNotPreselectOrProbeItsAlias(bool supportsAutomatic, string login)
    {
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));
        var route = await ServerCenterRouting.PrepareAsync(Directory("gateway"), resolver, supportsAutomatic, login);
        Assert.False(route.AutomaticRequested);
        Assert.Equal(["2", "3"], route.Targets.Select(row => row.Id));
        Assert.Equal([2, 3], route.Choices.Select(row => row.NativeId));
    }

    [Fact]
    public async Task ServerDisablingAutomaticKeepsManualGatewayDirectory()
    {
        var directory = Directory("gateway");
        directory = directory with { Servers = directory.Servers.Where(row => !row.Automatic).ToArray() };
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));
        var route = await ServerCenterRouting.PrepareAsync(directory, resolver, true, "required");
        Assert.False(route.AutomaticRequested);
        Assert.Equal(["2", "3"], route.Targets.Select(row => row.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("automatic")]
    [InlineData("Gateway")]
    public async Task UnknownCenterModeNeverEnablesLocalGateway(string? mode)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ServerCenterRouting.PrepareAsync(
            Directory("native") with { Center = new(true, mode) }, NoNetwork(), true, "required"));
    }
}
