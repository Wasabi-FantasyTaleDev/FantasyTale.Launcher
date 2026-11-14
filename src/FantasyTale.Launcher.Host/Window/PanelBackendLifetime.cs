using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

// Controller lifetime belongs to the game session, independently of page readiness, visibility and suspension.
internal sealed class PanelBackendLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<IAsyncDisposable> _backends = [];
    private Task? _dispose;

    internal void Start(PluginDefinition package, BuildDefinition build, IOverlayPanel panel, IGameNativeUi? native,
        string? locale, IGameOverlayControllerProvider? provider, Action<string, string>? record = null, ICompanionChannel? companion = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            if (package.Overlay?.Controller is not null)
            {
                if (provider is null)
                    throw new InvalidOperationException("The game module does not provide the declared overlay controller.");
                var scoped = native is IGameNativeUiScopes scopes ? scopes.ForOwner(package.Id) : native;
                var controller = provider.StartController(new(package, build, panel, scoped, locale) { Record = record, Companion = companion })
                    ?? throw new InvalidOperationException("The game module did not return an overlay controller lifetime.");
                _backends.Add(controller);
            }
            else _backends.Add(new BridgeLifetime(new PanelNativeUiBridge(panel, package.Id, native)));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= Task.Run(DisposeCoreAsync));
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception>? failures = null;
        for (var i = _backends.Count - 1; i >= 0; i--)
        {
            try { await _backends[i].DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        _backends.Clear();
        if (failures is not null) throw new AggregateException("Overlay backends failed to stop cleanly.", failures);
    }

    private sealed class BridgeLifetime(PanelNativeUiBridge bridge) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { bridge.Dispose(); return ValueTask.CompletedTask; }
    }
}
