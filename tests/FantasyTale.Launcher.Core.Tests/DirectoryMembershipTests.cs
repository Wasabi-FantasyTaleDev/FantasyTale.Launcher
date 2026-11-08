using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DirectoryMembershipTests
{
    private static readonly DirectorySource Source = new("source", "Source", "https://directory.example/", true, false);
    private static DirectoryEntry Room(string id) => new(id, "room",
        new("fixture", id, "https://game.example/", ["kr-1256"], ""), DateTimeOffset.UtcNow.AddMinutes(2));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task PassiveRefreshReadsOneListingForMoreThanTheFormerRoomCapacity()
    {
        using var clock = new ManualClock(); var called = Signal(); var calls = 0;
        using var client = new DirectoryClient();
        var rooms = Enumerable.Range(0, 97).Select(i => Room("room-" + i)).ToArray();
        await using var connections = new DirectoryConnections(client, _ => { }, clock, (_, _) =>
        {
            Interlocked.Increment(ref calls); called.TrySetResult(); return Task.FromResult(new DirectoryListing(1, rooms));
        });
        foreach (var room in rooms) connections.Join(Source, room, "kr-1256");
        Assert.Equal(rooms.Length, connections.List().Count);
        clock.Tick(); await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(rooms.Length, connections.List().Count);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task SlowSourceDoesNotPreventAnotherSourceFromInvalidatingAnEndedRoom()
    {
        using var clock = new ManualClock(); var slow = Signal(); var ended = Signal();
        using var client = new DirectoryClient();
        await using var connections = new DirectoryConnections(client, _ => ended.TrySetResult(), clock, async (source, ct) =>
        {
            if (source.Id == "source") { slow.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            return new(1, []);
        });
        connections.Join(Source, Room("slow"), "kr-1256");
        connections.Join(Source with { Id = "other", BaseUrl = "https://other.example/" }, Room("ended"), "kr-1256");
        clock.Tick();
        await slow.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("slow", Assert.Single(connections.List()).Name);
    }

    [Fact]
    public async Task DelayedPassiveListingCannotDiscardARejoinedRoom()
    {
        using var clock = new ManualClock(); var started = Signal(); var release = Signal(); var completed = Signal(); var nextRound = Signal(); var calls = 0;
        var forgotten = new ConcurrentBag<string>(); using var client = new DirectoryClient();
        var room = Room("same");
        await using var connections = new DirectoryConnections(client, forgotten.Add, clock, async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) > 1) { nextRound.TrySetResult(); return new(1, [room]); }
            started.TrySetResult(); await release.Task.WaitAsync(ct); completed.TrySetResult(); return new(1, []);
        });
        connections.Join(Source, room, "kr-1256");
        clock.Tick(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connections.Join(Source, room, "kr-1256"); release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Tick(); await nextRound.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(connections.List()); Assert.Empty(forgotten);
    }

    [Fact]
    public async Task MalformedSourceDoesNotTerminateLaterMembershipRefreshes()
    {
        using var clock = new ManualClock(); var failed = Signal(); var removed = Signal(); var calls = 0;
        using var client = new DirectoryClient();
        await using var connections = new DirectoryConnections(client, _ => removed.TrySetResult(), clock, (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { failed.TrySetResult(); throw new InvalidDataException("Malformed listing"); }
            return Task.FromResult(new DirectoryListing(1, []));
        });
        connections.Join(Source, Room("retry"), "kr-1256");
        clock.Tick(); await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Tick(); await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(connections.List());
    }

    [Fact]
    public async Task DirectoryTransportAcceptsManySourcesAndListingsBeyondFormerRowAndByteCeilings()
    {
        var entries = Enumerable.Range(0, 2051).Select(i => Room("entry-" + i) with { Kind = "server", ExpiresAt = null }).ToArray();
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/v1/info"
            ? JsonContent.Create(new { schema = 1, name = "Source", roomLeaseSeconds = 90, hostPolicy = "domain", padding = new string(' ', 17 * 1024) })
            : JsonContent.Create(new { schema = 1, entries, padding = new string(' ', 21 * 1024 * 1024) }));
        using var http = new HttpClient(handler); using var client = new DirectoryClient(http);
        for (var i = 0; i < 67; i++) await client.InfoAsync(Source with { BaseUrl = $"https://directory-{i}.example/" });
        var listing = await client.ListAsync(Source);
        Assert.Equal(entries.Length, listing.Entries.Count);
        Assert.Equal(entries[^1].Id, listing.Entries[^1].Id);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpContent> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = reply(request) });
    }

    private sealed class ManualClock : TimeProvider, IDisposable
    {
        private ManualTimer? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _timer = new(callback, state);
        internal void Tick() => _timer!.Tick();
        public void Dispose() => _timer?.Dispose();
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            internal void Tick() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
