using System.Text;
using System.Text.Json;

namespace FantasyTale.Launcher.Host.Window;

// Events are retained only until consumed by the owning native plugin.
internal sealed class OverlayEventQueue
{
    internal const int MaximumEventBytes = 4096;
    private const int MaximumEvents = 32;
    private readonly Queue<string> _events = new();
    private readonly object _gate = new();

    internal void Add(string name, JsonElement data)
    {
        var value = JsonSerializer.Serialize(new { name, data = data.ValueKind == JsonValueKind.Undefined ? (object?)null : data });
        if (Encoding.UTF8.GetByteCount(value) > MaximumEventBytes)
            value = "{\"name\":\"overlay.event-too-large\",\"data\":null}";
        lock (_gate)
        {
            if (_events.Count == MaximumEvents)
            {
                _events.Clear();
                _events.Enqueue("{\"name\":\"overlay.events-overflow\",\"data\":null}");
            }
            _events.Enqueue(value);
        }
    }

    internal string? Take()
    {
        lock (_gate) return _events.TryDequeue(out var value) ? value : null;
    }
}
