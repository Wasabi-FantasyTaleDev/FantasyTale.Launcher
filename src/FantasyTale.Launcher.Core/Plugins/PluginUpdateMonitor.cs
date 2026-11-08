using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Identifies a client and the game/build scope whose library results it shares.</summary>
/// <param name="InstanceId">Managed client identity.</param>
/// <param name="Scope">Stable game/build identity; equal scopes must have identical discovery inputs.</param>
public sealed record PluginUpdateTarget(string InstanceId, string Scope);

/// <summary>Read-only discovery state; it neither downloads nor activates a plugin.</summary>
/// <param name="Checking">Whether background discovery is in progress.</param>
/// <param name="CheckedAt">Last successful discovery time in UTC, or null before discovery.</param>
/// <param name="Updates">Source-bound results for this client's game/build.</param>
/// <param name="Error">Safe failure category without source addresses or credentials.</param>
public sealed record PluginUpdateSnapshot(bool Checking, DateTimeOffset? CheckedAt, IReadOnlyList<PluginUpdateState> Updates, string? Error);

/// <summary>Owns bounded periodic discovery, optional shared upgrade scheduling and cached results while the plugin page is closed.</summary>
public sealed class PluginUpdateMonitor : IAsyncDisposable
{
    private readonly Func<IReadOnlyList<PluginUpdateTarget>> _targets;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<PluginUpdateState>>> _check;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _autoUpdate;
    private readonly Func<PluginUpdateTarget, IReadOnlyList<PluginUpdateState>, CancellationToken, Task>? _install;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _gate = new();
    private Dictionary<string, PluginUpdateSnapshot> _snapshots = new(StringComparer.Ordinal);
    private Dictionary<string, string> _snapshotScopes = new(StringComparer.Ordinal);
    private Task? _monitor;
    private bool _checking;
    private string? _error;
    private long _revision;

    /// <summary>Connects passive discovery to current clients and persisted preferences; the default interval is thirty minutes.</summary>
    public PluginUpdateMonitor(Func<IReadOnlyList<PluginUpdateTarget>> targets,
        Func<string, CancellationToken, Task<IReadOnlyList<PluginUpdateState>>> check, Func<bool> enabled,
        TimeSpan? interval = null, Func<bool>? autoUpdate = null,
        Func<PluginUpdateTarget, IReadOnlyList<PluginUpdateState>, CancellationToken, Task>? install = null)
    {
        _targets = targets; _check = check; _enabled = enabled; _autoUpdate = autoUpdate ?? (() => false); _install = install; _interval = interval ?? TimeSpan.FromMinutes(30);
        if (_interval <= TimeSpan.Zero || _interval > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(interval));
    }

    /// <summary>Starts one lifetime-owned loop without blocking frontend initialization.</summary>
    public void StartMonitoring() { lock (_gate) _monitor ??= Task.Run(MonitorAsync); }

    /// <summary>Reevaluates saved preferences without creating another monitor or queueing unbounded work.</summary>
    public void Wake() { lock (_gate) if (_wake.CurrentCount == 0) _wake.Release(); }

    /// <summary>Reads cached discovery without sending network requests.</summary>
    public PluginUpdateSnapshot Read(string instanceId)
    {
        var target = _targets().SingleOrDefault(t => t.InstanceId == instanceId);
        lock (_gate)
        {
            if (target is null || _snapshotScopes.GetValueOrDefault(instanceId) != target.Scope)
            {
                _snapshots.Remove(instanceId); _snapshotScopes.Remove(instanceId);
            }
            var snapshot = _snapshots.GetValueOrDefault(instanceId) ?? new(false, null, [], null);
            return snapshot with { Checking = _checking, Error = _error ?? snapshot.Error };
        }
    }

    /// <summary>Records a foreground result only for its original client scope and fences out an older background pass.</summary>
    public void Remember(PluginUpdateTarget target, IReadOnlyList<PluginUpdateState> updates)
    {
        var current = _targets().SingleOrDefault(t => t.InstanceId == target.InstanceId);
        if (current != target) return;
        var instanceId = target.InstanceId;
        lock (_gate)
        {
            _revision++;
            _snapshotScopes[instanceId] = target.Scope;
            _snapshots[instanceId] = new(false, DateTimeOffset.UtcNow, updates.ToArray(), null); _error = null;
        }
    }

    /// <summary>Checks installed clients, sharing results per game/build and limiting a pass to two minutes.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        if (!await _operation.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        long revision;
        lock (_gate) { _checking = true; _error = null; revision = _revision; }
        try
        {
            var targets = _targets();
            var results = new Dictionary<string, PluginUpdateSnapshot>(StringComparer.Ordinal);
            foreach (var scope in targets.GroupBy(t => t.Scope, StringComparer.Ordinal))
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    var updates = await _check(scope.First().InstanceId, deadline.Token).ConfigureAwait(false);
                    // Publish discovery before download scheduling so launch prompts can show active transfers.
                    lock (_gate)
                    {
                        if (revision != _revision) continue;
                        var previous = _snapshotScopes.GetValueOrDefault(scope.First().InstanceId) == scope.Key
                            ? _snapshots.GetValueOrDefault(scope.First().InstanceId) : null;
                        updates = updates.Select(update => update.Status is "network" or "integrity" or "disk" or "source"
                            ? previous?.Updates.FirstOrDefault(old => old.Id == update.Id && old.InstalledVersion == update.InstalledVersion && old.Version is not null) ?? update
                            : update).ToArray();
                        if (revision == _revision)
                            foreach (var target in scope)
                            {
                                _snapshotScopes[target.InstanceId] = target.Scope;
                                _snapshots[target.InstanceId] = new(false, DateTimeOffset.UtcNow, updates.ToArray(), null);
                            }
                    }
                    var snapshot = new PluginUpdateSnapshot(false, DateTimeOffset.UtcNow, updates.ToArray(), null);
                    foreach (var target in scope) results.Add(target.InstanceId, snapshot);
                    if (_autoUpdate() && _install is not null && updates.Any(update => update.Status is "available" or "installed"))
                        foreach (var target in scope)
                        {
                            if (!_autoUpdate()) break;
                            if (_targets().Contains(target))
                            {
                                try { await _install(target, updates, deadline.Token).ConfigureAwait(false); }
                                catch (Exception error) when (!deadline.IsCancellationRequested && IsDiscoveryFailure(error))
                                { results[target.InstanceId] = snapshot with { Error = FailureCategory(error) }; }
                            }
                        }
                }
                catch (Exception error) when (!deadline.IsCancellationRequested && IsDiscoveryFailure(error))
                {
                    // A failing source must not hide successful discovery for other client scopes.
                    lock (_gate)
                        foreach (var target in scope)
                        {
                            var previous = _snapshotScopes.GetValueOrDefault(target.InstanceId) == scope.Key
                                ? _snapshots.GetValueOrDefault(target.InstanceId) : null;
                            previous ??= new(false, null, [], null);
                            results.Add(target.InstanceId, previous with { Error = FailureCategory(error) });
                        }
                }
            }
            var current = _targets().ToDictionary(t => t.InstanceId, t => t.Scope, StringComparer.Ordinal);
            foreach (var target in targets)
                if (!current.TryGetValue(target.InstanceId, out var scope) || scope != target.Scope) results.Remove(target.InstanceId);
            lock (_gate)
                if (revision == _revision)
                {
                    _snapshots = results;
                    _snapshotScopes = targets.Where(t => results.ContainsKey(t.InstanceId))
                        .ToDictionary(t => t.InstanceId, t => t.Scope, StringComparer.Ordinal);
                }
        }
        catch (Exception error) when (!linked.IsCancellationRequested && IsDiscoveryFailure(error))
        {
            lock (_gate) if (revision == _revision) _error = FailureCategory(error);
        }
        finally { lock (_gate) _checking = false; _operation.Release(); }
    }

    private static bool IsDiscoveryFailure(Exception error) => error is IOException or UnauthorizedAccessException or
        InvalidOperationException or InvalidDataException or ArgumentException or DefinitionException or HttpRequestException or OperationCanceledException;

    private static string FailureCategory(Exception error) => error is HttpRequestException or OperationCanceledException ? "network" :
        error is IOException or UnauthorizedAccessException ? "disk" : "integrity";

    private async Task MonitorAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (_enabled()) await CheckAsync(_stop.Token).ConfigureAwait(false);
                await _wake.WaitAsync(_interval, _stop.Token).ConfigureAwait(false);
            }
        }
        // A client may disappear while shutdown cancels a discovery pass that already opened it.
        catch (Exception error) when (_stop.IsCancellationRequested && IsDiscoveryFailure(error)) { }
    }

    /// <summary>Cancels and drains periodic and explicit checks before their dependencies close.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        Task? monitor; lock (_gate) monitor = _monitor;
        if (monitor is not null) await monitor.ConfigureAwait(false);
        await _operation.WaitAsync().ConfigureAwait(false); _operation.Release();
    }
}
