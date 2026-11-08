using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AccountListBridgeTests
{
    [Theory]
    [InlineData("revoked", true)]
    [InlineData("revoked", false)]
    [InlineData("unavailable", true)]
    [InlineData("catalog", true)]
    [InlineData("upgrade", true)]
    [InlineData("malformed", true)]
    [InlineData("valid", true)]
    public async Task RestoredAccountListingIsolatesRevocationWithoutHidingServiceFailures(string failure, bool anotherAccount)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-account-list-bridge-").FullName;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            await using var app = builder.Build();
            var renewals = new ConcurrentQueue<string>();
            app.MapGet("/v1/catalog", () => failure == "catalog" ? Results.Unauthorized()
                : Results.Json(new LauncherCatalog(1, "fixture", "Fixture", false, [], [])));
            app.MapPost("/v1/auth/refresh", async (HttpContext context) =>
            {
                var body = await context.Request.ReadFromJsonAsync<Dictionary<string, string>>(context.RequestAborted);
                var username = body!["refreshToken"] == "fixture-refresh-bob" ? "bob" : "alice";
                Assert.Equal("fixture-refresh-" + username, body["refreshToken"]);
                renewals.Enqueue(username);
                if (username == "bob")
                {
                    if (failure == "revoked") return Results.Unauthorized();
                    if (failure == "unavailable") return Results.StatusCode(503);
                    if (failure == "upgrade") return Results.Json(new { error = "launcher_update_required", minimumLauncherVersion = "999.0.0" }, statusCode: 426);
                    if (failure == "malformed") return Results.Json(new { accessToken = "incomplete" });
                }
                return Results.Json(new
                {
                    accessToken = "fixture-access-" + username, accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "fixture-rotated-" + username, refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(7), account = Profile(username)
                });
            });
            await app.StartAsync();
            var address = Assert.Single(app.Urls);
            var games = Path.Combine(root, "games");
            var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            var data = Path.Combine(root, "data");
            new ServerStore(Path.Combine(data, "servers")).Save(new("one", "fixture", "One", address, null, false, false));
            var store = new ProtectedAccountSessionStore(data);
            var accounts = new List<PersistedAccountSession>();
            if (anotherAccount) accounts.Add(new("one", address, "alice", "fixture-refresh-alice", DateTimeOffset.UtcNow.AddDays(7), Profile("alice"), false));
            accounts.Add(new("one", address, "bob", "fixture-refresh-bob", DateTimeOffset.UtcNow.AddDays(7), Profile("bob"), true));
            store.Save("one", address, "bob", accounts);

            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            async Task<JsonNode> Call(string method, object? args = null)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }

            var listing = await Call("accounts.list", new { serverId = "one" });
            if (failure == "revoked")
            {
                Assert.Null(listing["error"]);
                Assert.Null(listing["result"]!["current"]);
                var remaining = listing["result"]!["accounts"]!.AsArray();
                Assert.Equal(anotherAccount ? 1 : 0, remaining.Count);
                Assert.Equal(anotherAccount ? 1 : 0, store.Load().Count);
                if (anotherAccount)
                {
                    Assert.Equal("alice", remaining[0]!["account"]!["username"]!.GetValue<string>());
                    Assert.Null((await Call("accounts.select", new { serverId = "one", username = "alice" }))["error"]);
                    var retry = await Call("accounts.list", new { serverId = "one" });
                    Assert.Null(retry["error"]);
                    Assert.Equal("alice", retry["result"]!["current"]!["username"]!.GetValue<string>());
                    Assert.Equal("fixture-rotated-alice", Assert.Single(store.Load()).RefreshToken);
                    Assert.Equal(["bob", "alice"], renewals);
                }
                else
                {
                    Assert.Null((await Call("accounts.list", new { serverId = "one" }))["error"]);
                    Assert.Equal(["bob"], renewals);
                }
            }
            else
            {
                if (failure == "valid")
                {
                    Assert.Null(listing["error"]);
                    Assert.Equal("bob", listing["result"]!["current"]!["username"]!.GetValue<string>());
                    Assert.Equal(2, listing["result"]!["accounts"]!.AsArray().Count);
                }
                else
                {
                    Assert.NotNull(listing["error"]);
                    if (failure == "upgrade") Assert.Equal("launcher_update_required", listing["code"]!.GetValue<string>());
                    if (failure == "unavailable") Assert.Equal(503, listing["httpStatus"]!.GetValue<int>());
                }
                Assert.Equal(2, store.Load().Count);
                Assert.Equal("bob", (await Call("accounts.current", new { serverId = "one" }))["result"]!["username"]!.GetValue<string>());
                Assert.Equal(failure == "catalog" ? [] : ["bob"], renewals);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static AccountProfile Profile(string username) => new(username, username, new Dictionary<string, long?>(), null);
}
