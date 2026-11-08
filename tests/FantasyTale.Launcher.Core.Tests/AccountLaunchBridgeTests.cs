using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AccountLaunchBridgeTests
{
    [Theory]
    [InlineData("switch")]
    [InlineData("switch-back")]
    [InlineData("login")]
    public async Task QueuedLaunchRejectsAccountChangesBeforeLifecycleLeaseBecomesAvailable(string change)
    {
        var root = Directory.CreateTempSubdirectory("ft-account-launch-bridge-").FullName;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            app.MapGet("/store.toml", async (HttpContext context) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                return Results.Text("schema=2\n[catalog]\nid='fixture'\nname={en='Fixture'}\n");
            });
            app.MapGet("/v1/catalog", () => Results.Json(new LauncherCatalog(1, "fixture", "Fixture", false, [],
                [new("preset", "test-1", new Dictionary<string, string> { ["en"] = "Fixture" }, new("127.0.0.1", 7100), "required")])));
            var loginSequence = 0;
            app.MapPost("/v1/auth/login", async (HttpContext context) =>
            {
                var body = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>(context.RequestAborted);
                var username = body!["username"].GetString()!;
                var sequence = Interlocked.Increment(ref loginSequence);
                return Results.Json(new
                {
                    accessToken = "access-" + sequence, accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "refresh-" + sequence, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    account = new AccountProfile(username, username, new Dictionary<string, long?>(), null)
                });
            });
            var ticketRequests = 0;
            app.MapPost("/v1/launch", () => { Interlocked.Increment(ref ticketRequests); return Results.StatusCode(500); });
            await app.StartAsync();
            var address = Assert.Single(app.Urls);
            var games = Path.Combine(root, "games");
            var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema=1\n[build]\nid='test-1'\ngame='fixture'\nbranch='test'\nprotocol_version=1\nexecutable='client.exe'\nname={en='Fixture'}\n[launch]\nlogin='unverified'\narguments=[]\n");
            var data = Path.Combine(root, "data");
            var sources = new StoreSourceStore(Path.Combine(data, "sources"));
            foreach (var source in sources.List()) sources.Save(source with { Enabled = false });
            sources.Save(new("fixture", "Fixture", "http", address, "store.toml", true, false));
            new ServerStore(Path.Combine(data, "servers")).Save(new("one", "fixture", "One", address, new("127.0.0.1", 7100), false, false));
            await new InstanceStore(Path.Combine(data, "instances")).CreateAsync(new("client", "Client", "fixture", "test-1", null, []));
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            async Task<JsonNode> Call(string method, object parameters)
            {
                var id = Interlocked.Increment(ref sequence);
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Null((await Call("accounts.login", new { serverId = "one", username = "bob", password = "password" }))["error"]);
                Assert.Null((await Call("accounts.login", new { serverId = "one", username = "alice", password = "password" }))["error"]);
                var lifecycle = (SemaphoreSlim)typeof(LauncherBackend).GetField("_lifecycle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!;
                await lifecycle.WaitAsync();
                var launch = Call("instances.launch", new { id = "client", serverId = "one" });
                try
                {
                Assert.False(launch.IsCompleted);
                if (change == "login")
                    Assert.Null((await Call("accounts.login", new { serverId = "one", username = "alice", password = "password" }))["error"]);
                else
                {
                    Assert.Null((await Call("accounts.select", new { serverId = "one", username = "bob" }))["error"]);
                    if (change == "switch-back")
                        Assert.Null((await Call("accounts.select", new { serverId = "one", username = "alice" }))["error"]);
                }
                }
                finally { lifecycle.Release(); }
                var result = await launch.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains("selected account changed", result["error"]!.GetValue<string>(), StringComparison.Ordinal);
                Assert.Equal(0, ticketRequests);
                Assert.Empty((await Call("instances.running", new { }))["result"]!.AsArray());
                Assert.False(File.Exists(Path.Combine(data, "instances", "client", "client", "session.toml")));
            }
            finally { release.TrySetResult(); }
        }
        finally { release.TrySetResult(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LocalLaunchValidationDoesNotWaitForStoreDiscovery()
    {
        var root = Directory.CreateTempSubdirectory("ft-launch-store-independent-").FullName;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
            await using var app = builder.Build();
            app.MapGet("/store.toml", async (HttpContext context) =>
            {
                entered.TrySetResult(); await release.Task.WaitAsync(context.RequestAborted);
                return Results.Text("schema=2\n[catalog]\nid='fixture'\n");
            });
            await app.StartAsync();
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema=1\n[build]\nid='test-1'\ngame='fixture'\nbranch='test'\nprotocol_version=1\nexecutable='client.exe'\nenabled=false\nname={en='Fixture'}\n[launch]\nlogin='none'\narguments=[]\n");
            var data = Path.Combine(root, "data"); var sources = new StoreSourceStore(Path.Combine(data, "sources"));
            foreach (var source in sources.List()) sources.Save(source with { Enabled = false });
            sources.Save(new("fixture", "Fixture", "http", Assert.Single(app.Urls), "store.toml", true, false));
            new ServerStore(Path.Combine(data, "servers")).Save(new("one", "fixture", "One", null, new("127.0.0.1", 7100), false, false));
            await new InstanceStore(Path.Combine(data, "instances")).CreateAsync(new("client", "Client", "fixture", "test-1", null, []));
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 1, method = "instances.launch", @params = new { id = "client", serverId = "one" } }), default).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(release.Task.IsCompleted);
                Assert.Equal("build_disabled", replies.Single(row => row["id"]?.GetValue<long>() == 1)["error"]!.GetValue<string>());
            }
            finally { release.TrySetResult(); }
        }
        finally { release.TrySetResult(); Directory.Delete(root, true); }
    }
}
