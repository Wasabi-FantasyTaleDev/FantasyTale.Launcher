using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Public room publication state without directory management credentials.</summary>
/// <param name="ProfileId">Local hosting configuration.</param>
/// <param name="RunId">Exact owned server invocation.</param>
/// <param name="SourceId">Directory selected for this publication.</param>
/// <param name="RoomId">Published identity within that source.</param>
/// <param name="State">published, reconnecting, withdrawing, expired or withdrawn.</param>
/// <param name="ExpiresAt">Last expiry confirmed by the directory.</param>
/// <param name="Error">Bounded diagnostic text when communication failed.</param>
public sealed record HostedRoomStatus(string ProfileId, string RunId, string SourceId, string RoomId, string State, DateTimeOffset ExpiresAt, string? Error);

/// <summary>Keeps directory leases tied to ready owned server processes, not to a page or UI timer.</summary>
public sealed class HostedRoomPublisher : IAsyncDisposable
{
    private sealed class Publication(LocalServerProfile profile, string runId, DirectorySource source, DirectoryLease lease) : IDisposable
    {
        internal readonly LocalServerProfile Profile = profile;
        internal readonly string RunId = runId;
        internal readonly DirectorySource Source = source;
        internal DirectoryLease Lease = lease;
        internal DateTimeOffset Next = DateTimeOffset.UtcNow.AddSeconds(lease.RenewAfterSeconds);
        internal bool Withdrawing;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal Task? Operation;
        internal HostedRoomStatus Status = new(profile.Id, runId, source.Id, lease.Id, "published", lease.ExpiresAt, null);
        /// <summary>Releases the publication gate after its last request has completed.</summary>
        public void Dispose() => Gate.Dispose();
    }
    private readonly LocalServerService _hosting;
    private readonly DirectoryClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _requests = new(4, 4);
    private readonly Dictionary<string, Publication> _publications = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private bool _disposed;

    /// <summary>Starts a bounded backend heartbeat loop; construction publishes nothing and starts no servers.</summary>
    public HostedRoomPublisher(LocalServerService hosting, DirectoryClient client)
    { _hosting = hosting; _client = client; _loop = LoopAsync(); }

    /// <summary>Returns detached publication status snapshots for the frontend.</summary>
    public IReadOnlyList<HostedRoomStatus> List()
    { lock (_publications) return _publications.Values.Select(p => p.Status).ToArray(); }

    /// <summary>Publishes a ready room using its advertised API address, retaining source and run identity.</summary>
    public async Task<HostedRoomStatus> PublishAsync(LocalServerProfile profile, string runId, DirectorySource source, string description, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!source.Enabled || profile.Mode != "room" || !Ready(profile.Id, runId)) throw new InvalidOperationException("Start a room before publishing it to an enabled directory.");
            lock (_publications)
            {
                if (_publications.TryGetValue(profile.Id, out var prior) && prior.Status.State is not ("expired" or "withdrawn")) throw new InvalidOperationException("Withdraw the current publication before publishing again.");
            }
            var address = new UriBuilder("http", profile.AdvertisedHost, profile.ApiPort).Uri.AbsoluteUri;
            var lease = await _client.PublishAsync(source, new(profile.Game, profile.Name, address, [profile.Build], description), ct).ConfigureAwait(false);
            var publication = new Publication(profile, runId, source, lease);
            Publication? replaced;
            lock (_publications) { replaced = _publications.GetValueOrDefault(profile.Id); _publications[profile.Id] = publication; }
            if (replaced is not null)
            {
                await (replaced.Operation ?? Task.CompletedTask).ConfigureAwait(false);
                replaced.Dispose();
            }
            if (!Ready(profile.Id, runId))
            {
                publication.Withdrawing = true; await WithdrawCoreAsync(publication, CancellationToken.None).ConfigureAwait(false);
                lock (_publications) publication.Operation = Task.CompletedTask;
                throw new InvalidOperationException("The local server stopped while publishing its room.");
            }
            lock (_publications) publication.Operation = Task.CompletedTask;
            return publication.Status;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Stops renewal and attempts immediate withdrawal for one exact invocation.</summary>
    public async Task WithdrawAsync(string profileId, string runId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); Publication? publication;
            lock (_publications) publication = _publications.GetValueOrDefault(profileId);
            if (publication is null) return;
            if (publication.RunId != runId) throw new InvalidOperationException("The room invocation changed. Refresh its status.");
            await publication.Gate.WaitAsync(ct).ConfigureAwait(false);
            try { publication.Withdrawing = true; await WithdrawCoreAsync(publication, ct).ConfigureAwait(false); }
            finally { publication.Gate.Release(); }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Prevents editing the source while its room management authority remains in use.</summary>
    public bool UsesSource(string id)
    { lock (_publications) return _publications.Values.Any(p => p.Source.Id == id && p.Status.State is not ("expired" or "withdrawn")); }

    private bool Ready(string id, string runId) => _hosting.List().Any(s => s.ProfileId == id && s.RunId == runId && s.State == "ready") && _hosting.Destination(id) is not null;
    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                Publication[] publications; lock (_publications) publications = _publications.Values.ToArray();
                foreach (var publication in publications)
                {
                    Task? prior; lock (_publications) prior = publication.Operation;
                    if (prior is null || !prior.IsCompleted) continue;
                    await prior.ConfigureAwait(false);
                    // Each room keeps its own request; a stalled peer cannot block later ticks.
                    lock (_publications)
                        if (ReferenceEquals(_publications.GetValueOrDefault(publication.Profile.Id), publication))
                            publication.Operation = RefreshOwnedAsync(publication);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RefreshOwnedAsync(Publication publication)
    {
        try
        {
            await publication.Gate.WaitAsync(_stop.Token).ConfigureAwait(false);
            try
            {
                await _requests.WaitAsync(_stop.Token).ConfigureAwait(false);
                try { await RefreshPublicationAsync(publication).ConfigureAwait(false); }
                finally { _requests.Release(); }
            }
            finally { publication.Gate.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async ValueTask RefreshPublicationAsync(Publication publication)
    {
        if (publication.Status.State is "expired" or "withdrawn") return;
        var now = DateTimeOffset.UtcNow;
        if (publication.Lease.ExpiresAt <= now) { Set(publication, "expired", publication.Status.Error); return; }
        if (!Ready(publication.Profile.Id, publication.RunId)) publication.Withdrawing = true;
        if (publication.Withdrawing) { if (publication.Next <= now || publication.Status.State == "published") await WithdrawCoreAsync(publication, _stop.Token).ConfigureAwait(false); return; }
        if (publication.Next > now) return;
        try
        {
            publication.Lease = await _client.RenewAsync(publication.Source, publication.Lease, _stop.Token).ConfigureAwait(false);
            publication.Next = DateTimeOffset.UtcNow.AddSeconds(publication.Lease.RenewAfterSeconds);
            // The owned invocation may have stopped while the directory request was in flight.
            if (!Ready(publication.Profile.Id, publication.RunId))
            {
                publication.Withdrawing = true;
                await WithdrawCoreAsync(publication, _stop.Token).ConfigureAwait(false);
                return;
            }
            Set(publication, "published", null);
        }
        catch (Exception error) when (TransportError(error) && !_stop.IsCancellationRequested)
        {
            publication.Next = DateTimeOffset.UtcNow.AddSeconds(5);
            var ended = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.NotFound };
            Set(publication, ended ? "expired" : "reconnecting", SafeError(error));
            if (!ended && !Ready(publication.Profile.Id, publication.RunId))
            {
                publication.Withdrawing = true;
                await WithdrawCoreAsync(publication, _stop.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task WithdrawCoreAsync(Publication publication, CancellationToken ct)
    {
        if (publication.Status.State is "expired" or "withdrawn") return;
        Set(publication, "withdrawing", publication.Status.Error);
        try { await _client.WithdrawAsync(publication.Source, publication.Lease, ct).ConfigureAwait(false); Set(publication, "withdrawn", null); }
        catch (Exception error) when (TransportError(error))
        {
            publication.Next = DateTimeOffset.UtcNow.AddSeconds(5);
            Set(publication, publication.Lease.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "withdrawing", SafeError(error));
        }
    }
    // Invalid replies belong to the affected lease; they must not terminate the shared heartbeat loop.
    private static bool TransportError(Exception error) => error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or ArgumentException;
    private static string SafeError(Exception error) => error is HttpRequestException { StatusCode: { } status } ? $"HTTP {(int)status}" : "Directory connection failed.";
    private void Set(Publication publication, string state, string? error)
    { lock (_publications) publication.Status = publication.Status with { State = state, ExpiresAt = publication.Lease.ExpiresAt, Error = error }; }

    /// <summary>Ends heartbeats and withdraws rooms within one ten-second deadline; failed withdrawals retain their existing expiry.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await _loop.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return; _disposed = true;
            Publication[] publications; lock (_publications) publications = _publications.Values.ToArray();
            await Task.WhenAll(publications.Select(p => p.Operation ?? Task.CompletedTask)).ConfigureAwait(false);
            // Shutdown has one transport deadline, rather than one full timeout
            // per room, so an unavailable directory cannot postpone server cleanup.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Parallel.ForEachAsync(publications, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (publication, _) =>
            {
                publication.Withdrawing = true;
                await WithdrawCoreAsync(publication, deadline.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            foreach (var publication in publications) publication.Dispose();
        }
        finally { _gate.Release(); }
    }
}
