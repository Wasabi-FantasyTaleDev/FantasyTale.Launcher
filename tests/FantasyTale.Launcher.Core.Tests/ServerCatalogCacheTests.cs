using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerCatalogCacheTests
{
    private static readonly ServerDefinition Server = new("one", "fixture", "One", "https://one.example/", null, true, false);
    private static LauncherCatalog Catalog(string name) => new(1, Server.Game, name, false, [], []) { HostPolicy = "domain" };
    private static HttpResponseMessage Reply(LauncherCatalog catalog) => new(HttpStatusCode.OK) { Content = JsonContent.Create(catalog) };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task LaunchDiscoveryReusesValidatedMetadataOnlyForTheExactServerEndpointAndGame()
    {
        var requests = 0;
        using var handler = new Handler(_ => { requests++; return Task.FromResult(Reply(Catalog("Discovered"))); });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        Assert.Null(api.CachedCatalog(Server));
        var catalog = await api.CatalogAsync(Server);
        Assert.Same(catalog, api.CachedCatalog(Server));
        Assert.Null(api.CachedCatalog(Server with { ApiUrl = "https://two.example/" }));
        Assert.Null(api.CachedCatalog(Server with { Id = "two" }));
        Assert.Null(api.CachedCatalog(Server with { Game = "other" }));
        Assert.Equal(1, requests);
        api.Forget(Server.Id);
        Assert.Null(api.CachedCatalog(Server));
    }

    [Fact]
    public async Task ExplicitRefreshReplacesDiscoveryAndNetworkFailureDoesNotCorruptTheLastValidatedSnapshot()
    {
        var name = "First"; var reject = false;
        using var handler = new Handler(_ => reject ? throw new HttpRequestException("Unavailable") : Task.FromResult(Reply(Catalog(name))));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await api.CatalogAsync(Server);
        name = "Second";
        var second = await api.CatalogAsync(Server);
        Assert.Equal(name, api.CachedCatalog(Server)!.Name);
        reject = true;
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => api.CatalogAsync(Server));
        Assert.Same(second, api.CachedCatalog(Server));
    }

    [Fact]
    public async Task RemovedServerCannotBeRestoredByItsDelayedDiscoveryResponse()
    {
        var started = Signal(); var release = Signal();
        using var handler = new Handler(async ct => { started.TrySetResult(); await release.Task.WaitAsync(ct); return Reply(Catalog("Old")); });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var pending = api.CatalogAsync(Server);
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); api.Forget(Server.Id); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Null(api.CachedCatalog(Server));
    }

    [Fact]
    public async Task OlderDiscoveryCannotReplaceAMoreRecentlyCompletedCatalog()
    {
        var started = Signal(); var release = Signal(); var requests = 0;
        using var handler = new Handler(async ct =>
        {
            if (Interlocked.Increment(ref requests) != 1) return Reply(Catalog("New"));
            started.TrySetResult(); await release.Task.WaitAsync(ct);
            return Reply(Catalog("Old"));
        });
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var old = api.CatalogAsync(Server);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("New", (await api.CatalogAsync(Server)).Name);
        }
        finally { release.TrySetResult(); }
        Assert.Equal("Old", (await old).Name);
        Assert.Equal("New", api.CachedCatalog(Server)!.Name);
    }

    [Fact]
    public async Task CachedDiscoveryCannotBypassTheServerMinimumLauncherVersion()
    {
        using var handler = new Handler(_ => Task.FromResult(Reply(Catalog("Upgrade") with { MinimumLauncherVersion = "99.0.0" })));
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        await Assert.ThrowsAsync<LauncherUpgradeRequiredException>(() => api.CatalogAsync(Server));
        Assert.Throws<LauncherUpgradeRequiredException>(() => api.CachedCatalog(Server));
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(ct);
    }
}
