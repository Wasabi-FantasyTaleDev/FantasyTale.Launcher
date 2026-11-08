using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Public synchronization result, retaining last-known-good metadata independently of a failed refresh.</summary>
/// <param name="SourceId">Configured source identity.</param>
/// <param name="Status">cached, checking, current or failed.</param>
/// <param name="CheckedAt">Last successful retrieval time, not the time a failed check was attempted.</param>
/// <param name="Failure">Safe network, timeout, integrity or disk classification.</param>
public sealed record StoreCatalogSyncState(string SourceId, string Status, DateTimeOffset? CheckedAt, string? Failure);

/// <summary>Shares bounded source synchronization and validated disk snapshots across catalog consumers.</summary>
public sealed class StoreCatalogCache : IAsyncDisposable
{
    private sealed class Entry
    {
        internal required StoreSource Source;
        internal StoreCatalog? Catalog;
        internal DateTimeOffset? Fetched;
        internal Task<StoreCatalog>? Pending;
        internal string? Failure;
    }

    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Func<StoreSource, IDownloadSource> _open;
    private readonly GitHubRateLimit _githubRateLimit = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;

    /// <summary>Opens the last-known-good cache without starting network traffic or modifying installed game files.</summary>
    public StoreCatalogCache(string root, HttpClient http, Func<StoreSource, IDownloadSource> openSource)
    {
        _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root);
        _http = http; _open = openSource;
    }

    /// <summary>Returns cached validated metadata immediately; changing a source endpoint cannot reuse another endpoint's cache.</summary>
    public StoreCatalog? Cached(StoreSource source)
    {
        lock (_gate) return GetEntry(source).Catalog;
    }

    /// <summary>Returns safe per-source progress without exposing storage URLs or raw server errors.</summary>
    public StoreCatalogSyncState State(StoreSource source)
    {
        lock (_gate)
        {
            var entry = GetEntry(source);
            return new(source.Id, entry.Pending is { IsCompleted: false } ? "checking" : entry.Failure is not null ? "failed"
                : entry.Fetched is null ? "cached" : "current", entry.Fetched, entry.Failure);
        }
    }

    /// <summary>Coalesces concurrent callers; canceling one caller does not cancel another view's shared bounded refresh.</summary>
    public Task<StoreCatalog> GetAsync(StoreSource source, bool force = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<StoreCatalog> work;
        lock (_gate)
        {
            var entry = GetEntry(source);
            if (entry.Pending is { IsCompleted: false }) work = entry.Pending;
            else if (!force && entry.Catalog is not null && entry.Fetched > DateTimeOffset.UtcNow.AddSeconds(-30))
                return Task.FromResult(entry.Catalog);
            else work = entry.Pending = RefreshAsync(entry);
        }
        return work.WaitAsync(cancellationToken);
    }

    /// <summary>Starts enabled source checks concurrently and waits for every completion or bounded failure.</summary>
    public async Task<IReadOnlyList<StoreCatalogSyncState>> SynchronizeAsync(IReadOnlyList<StoreSource> sources,
        CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(sources.Where(s => s.Enabled && s.BaseUrl is not null).Select(async source =>
        {
            try { await GetAsync(source, true, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (Expected(error))
            { cancellationToken.ThrowIfCancellationRequested(); }
        })).ConfigureAwait(false);
        return sources.Where(s => s.Enabled && s.BaseUrl is not null).Select(State).ToArray();
    }

    /// <summary>Applies source profiles over bundled fallback definitions without writing into the application directory.</summary>
    public IReadOnlyList<GameDefinition> Apply(IReadOnlyList<GameDefinition> games, IReadOnlyList<StoreSource> sources)
    {
        var profiles = sources.Where(s => s.Enabled && s.BaseUrl is not null).Select(s => Cached(s)).OfType<StoreCatalog>()
            .SelectMany(c => c.Profiles).GroupBy(p => (p.Game, p.Definition.Id)).Select(g => g.First()).ToArray();
        return games.Select(game => game with { Builds = game.Builds.Where(b => !profiles.Any(p => p.Game == game.Id && p.Definition.Id == b.Id))
            .Concat(profiles.Where(p => p.Game == game.Id).Select(p => p.Definition with
            { ClientPolicy = Plugins.ProfileClientPolicy.Combine(game.ClientPolicy, p.Definition.ClientPolicy) }))
            .OrderBy(b => b.Id, StringComparer.Ordinal).ToArray() }).ToArray();
    }

    /// <summary>Resolves directory defaults from the same shared catalog response without additional network requests.</summary>
    public IReadOnlyList<DirectorySource> DirectorySources(IReadOnlyList<StoreSource> sources) => Catalogs(sources)
        .SelectMany(catalog => catalog.DirectorySources).DistinctBy(source => source.Id, StringComparer.Ordinal).ToArray();

    private IEnumerable<StoreCatalog> Catalogs(IReadOnlyList<StoreSource> sources) => sources
        .Where(source => source.Enabled && source.BaseUrl is not null).Select(Cached).OfType<StoreCatalog>();

    private Entry GetEntry(StoreSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = StoreSourceIdentity.Of(source);
        if (_entries.TryGetValue(key, out var entry) && entry.Source.PreferIp == source.PreferIp) return entry;
        entry = new Entry { Source = source };
        var path = Path.Combine(_root, key + ".toml"); InstanceStore.RejectLinks(path);
        if (File.Exists(path))
        {
            try
            {
                entry.Catalog = StoreCatalogFile.Parse(source.Id, File.ReadAllText(path, new UTF8Encoding(false, true)));
                entry.Fetched = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception error) when (error is IOException or DefinitionException or DecoderFallbackException)
            { entry.Failure = "integrity"; }
        }
        _entries[key] = entry; return entry;
    }

    private async Task<StoreCatalog> RefreshAsync(Entry entry)
    {
        // Yield before transport callbacks: they may complete synchronously while GetAsync holds the cache lock.
        await Task.Yield();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var text = await SourceDocumentReader.ReadAsync(_http, _open(entry.Source), entry.Source.CatalogPath,
                deadline.Token).ConfigureAwait(false);
            var catalog = StoreCatalogFile.Parse(entry.Source.Id, text);
            var offline = false;
            if (catalog.GitHubReleases.Count > 0)
            {
                (text, offline) = await GitHubStoreReleases.ResolveAsync(_http, text, catalog, entry.Catalog, _githubRateLimit, deadline.Token).ConfigureAwait(false);
                catalog = StoreCatalogFile.Parse(entry.Source.Id, text);
            }
            lock (_gate)
            {
                if (entry.Catalog is { } previous)
                    foreach (var profile in catalog.Profiles)
                    {
                        var old = previous.Profiles.SingleOrDefault(p => p.Game == profile.Game && p.Definition.Id == profile.Definition.Id);
                        if (old is not null && (profile.Revision < old.Revision || profile.Revision == old.Revision && profile.Sha256 != old.Sha256))
                            throw new InvalidDataException("Profile revision regressed or changed without a revision increase.");
                    }
                var path = Path.Combine(_root, StoreSourceIdentity.Of(entry.Source) + ".toml");
                var temporary = path + ".tmp"; InstanceStore.RejectLinks(path); InstanceStore.RejectLinks(temporary);
                try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                entry.Catalog = catalog; entry.Fetched = DateTimeOffset.UtcNow; entry.Failure = offline ? "network" : null;
                return catalog;
            }
        }
        catch (Exception error) when (Expected(error))
        {
            lock (_gate) entry.Failure = error switch
            {
                OperationCanceledException => "timeout", HttpRequestException => "network",
                DefinitionException or InvalidDataException or DecoderFallbackException or System.Text.Json.JsonException => "integrity", _ => "disk",
            };
            throw;
        }
    }

    /// <summary>Cancels and drains shared requests before their HTTP clients are disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; pending = _entries.Values.Select(e => e.Pending).OfType<Task>().ToArray();
        }
        _stop.Cancel();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (Exception error) when (Expected(error)) { }
        _stop.Dispose();
    }

    private static bool Expected(Exception error) => error is HttpRequestException or IOException or InvalidDataException
        or DefinitionException or OperationCanceledException or InvalidOperationException or DecoderFallbackException or UnauthorizedAccessException or System.Text.Json.JsonException;
}
