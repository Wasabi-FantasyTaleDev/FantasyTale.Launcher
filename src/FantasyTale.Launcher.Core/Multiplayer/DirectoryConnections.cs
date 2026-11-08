using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Keeps joined rooms transient and rechecks source membership before sending account requests.</summary>
public sealed class DirectoryConnections : IAsyncDisposable
{
    private sealed record Joined(DirectorySource Source, DirectoryEntry Entry, string Build, ServerDefinition Server, Guid Generation);
    private readonly Dictionary<string, Joined> _joined = new(StringComparer.Ordinal);
    private readonly Func<DirectorySource, CancellationToken, Task<DirectoryListing>> _readListing;
    private readonly Action<string> _forget;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private bool _disposed;

    /// <summary>Starts passive membership refresh; account invalidation is delegated to the host session owner.</summary>
    public DirectoryConnections(DirectoryClient client, Action<string> forget, TimeProvider? clock = null,
        Func<DirectorySource, CancellationToken, Task<DirectoryListing>>? readListing = null)
    { _readListing = readListing ?? client.ListAsync; _forget = forget; _clock = clock ?? TimeProvider.System; _loop = LoopAsync(); }

    /// <summary>Builds a destination whose identity includes both source and entry, preventing cross-source account reuse.</summary>
    public static ServerDefinition Destination(DirectorySource source, DirectoryEntry entry)
    {
        DirectoryValidation.Id(entry.Id); var details = DirectoryValidation.Normalize(entry.Details);
        if (entry.Kind is not ("server" or "room")) throw new ArgumentException("Unknown directory entry kind.");
        var identity = ServerStore.ApiBase(source.BaseUrl ?? throw new ArgumentException("Directory address is required.")).AbsoluteUri + "\n"
            + source.StoreSourceId + "\n" + entry.Kind + "\n" + entry.Id;
        var id = "directory-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        return new(id, details.Game, details.Name, details.ApiUrl, details.Endpoint, true, entry.Kind == "room") { Kind = entry.Kind };
    }

    /// <summary>Retains a room only after the host has validated its actual server catalog and selected build.</summary>
    public void Join(DirectorySource source, DirectoryEntry entry, string build)
    {
        var server = Destination(source, entry);
        var now = _clock.GetUtcNow();
        if (entry.Kind != "room" || entry.ExpiresAt is null || entry.ExpiresAt <= now || entry.ExpiresAt > now.AddMinutes(6) || !entry.Details.Builds.Contains(build, StringComparer.Ordinal)) throw new InvalidOperationException("The room is unavailable for this build.");
        lock (_joined)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RemoveExpired();
            if (_joined.TryGetValue(server.Id, out var previous) && previous.Server.ApiUrl != server.ApiUrl) _forget(server.Id);
            _joined[server.Id] = new(source, entry, build, server, Guid.NewGuid());
        }
    }

    /// <summary>Lists rooms whose last confirmed source lease has not elapsed.</summary>
    public IReadOnlyList<ServerDefinition> List()
    { lock (_joined) { RemoveExpired(); return _joined.Values.Select(j => j.Server).ToArray(); } }

    /// <summary>Rechecks membership and immutable destination metadata before returning a joined room.</summary>
    public async Task<ServerDefinition?> ResolveAsync(string id, CancellationToken ct)
    {
        Joined? joined;
        lock (_joined)
        {
            joined = _joined.GetValueOrDefault(id);
            if (joined is not null && joined.Entry.ExpiresAt <= _clock.GetUtcNow())
            {
                _joined.Remove(id); _forget(id);
                throw new InvalidOperationException("The room lease expired. Join it again.");
            }
        }
        if (joined is null) return null;
        var listing = await _readListing(joined.Source, ct).ConfigureAwait(false);
        var current = listing.Entries.SingleOrDefault(e => e.Id == joined.Entry.Id);
        return Confirm(joined, current);
    }

    private ServerDefinition Confirm(Joined joined, DirectoryEntry? current)
    {
        var id = joined.Server.Id;
        lock (_joined)
        {
            var latest = _joined.GetValueOrDefault(id);
            if (latest is null || latest.Generation != joined.Generation)
                throw new InvalidOperationException("The room selection changed. Join it again.");
            if (current is null || current.Kind != "room" || current.ExpiresAt is null || current.ExpiresAt <= _clock.GetUtcNow() || current.Details.Game != joined.Server.Game ||
                ServerStore.ApiBase(current.Details.ApiUrl).AbsoluteUri != joined.Server.ApiUrl || !current.Details.Builds.Contains(joined.Build, StringComparer.Ordinal))
            {
                _joined.Remove(id); _forget(id); throw new InvalidOperationException("The room ended or changed its connection details. Join it again.");
            }
            _joined[id] = joined with { Entry = current }; return joined.Server;
        }
    }

    private void RemoveExpired()
    {
        var now = _clock.GetUtcNow();
        foreach (var id in _joined.Where(pair => pair.Value.Entry.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        { _joined.Remove(id); _forget(id); }
    }

    /// <summary>Invalidates source-bound account sessions before changing or removing a directory source.</summary>
    public void DisconnectSource(string sourceId)
    {
        lock (_joined)
            foreach (var id in _joined.Where(p => p.Value.Source.Id == sourceId).Select(p => p.Key).ToArray()) { _joined.Remove(id); _forget(id); }
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                Joined[] rooms; lock (_joined) { RemoveExpired(); rooms = _joined.Values.ToArray(); }
                // One listing covers every joined room from a source; independent sources refresh concurrently.
                await Parallel.ForEachAsync(rooms.GroupBy(room => room.Source),
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = _stop.Token }, async (group, ct) =>
                {
                    try
                    {
                        var listing = await _readListing(group.Key, ct).ConfigureAwait(false);
                        var entries = listing.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
                        foreach (var room in group)
                        {
                            try { Confirm(room, entries.GetValueOrDefault(room.Entry.Id)); }
                            catch (InvalidOperationException) { }
                        }
                    }
                    catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException)
                    {
                        lock (_joined) RemoveExpired();
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>Stops membership refresh and discards all transient room accounts.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await _loop.ConfigureAwait(false);
        lock (_joined) { _disposed = true; foreach (var id in _joined.Keys) _forget(id); _joined.Clear(); }
    }
}
