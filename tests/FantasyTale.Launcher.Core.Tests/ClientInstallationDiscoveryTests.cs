using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Host.Bridge;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientInstallationDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-discovery-").FullName;
    private static BuildDefinition Build => new("fixture-1", "test", 1, new(new Dictionary<string, string> { ["en"] = "Fixture" }), "client.exe", "forward", "fixture", null, null, "none", [], []);

    [Fact]
    public async Task NestedRootRegistrationRepairsTheEarlierParentAndReusesItOnRetry()
    {
        var selected = Directory.CreateDirectory(Path.Combine(_root, "download")).FullName;
        var client = Directory.CreateDirectory(Path.Combine(selected, "release", "game")).FullName;
        await File.WriteAllTextAsync(Path.Combine(client, "client.exe"), "fixture");
        var found = Assert.IsType<DiscoveredClientInstallation>(ClientInstallationDiscovery.Find(selected, [Build]));
        Assert.Equal(client, found.Path); Assert.Equal(["fixture-1"], found.BuildIds);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var old = new InstanceDefinition("first", "Fixture", "fixture", Build.Id, null, []);
        await store.CreateAtAsync(old, selected, true);
        var repaired = await store.RegisterDetectedAsync(old with { Id = "retry" }, client, Build);
        Assert.Equal("first", repaired.Id); Assert.Equal(client, store.ClientFolder("first"));
        var repeated = await store.RegisterDetectedAsync(old with { Id = "again" }, client, Build);
        Assert.Equal("first", repeated.Id); Assert.Single(store.List());
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(client, "client.exe")));
    }

    [Fact]
    public async Task MissingExecutableNeverCreatesARegistrationAndMultipleClientsRequireSelection()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var selected = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
        Assert.Null(ClientInstallationDiscovery.Find(selected, [Build]));
        var definition = new InstanceDefinition("first", "Fixture", "fixture", Build.Id, null, []);
        await Assert.ThrowsAsync<IOException>(() => store.RegisterDetectedAsync(definition, selected, Build));
        Assert.Empty(store.List());
        foreach (var name in new[] { "one", "two" })
            await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Path.Combine(selected, name)).FullName, "client.exe"), "fixture");
        Assert.Equal("client_search_multiple", Assert.Throws<IOException>(() => ClientInstallationDiscovery.Find(selected, [Build])).Message);
        var first = Path.Combine(selected, "one");
        Assert.Equal(first, ClientInstallationDiscovery.Find(first, [Build])!.Path);
    }

    [Fact]
    public async Task ParentRepairCannotChangeAValidRegistrationOrAnotherBuild()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var parent = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(parent, "nested")).FullName;
        await File.WriteAllTextAsync(Path.Combine(parent, "client.exe"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(nested, "client.exe"), "fixture");
        var definition = new InstanceDefinition("first", "Fixture", "fixture", Build.Id, null, []);
        await store.CreateAtAsync(definition, parent, true);
        await Assert.ThrowsAsync<IOException>(() => store.RegisterDetectedAsync(definition with { Id = "second" }, nested, Build));
        Assert.Equal(parent, store.ClientFolder("first"));
    }

    [Fact]
    public async Task BridgeResolvesNestedFolderAndRepeatedAddReturnsTheOriginalIdentity()
    {
        var games = Path.Combine(_root, "games");
        var game = Directory.CreateDirectory(Path.Combine(games, "Fixture")).FullName;
        Directory.CreateDirectory(Path.Combine(game, "builds"));
        await File.WriteAllTextAsync(Path.Combine(game, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
        await File.WriteAllTextAsync(Path.Combine(game, "builds", "fixture-1.toml"), "schema = 1\n[build]\nid = \"fixture-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
        var selected = Directory.CreateDirectory(Path.Combine(_root, "release")).FullName;
        var actual = Directory.CreateDirectory(Path.Combine(selected, "nested")).FullName;
        var data = Path.Combine(_root, "data");
        await using var backend = new LauncherBackend(games, data);
        var replies = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        var sequence = 0;
        async Task<JsonNode> Call(string method, object parameters)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }), default);
            return replies.Single(reply => reply["id"]?.GetValue<int>() == id);
        }
        var missing = await Call("instances.create", new { id = "missing", name = "Fixture", game = "fixture", build = Build.Id, installationPath = selected, existingClient = true });
        Assert.Equal("client_executable_missing", missing["error"]!.GetValue<string>());
        Assert.Empty(new InstanceStore(Path.Combine(data, "instances")).List());
        await File.WriteAllTextAsync(Path.Combine(actual, "client.exe"), "fixture");
        var inspected = await Call("instances.inspectInstallation", new { id = "inspect", game = "fixture", installationPath = selected });
        Assert.Null(inspected["error"]); Assert.Equal(actual, inspected["result"]!["path"]!.GetValue<string>());
        foreach (var id in new[] { "first", "retry" })
        {
            var created = await Call("instances.create", new { id, name = "Fixture", game = "fixture", build = Build.Id, installationPath = selected, existingClient = true });
            Assert.Null(created["error"]); Assert.Equal("first", created["result"]!["id"]!.GetValue<string>());
        }
        var rechecked = await Call("instances.inspectInstallation", new { id = "inspect-again", game = "fixture", installationPath = selected });
        Assert.Null(rechecked["error"]);
        Assert.Single(new InstanceStore(Path.Combine(data, "instances")).List());
    }

    public void Dispose() => Directory.Delete(_root, true);
}
