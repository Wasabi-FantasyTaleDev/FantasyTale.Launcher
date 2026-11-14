using System.Collections.Frozen;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

// Each subscriber retains only the latest pending revision and runs outside transport and observer locks.
internal sealed class CompanionStateObservers(CompanionConnectionSnapshot initial, int limit, Action? failed = null)
{
    private readonly object _gate = new();
    private readonly HashSet<Subscription> _subscriptions = [];
    private CompanionConnectionSnapshot _current = initial with { Capabilities = initial.Capabilities.ToFrozenSet(StringComparer.Ordinal) };
    private long _sourceRevision = -1;

    internal CompanionConnectionSnapshot Current { get { lock (_gate) return _current; } }

    internal void Update(string? epoch, CompanionChannelState state, IReadOnlySet<string> capabilities, long sourceRevision)
    {
        CompanionConnectionSnapshot snapshot;
        Subscription[] subscriptions;
        lock (_gate)
        {
            if (sourceRevision <= _sourceRevision) return;
            _sourceRevision = sourceRevision;
            if (_current.Epoch == epoch && _current.State == state && _current.Capabilities.SetEquals(capabilities)) return;
            snapshot = new(epoch, state, capabilities.ToFrozenSet(StringComparer.Ordinal)) { Revision = checked(_current.Revision + 1) };
            _current = snapshot;
            subscriptions = _subscriptions.ToArray();
        }
        foreach (var subscription in subscriptions) subscription.Deliver(snapshot);
    }

    internal IDisposable Subscribe(Action<CompanionConnectionSnapshot> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        CompanionConnectionSnapshot snapshot;
        lock (_gate)
        {
            if (_subscriptions.Count >= limit) throw new InvalidOperationException("Companion lifecycle subscription limit reached.");
            _subscriptions.Add(subscription);
            snapshot = _current;
        }
        subscription.Deliver(snapshot);
        return subscription;
    }

    private void Remove(Subscription subscription) { lock (_gate) _subscriptions.Remove(subscription); }

    private sealed class Subscription(CompanionStateObservers owner, Action<CompanionConnectionSnapshot> handler) : IDisposable
    {
        private readonly object _gate = new();
        private CompanionConnectionSnapshot? _pending;
        private long _revision = -1;
        private bool _running, _disposed;

        internal void Deliver(CompanionConnectionSnapshot snapshot)
        {
            lock (_gate)
            {
                if (_disposed || snapshot.Revision <= _revision) return;
                _revision = snapshot.Revision;
                _pending = snapshot;
                if (_running) return;
                _running = true;
            }
            while (true)
            {
                CompanionConnectionSnapshot current;
                lock (_gate)
                {
                    if (_disposed || _pending is null) { _running = false; return; }
                    current = _pending;
                    _pending = null;
                }
                try { handler(current); }
                catch (Exception) { owner.ReportFailure(); }
            }
        }

        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; _pending = null; }
            owner.Remove(this);
        }
    }

    private void ReportFailure() => failed?.Invoke();
}
