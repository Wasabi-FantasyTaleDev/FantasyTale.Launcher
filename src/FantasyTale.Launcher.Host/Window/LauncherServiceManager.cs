using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

// Per-client composition only. Providers remain optional and never authorize a network session themselves.
internal sealed class LauncherServiceManager : IAsyncDisposable
{
    internal delegate Task<CompanionSession?> CompanionFactory(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyDictionary<string, int> messages, CompanionLimits limits, CancellationToken cancellationToken);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly CompanionFactory? _companionFactory;
    private readonly Func<string, IOverlayHost?> _overlay;
    private readonly Func<string, string, IOverlayPanel?> _entryPanel;
    private readonly CancellationTokenSource _stop = new();
    private Task? _dispose;
    private readonly Action<string, string>? _record;

    internal LauncherServiceManager(Func<string, IOverlayHost?> overlay, CompanionFactory? companionFactory = null,
        Func<string, string, IOverlayPanel?>? entryPanel = null, Action<string, string>? record = null)
    {
        _overlay = overlay; _companionFactory = companionFactory; _entryPanel = entryPanel ?? ((_, _) => null); _record = record;
    }

    internal void StartForClient(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<ResolvedLauncherService> plan,
        Task<LaunchResult> gameCompletion, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? nativeConsumers = null,
        HookChannel? control = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? panelConsumers = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? controllerConsumers = null)
    {
        nativeConsumers ??= new Dictionary<string, IReadOnlyDictionary<string, int>>();
        panelConsumers ??= new Dictionary<string, IReadOnlyDictionary<string, int>>();
        controllerConsumers ??= new Dictionary<string, IReadOnlyDictionary<string, int>>();
        if (plan.Count == 0 && nativeConsumers.Count == 0 && panelConsumers.Count == 0 && controllerConsumers.Count == 0) return;
        _ = LauncherServicePlan.MessageGrants(plan, Consumers(nativeConsumers, panelConsumers, controllerConsumers));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            if (_entries.Count >= 32) throw new InvalidOperationException("Launcher session service limit reached.");
            var entry = new Entry(plan, nativeConsumers, panelConsumers, controllerConsumers, control);
            if (!_entries.TryAdd(instance.Id, entry)) throw new InvalidOperationException("Launcher services already own this client.");
            entry.Work = Task.Run(() => RunAsync(instance, build, entry, gameCompletion));
        }
    }

    internal async Task<ICompanionChannel?> GetControllerChannelAsync(string instanceId, string owner, CancellationToken cancellationToken = default)
    {
        Entry? entry;
        lock (_gate) _entries.TryGetValue(instanceId, out entry);
        if (entry is null || !entry.ControllerConsumers.ContainsKey(owner)) return null;
        var channels = await entry.ControllerChannels.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return channels.GetValueOrDefault(owner);
    }

    internal IReadOnlyDictionary<string, string> Status(string instanceId)
    {
        lock (_gate) return _entries.TryGetValue(instanceId, out var entry)
            ? entry.Status.ToDictionary(StringComparer.Ordinal) : new Dictionary<string, string>();
    }

    private async Task RunAsync(InstanceDefinition instance, BuildDefinition build, Entry entry, Task<LaunchResult> gameCompletion)
    {
        CompanionSession? companion = null;
        CompanionOverlayNotifications? notifications = null;
        ICompanionChannel? channel = null;
        ICompanionChannel? nativeChannel = null;
        CompanionNativeUiRequests? nativeRequests = null;
        var controllers = new Dictionary<string, ICompanionChannel>(StringComparer.Ordinal);
        var bridges = new Dictionary<string, (PanelCompanionBridge Bridge, ICompanionChannel Channel)>(StringComparer.Ordinal);
        using var native = new NativeCompanionRouter(entry.Consumers, (owner, grants) => Volatile.Read(ref companion)?.CreateChannel(owner, grants));
        entry.Control?.SetCompanionHandler(native.HandleAsync);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        try
        {
            var transport = entry.Plan.SingleOrDefault(p => p.Plugin.Service!.Provider == "companion");
            var notification = entry.Plan.SingleOrDefault(p => p.Plugin.Service!.Provider == "overlay-notifications")
                ?? (transport?.Plugin.Service!.Messages.ContainsKey("ui.notification.show") == true ? transport : null);
            void NotificationStatus(string status)
            {
                if (notification is not null && notification != transport) SetStatus(entry, notification.Plugin.Id, status);
            }
            if (transport is null)
            {
                entry.ControllerChannels.TrySetResult(controllers);
                foreach (var owner in entry.ConsumerIds) SetStatus(entry, owner, "dependency-missing");
                if (notification is not null && notification != transport) SetStatus(entry, notification.Plugin.Id, "dependency-missing");
                await gameCompletion.WaitAsync(lifetime.Token).ConfigureAwait(false);
                return;
            }
            if (_companionFactory is null)
            {
                entry.ControllerChannels.TrySetResult(controllers);
                foreach (var owner in entry.ConsumerIds) SetStatus(entry, owner, "server-unavailable");
                SetStatus(entry, transport.Plugin.Id, "server-unavailable");
                if (notification is not null && notification != transport) SetStatus(entry, notification.Plugin.Id, "dependency-missing");
                await gameCompletion.WaitAsync(lifetime.Token).ConfigureAwait(false);
                return;
            }
            var messages = LauncherServicePlan.MessageGrants(entry.Plan, Consumers(entry.Consumers, entry.PanelConsumers, entry.ControllerConsumers));
            var limits = new CompanionLimits
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(Option(transport, "heartbeat_interval_ms", 20000)),
                RequestTimeout = TimeSpan.FromMilliseconds(Option(transport, "request_timeout_ms", 10000)),
            };
            // Server-issued launch identity and grant acquisition are supplied by the account integration.
            // Never invent a launch id from PID, username, the Overlay pipe or a local random value.
            var preparing = _companionFactory(instance, build, messages, limits, lifetime.Token);
            if (await Task.WhenAny(preparing, gameCompletion).ConfigureAwait(false) == gameCompletion) lifetime.Cancel();
            companion = await preparing.ConfigureAwait(false);
            if (companion is null)
            {
                entry.ControllerChannels.TrySetResult(controllers);
                foreach (var owner in entry.ConsumerIds) SetStatus(entry, owner, "server-unavailable");
                SetStatus(entry, transport.Plugin.Id, "server-unavailable");
                if (notification is not null && notification != transport) SetStatus(entry, notification.Plugin.Id, "dependency-missing");
                await gameCompletion.WaitAsync(lifetime.Token).ConfigureAwait(false);
                return;
            }
            foreach (var (owner, grants) in entry.ControllerConsumers) controllers.Add(owner, companion.CreateChannel(owner, grants));
            entry.ControllerChannels.TrySetResult(controllers);
            var running = companion.Start();
            SetStatus(entry, transport.Plugin.Id, "connecting");
            while (!gameCompletion.IsCompleted && !lifetime.IsCancellationRequested && !running.IsCompleted)
            {
                SetStatus(entry, transport.Plugin.Id, companion.State.ToString().ToLowerInvariant());
                foreach (var owner in entry.ConsumerIds) SetStatus(entry, owner, companion.State.ToString().ToLowerInvariant());
                if (notification is not null && notifications is null)
                {
                    var overlay = notification == transport ? new DeferredOverlay(() => _overlay(instance.Id)) : _overlay(instance.Id);
                    if (overlay is null) NotificationStatus("overlay-unavailable");
                    else if (companion.State == CompanionChannelState.Connected)
                    {
                        channel ??= companion.CreateChannel(notification.Plugin.Id, notification.Plugin.Service!.Messages);
                        if (notification.Plugin.Service!.Messages.Keys.All(channel.Capabilities.Contains))
                        {
                            notifications = new(channel, overlay, (int)Option(notification, "max_dialogs", 4),
                                native: entry.Control is null ? null : new GameNativeUiClient(entry.Control, build.Id));
                            NotificationStatus("ready");
                        }
                        else NotificationStatus("server-unavailable");
                    }
                }
                if (companion.State == CompanionChannelState.Connected) BridgePanels(instance, entry, companion, bridges);
                // The server's native UI requests: the game's own controls first, the overlay as the fallback.
                if (nativeRequests is null && companion.State == CompanionChannelState.Connected
                    && transport.Plugin.Service!.Messages.ContainsKey(CompanionNativeUiRequests.InvokeType))
                {
                    nativeChannel ??= companion.CreateChannel("fantasytale.native-ui", new Dictionary<string, int>(StringComparer.Ordinal)
                        { [CompanionNativeUiRequests.InvokeType] = 1, [CompanionNativeUiRequests.ResultType] = 1 });
                    if (nativeChannel.Capabilities.Contains(CompanionNativeUiRequests.InvokeType))
                        nativeRequests = new(nativeChannel, entry.Control is null ? null : new GameNativeUiClient(entry.Control, build.Id),
                            new DeferredOverlay(() => _overlay(instance.Id)));
                }
                foreach (var (_, bridge) in bridges) bridge.Bridge.Refresh();
                if (notification is not null && notifications is not null)
                {
                    var status = notifications.DeliveryFailed ? "delivery-failed"
                        : channel!.State != CompanionChannelState.Connected ? "dependency-missing"
                        : !notification.Plugin.Service!.Messages.Keys.All(channel.Capabilities.Contains) ? "server-unavailable" : "ready";
                    NotificationStatus(status);
                }
                await Task.WhenAny(gameCompletion, Task.Delay(500, lifetime.Token)).ConfigureAwait(false);
            }
            if (!gameCompletion.IsCompleted && running.IsCompleted)
            {
                await running.ConfigureAwait(false);
                foreach (var owner in entry.ConsumerIds) SetStatus(entry, owner, "dependency-missing");
                SetStatus(entry, transport.Plugin.Id, companion.State.ToString().ToLowerInvariant());
                if (notification is not null && notification != transport) SetStatus(entry, notification.Plugin.Id, "dependency-missing");
                await gameCompletion.WaitAsync(lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            entry.ControllerChannels.TrySetResult(new Dictionary<string, ICompanionChannel>());
            lock (_gate)
                foreach (var id in entry.Status.Keys.ToArray()) entry.Status[id] = "failed";
            // Keep a content-free failure visible for this game's lifetime, including failed authorization.
            try { await gameCompletion.WaitAsync(lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
        finally
        {
            entry.ControllerChannels.TrySetResult(new Dictionary<string, ICompanionChannel>());
            lifetime.Cancel();
            try
            {
                try { native.Dispose(); }
                finally
                {
                    try
                    {
                        foreach (var controller in controllers.Values) controller.Dispose();
                        foreach (var (_, bridge) in bridges) { bridge.Bridge.Dispose(); bridge.Channel.Dispose(); }
                        if (nativeRequests is not null) await nativeRequests.DisposeAsync().ConfigureAwait(false);
                        nativeChannel?.Dispose();
                        if (notifications is not null) await notifications.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        try { channel?.Dispose(); }
                        finally { if (companion is not null) await companion.DisposeAsync().ConfigureAwait(false); }
                    }
                }
            }
            finally
            {
                lock (_gate)
                    if (_entries.TryGetValue(instance.Id, out var current) && ReferenceEquals(current, entry)) _entries.Remove(instance.Id);
            }
        }
    }

    // A package's entry panel appears once the overlay is ready; each gets its own channel, limited to its own grants.
    private void BridgePanels(InstanceDefinition instance, Entry entry, CompanionSession companion,
        Dictionary<string, (PanelCompanionBridge Bridge, ICompanionChannel Channel)> bridges)
    {
        foreach (var (owner, grants) in entry.PanelConsumers)
        {
            if (bridges.ContainsKey(owner) || _entryPanel(instance.Id, owner) is not { } panel) continue;
            var channel = companion.CreateChannel(owner, grants);
            try { bridges.Add(owner, (new PanelCompanionBridge(panel, channel, grants), channel)); }
            catch { channel.Dispose(); throw; }
            SetStatus(entry, owner, "ready");
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> Consumers(
        params IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>[] sources)
    {
        var all = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var source in sources)
            foreach (var (owner, grants) in source)
                if (!all.TryAdd(owner, grants)) throw new InvalidOperationException("A package cannot have multiple companion channel owners.");
        return all;
    }

    private void SetStatus(Entry entry, string id, string status)
    {
        bool changed;
        lock (_gate) { changed = !entry.Status.TryGetValue(id, out var previous) || previous != status; entry.Status[id] = status; }
        // Status transitions only: package ids and state names, never grants or endpoints.
        if (changed) _record?.Invoke("launch-companion-status", id + "-" + status);
    }

    private static long Option(ResolvedLauncherService service, string key, long fallback) =>
        service.Options.TryGetValue(key, out var value) && value is long number ? number : fallback;

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= Task.Run(DisposeCoreAsync));
    }

    private async Task DisposeCoreAsync()
    {
        _stop.Cancel();
        Task[] tasks;
        lock (_gate) tasks = _entries.Values.Select(e => e.Work).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }

    private sealed class Entry(IReadOnlyList<ResolvedLauncherService> plan,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> consumers,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> panelConsumers,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> controllerConsumers, HookChannel? control)
    {
        internal IReadOnlyList<ResolvedLauncherService> Plan { get; } = plan;
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Consumers { get; } = consumers;
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> PanelConsumers { get; } = panelConsumers;
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ControllerConsumers { get; } = controllerConsumers;
        internal TaskCompletionSource<IReadOnlyDictionary<string, ICompanionChannel>> ControllerChannels { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<string> ConsumerIds { get; } = [.. consumers.Keys, .. panelConsumers.Keys, .. controllerConsumers.Keys];
        internal HookChannel? Control { get; } = control;
        internal Dictionary<string, string> Status { get; } = plan.Select(p => p.Plugin.Id).Concat(consumers.Keys).Concat(panelConsumers.Keys).Concat(controllerConsumers.Keys)
            .ToDictionary(id => id, _ => "preparing", StringComparer.Ordinal);
        internal Task Work { get; set; } = Task.CompletedTask;
    }

    // Communication can run without Overlay; resolve it only when a notification actually needs presentation.
    private sealed class DeferredOverlay(Func<IOverlayHost?> resolve) : IOverlayHost
    {
        private IOverlayHost Current => resolve() ?? throw new NotSupportedException("Overlay is unavailable for this client.");
        public OverlayBuildContext? BuildContext => Current.BuildContext;
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) => Current.WaitUntilReadyAsync(cancellationToken);
        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) => Current.RegisterPanel(panelId, descriptor);
    }
}
