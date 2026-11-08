using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.DirectoryService;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DirectoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-directory-").FullName;
    private static DirectoryAdvertisement Details => new("bubblefighter", "My server", "http://game.example:8090", ["cn-406"], "An introduction");

    [Fact]
    public void ReviewAndRestartPreserveApprovedServersWhileRoomsExpireIndependently()
    {
        var clock = new TestClock(); string persistentId;
        using (var registry = new DirectoryRegistry(_root, 4, 30, clock))
        {
            var pending = registry.Submit(Details); Assert.Empty(registry.List().Entries);
            Assert.Throws<InvalidOperationException>(() => registry.Submit(Details));
            registry.Review(pending.Id, true); persistentId = Assert.Single(registry.List().Entries).Id;
            Assert.Throws<KeyNotFoundException>(() => registry.Review(pending.Id, false));
            var edit = registry.Submit(Details with { Name = "Changed" }); Assert.Equal("My server", registry.List().Entries[0].Details.Name);
            registry.Review(edit.Id, true); Assert.Equal(persistentId, Assert.Single(registry.List().Entries).Id);
            var room = registry.Publish(Details); Assert.Equal(2, registry.List().Entries.Count);
            Assert.Throws<UnauthorizedAccessException>(() => registry.Renew(room.Id, new string('0', 64)));
            clock.Advance(20); var renewed = registry.Renew(room.Id, room.Token); Assert.True(renewed.ExpiresAt > room.ExpiresAt);
            clock.Advance(31); Assert.Single(registry.List().Entries);
            Assert.Throws<UnauthorizedAccessException>(() => registry.Renew(room.Id, room.Token));
            var current = registry.Publish(Details); Assert.NotEqual(room.Id, current.Id);
            Assert.Throws<UnauthorizedAccessException>(() => registry.Withdraw(current.Id, room.Token));
            _ = registry.Submit(Details with { ApiUrl = "https://other.example/" });
        }
        using var reopened = new DirectoryRegistry(_root, 4, 30, clock);
        Assert.Equal(persistentId, Assert.Single(reopened.List().Entries).Id); Assert.Single(reopened.Pending());
        reopened.RemoveServer(persistentId); Assert.Empty(reopened.List().Entries);
    }

    [Fact]
    public void CapacityExclusivityAndFailedStorageNeverMutateVisibleState()
    {
        using var registry = new DirectoryRegistry(_root, 1, 30);
        Assert.Throws<IOException>(() => new DirectoryRegistry(_root));
        var a = registry.Submit(Details);
        Assert.Throws<InvalidOperationException>(() => registry.Submit(Details with { ApiUrl = "http://other.example" }));
        registry.Review(a.Id, true);
        var b = registry.Submit(Details with { ApiUrl = "http://other.example" });
        Assert.Throws<InvalidOperationException>(() => registry.Review(b.Id, true)); Assert.Single(registry.Pending());
        registry.Review(b.Id, false);
        var room = registry.Publish(Details); Assert.Throws<InvalidOperationException>(() => registry.Publish(Details));
        registry.Withdraw(room.Id, room.Token);
        using var held = new FileStream(Path.Combine(_root, "directory.json.saving"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => registry.RemoveServer(Assert.Single(registry.List().Entries).Id));
        Assert.Single(registry.List().Entries);
    }

    [Fact]
    public void InvalidStateIsNotReplacedAndCallerArraysCannotChangeApprovedMetadata()
    {
        using (var registry = new DirectoryRegistry(_root))
        {
            var builds = new[] { "cn-406" }; var pending = registry.Submit(Details with { Builds = builds });
            builds[0] = "cn-279"; registry.Review(pending.Id, true);
            var entry = Assert.Single(registry.List().Entries); ((IList<string>)entry.Details.Builds)[0] = "cn-279";
            Assert.Equal("cn-406", registry.List().Entries[0].Details.Builds[0]);
        }
        File.WriteAllText(Path.Combine(_root, "directory.json"), "broken");
        Assert.Throws<JsonException>(() => new DirectoryRegistry(_root));
        Assert.Equal("broken", File.ReadAllText(Path.Combine(_root, "directory.json")));
    }

    [Fact]
    public async Task RealHttpSupportsReviewRoomOwnershipAndRestartThroughTheSharedClient()
    {
        var secret = Guid.NewGuid().ToString("N");
        var options = new DirectoryOptions { Listen = "http://127.0.0.1:0", DataPath = _root, AdminToken = secret, HostPolicy = "ip", Region = "CN" };
        await using (var server = await DirectoryHttp.StartAsync(options))
        {
            var source = new DirectorySource("test", "Test", server.Addresses.Single(), true, false);
            using var client = new DirectoryClient(); using var http = new HttpClient { BaseAddress = new Uri(source.BaseUrl!) };
            var info = await client.InfoAsync(source); Assert.Equal("ip", info.HostPolicy);
            var submission = await client.SubmitAsync(source, Details); Assert.Empty((await client.ListAsync(source)).Entries);
            using var denied = await http.PostAsync("v1/admin/submissions/" + submission.Id + "/approve", null);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var approved = await http.PostAsync("v1/admin/submissions/" + submission.Id + "/approve", null); approved.EnsureSuccessStatusCode();
            var persistent = Assert.Single((await client.ListAsync(source)).Entries); Assert.Equal("server", persistent.Kind);
            var room = await client.PublishAsync(source, Details);
            var listed = await client.ListAsync(source); Assert.Equal(2, listed.Entries.Count);
            Assert.DoesNotContain("token", JsonSerializer.Serialize(listed), StringComparison.OrdinalIgnoreCase);
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.RenewAsync(source, room with { Token = new string('0', 64) }));
            Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
            _ = await client.RenewAsync(source, room);
            await client.WithdrawAsync(source, room); await client.WithdrawAsync(source, room);
            Assert.Single((await client.ListAsync(source)).Entries);
            using var extra = await http.PostAsJsonAsync("v1/rooms", new { Details.Game, Details.Name, Details.ApiUrl, Details.Builds, Details.Description, kind = "server" });
            Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
            using var invalid = await http.PostAsJsonAsync("v1/rooms", Details with { Game = "../escape" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await using var restarted = await DirectoryHttp.StartAsync(options);
        using var after = new DirectoryClient();
        Assert.Single((await after.ListAsync(new("test", "Test", restarted.Addresses.Single(), true, false))).Entries);
    }

    [Fact]
    public async Task IpDiscoveryFallbackBypassesAFailingProxyAndRemembersTheServerRequirement()
    {
        await using var server = await DirectoryHttp.StartAsync(new DirectoryOptions
        {
            Listen = "http://127.0.0.1:0", DataPath = _root, AdminToken = Guid.NewGuid().ToString("N"), HostPolicy = "ip", Region = "CN"
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        var proxyReply = Task.Run(async () =>
        {
            using var socket = await proxy.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var lines = new List<string>();
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line) lines.Add(line);
            var response = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, deadline.Token);
            return lines;
        }, deadline.Token);
        using var handler = NetworkPolicy.CreateHttpHandler(new WebProxy(new Uri($"http://{proxy.LocalEndpoint}")));
        using var http = new HttpClient(handler);
        using var client = new DirectoryClient(http, new NetworkPolicy(() => "disabled", (_, _) => Task.FromResult(new[] { IPAddress.Loopback })));
        var endpoint = new UriBuilder(server.Addresses.Single()) { Host = "directory.example" }.Uri;
        var source = new DirectorySource("test", "Test", endpoint.AbsoluteUri, true, false);
        Assert.Empty((await client.ListAsync(source, deadline.Token)).Entries);
        Assert.Equal("ip", (await client.InfoAsync(source, deadline.Token)).HostPolicy);
        var request = await proxyReply;
        Assert.StartsWith($"GET {new Uri(endpoint, "v1/info")} HTTP/1.1", request[0]);
        Assert.DoesNotContain(request, line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
        Assert.False(proxy.Pending());
    }

    [Fact]
    public async Task DomainRequirementPreventsIpFallbackAndRoomSecretsNeverFollowRedirects()
    {
        var handler = new PolicyHandler(); using var http = new HttpClient(handler);
        using var client = new DirectoryClient(http, new NetworkPolicy(() => "all", (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.8") })));
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        _ = await client.InfoAsync(source);
        handler.Redirect = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.PublishAsync(source, Details));
        Assert.All(handler.Requests, item => Assert.Equal("directory.example", item.Host));
        Assert.All(handler.Requests, item => Assert.False(item.Credential));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RenewAsync(source, new("room-one", new string('1', 64), DateTimeOffset.UtcNow.AddSeconds(60), 30)));
        Assert.Single(handler.Requests, item => item.Credential);
        Assert.All(handler.Requests, item => Assert.Equal("directory.example", item.Host));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UnspecifiedDeploymentRegionIsAbsentFromDiscovery(string region)
    {
        await using var server = await DirectoryHttp.StartAsync(new DirectoryOptions
        {
            Listen = "http://127.0.0.1:0", DataPath = _root, AdminToken = Guid.NewGuid().ToString("N"), Region = region
        });
        using var client = new DirectoryClient();
        var info = await client.InfoAsync(new("test", "Test", server.Addresses.Single(), true, false));
        Assert.Null(info.Region);
    }

    [Fact]
    public void SourcesRoundTripAndDifferentDirectoriesCannotShareAnAccountDestination()
    {
        var store = new DirectorySourceStore(_root); var bundled = Assert.Single(store.List());
        store.Save(bundled with { BaseUrl = "http://directory.example/", Enabled = false });
        Assert.False(Assert.Single(store.List()).Enabled);
        var custom = new DirectorySource("friends", "Friends", "https://friends.example/", true, false); store.Save(custom);
        Assert.Equal(2, store.List().Count); Assert.Throws<InvalidOperationException>(() => store.Delete(bundled.Id));
        var entry = new DirectoryEntry("room-one", "room", Details, DateTimeOffset.UtcNow.AddSeconds(90));
        Assert.NotEqual(DirectoryConnections.Destination(bundled with { BaseUrl = "http://directory.example/" }, entry).Id, DirectoryConnections.Destination(custom, entry).Id);
        store.Delete(custom.Id); Assert.Single(store.List());
    }

    private sealed class PolicyHandler : HttpMessageHandler
    {
        internal bool Redirect;
        internal List<(string Host, bool Credential)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.Host, request.Headers.Authorization is not null));
            return Task.FromResult(Redirect ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://elsewhere.example/") } } :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryServiceInfo(1, "Test", 90, "domain", "CN")) });
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyBundledOverridesInheritTheOfficialEndpointWithoutChangingPreferences(bool enabled)
    {
        var store = new DirectorySourceStore(_root); var original = Assert.Single(store.List()); Assert.NotNull(original.BaseUrl);
        store.Save(original with { Name = "My source label", BaseUrl = null, Enabled = enabled });
        var upgraded = Assert.Single(new DirectorySourceStore(_root).List());
        Assert.Equal(original.BaseUrl, upgraded.BaseUrl); Assert.Equal(enabled, upgraded.Enabled); Assert.Equal("My source label", upgraded.Name);
        store.Save(upgraded with { BaseUrl = "https://my-directory.example/" });
        Assert.Equal("https://my-directory.example/", Assert.Single(store.List()).BaseUrl);
    }
    [Fact]
    public async Task JoinedRoomEndpointChangesDiscardItsAccountScopeBeforeConnection()
    {
        var handler = new MembershipHandler(); using var http = new HttpClient(handler); using var client = new DirectoryClient(http);
        var forgotten = new List<string>(); await using var connections = new DirectoryConnections(client, forgotten.Add);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        connections.Join(source, handler.Entry, "cn-406"); var destination = Assert.Single(connections.List());
        Assert.Equal(destination, await connections.ResolveAsync(destination.Id, default));
        handler.Entry = handler.Entry with { Details = Details with { ApiUrl = "http://changed.example/" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.ResolveAsync(destination.Id, default));
        Assert.Equal(destination.Id, Assert.Single(forgotten)); Assert.Empty(connections.List());
        Assert.Throws<InvalidOperationException>(() => connections.Join(source, handler.Entry with { ExpiresAt = DateTimeOffset.UtcNow.AddYears(1) }, "cn-406"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
        {
            handler.Entry = handler.Entry with { ExpiresAt = DateTimeOffset.UtcNow.AddYears(1) };
            return client.ListAsync(source);
        });
    }

    [Fact]
    public async Task JoinedRoomExpiresAgainstTheInjectedClock()
    {
        var clock = new TestClock();
        using var http = new HttpClient(new MembershipHandler()); using var client = new DirectoryClient(http);
        var forgotten = new List<string>(); await using var connections = new DirectoryConnections(client, forgotten.Add, clock);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        var entry = new DirectoryEntry("room-one", "room", Details, clock.GetUtcNow().AddSeconds(90));
        connections.Join(source, entry, "cn-406");
        var destination = Assert.Single(connections.List());
        clock.Advance(91);
        Assert.Empty(connections.List());
        Assert.Equal(destination.Id, Assert.Single(forgotten));
        Assert.Empty(connections.List());
        Assert.Single(forgotten);
    }

    [Fact]
    public async Task ExpiredRoomCannotResolveAndItsCapacityIsReclaimedBeforeJoining()
    {
        var clock = new TestClock();
        using var http = new HttpClient(new MembershipHandler()); using var client = new DirectoryClient(http);
        var forgotten = new List<string>(); await using var connections = new DirectoryConnections(client, forgotten.Add, clock);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        var entry = new DirectoryEntry("room-one", "room", Details, clock.GetUtcNow().AddSeconds(90));
        for (var i = 0; i < 32; i++) connections.Join(source, entry with { Id = "room-" + i }, "cn-406");
        var destination = connections.List()[0];
        clock.Advance(91);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connections.ResolveAsync(destination.Id, default));
        Assert.Equal(destination.Id, Assert.Single(forgotten));
        connections.Join(source, entry with { Id = "fresh", ExpiresAt = clock.GetUtcNow().AddSeconds(90) }, "cn-406");
        Assert.Single(connections.List());
        Assert.Equal(32, forgotten.Distinct(StringComparer.Ordinal).Count());
    }
    private sealed class MembershipHandler : HttpMessageHandler
    {
        internal DirectoryEntry Entry = new("room-one", "room", Details, DateTimeOffset.UtcNow.AddSeconds(90));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath == "/v1/info"
                ? JsonContent.Create(new DirectoryServiceInfo(1, "Test", 90, "domain", null)) : JsonContent.Create(new DirectoryListing(1, [Entry])) });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldMembershipResponseCannotRemoveARejoinedRoom(bool disconnectFirst)
    {
        var entry = new DirectoryEntry("room-one", "room", Details, DateTimeOffset.UtcNow.AddSeconds(90));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelayedMembershipHandler(started, release);
        using var http = new HttpClient(handler); using var client = new DirectoryClient(http);
        var forgotten = new List<string>(); await using var connections = new DirectoryConnections(client, forgotten.Add);
        var source = new DirectorySource("test", "Test", "http://directory.example/", true, false);
        connections.Join(source, entry, "cn-406");
        var destination = Assert.Single(connections.List());
        var old = connections.ResolveAsync(destination.Id, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (disconnectFirst) connections.DisconnectSource(source.Id);
        connections.Join(source, entry, "cn-406");
        forgotten.Clear(); release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => old);
        Assert.Equal(destination, Assert.Single(connections.List()));
        Assert.Empty(forgotten);
    }

    private sealed class DelayedMembershipHandler(TaskCompletionSource started, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/v1/info")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryServiceInfo(1, "Test", 90, "domain", null)) };
            started.TrySetResult(); await release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DirectoryListing(1, [])) };
        }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
