using System.Globalization;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Sdk;

/// <summary>One layout rectangle read from a game UI resource.</summary>
public sealed record OverlayUiResourceNode(string Id, double X, double Y, double Width, double Height, bool Visible = true);

/// <summary>A bounded, parsed UI resource in its design-time coordinate space.</summary>
public sealed record OverlayUiResourceDocument(
    double ReferenceWidth,
    double ReferenceHeight,
    IReadOnlyDictionary<string, OverlayUiResourceNode> Nodes)
{
    /// <summary>Finds a named control, retaining its declared visibility for hidden anchors.</summary>
    public bool TryGetNode(string controlId, out OverlayUiResourceNode node) =>
        Nodes.TryGetValue(controlId, out node!);
}

/// <summary>Describes why a UI resource could not be used as an anchor.</summary>
public sealed class OverlayUiResourceFormatException(string message) : FormatException(message);

/// <summary>Optional build-aware decoder for a game's original UI resource format.</summary>
public interface IOverlayUiResourceDecoder
{
    /// <summary>Decodes approved resource bytes into design rectangles; unsupported formats fail explicitly.</summary>
    OverlayUiResourceDocument Decode(string resourceId, ReadOnlyMemory<byte> content, OverlayBuildContext context);
}

/// <summary>Parses normalized XML/JSON layouts supplied by optional UI-resource format adapters.</summary>
public static class OverlayUiResourceParser
{
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumNodes = 4096;
    private const int MaximumDepth = 64;

    /// <summary>Parses adapter-normalized layout text; proprietary game resource decoding is separate.</summary>
    public static OverlayUiResourceDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumBytes || Encoding.UTF8.GetByteCount(content) > MaximumBytes)
            throw new OverlayUiResourceFormatException("The UI resource exceeds 1 MiB.");
        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.IsEmpty) throw new OverlayUiResourceFormatException("The UI resource is empty.");
        return trimmed[0] is '<' ? ParseXml(content) : ParseJson(content);
    }

    private static OverlayUiResourceDocument ParseXml(string content)
    {
        XDocument document;
        try
        {
            using var text = new StringReader(content);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes,
            });
            document = XDocument.Load(reader);
        }
        catch (Exception error) when (error is System.Xml.XmlException or InvalidOperationException)
        { throw new OverlayUiResourceFormatException("The UI resource is not valid XML."); }
        var root = document.Root ?? throw new OverlayUiResourceFormatException("The UI resource has no root element.");
        var referenceWidth = Number(root, "referenceWidth", "designWidth", "viewportWidth", "width") ?? 0;
        var referenceHeight = Number(root, "referenceHeight", "designHeight", "viewportHeight", "height") ?? 0;
        var nodes = new Dictionary<string, OverlayUiResourceNode>(StringComparer.Ordinal);
        var budget = MaximumNodes * 2;
        WalkXml(root, 0, 0, true, 0, nodes, ref budget);
        return CreateDocument(referenceWidth, referenceHeight, nodes);
    }

    private static void WalkXml(XElement element, double parentX, double parentY, bool parentVisible, int depth,
        Dictionary<string, OverlayUiResourceNode> nodes, ref int budget)
    {
        if (--budget < 0) throw new OverlayUiResourceFormatException("The UI resource contains too many elements.");
        if (depth > MaximumDepth) throw new OverlayUiResourceFormatException("The UI resource nesting is too deep.");
        var x = Number(element, "x", "left") ?? 0;
        var y = Number(element, "y", "top") ?? 0;
        var width = Number(element, "width", "w");
        var height = Number(element, "height", "h");
        ValidateRectangle(x, y, width, height);
        var visible = parentVisible && Boolean(element, "visible", "isVisible", fallback: true);
        var id = Attribute(element, "id", "name", "control", "key");
        ValidateId(id);
        ValidateRectangle(parentX + x, parentY + y, width, height);
        if (id is not null && width is > 0 && height is > 0)
        {
            if (nodes.Count >= MaximumNodes) throw new OverlayUiResourceFormatException("The UI resource contains too many controls.");
            var absolute = new OverlayUiResourceNode(id, parentX + x, parentY + y, width.Value, height.Value, visible);
            if (!nodes.TryAdd(id, absolute)) throw new OverlayUiResourceFormatException($"The UI resource repeats control '{id}'.");
        }
        foreach (var child in element.Elements()) WalkXml(child, parentX + x, parentY + y, visible, depth + 1, nodes, ref budget);
    }

    private static OverlayUiResourceDocument ParseJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new OverlayUiResourceFormatException("The UI resource root must be an object.");
            var referenceWidth = JsonNumber(root, "referenceWidth", "designWidth", "viewportWidth", "width") ?? 0;
            var referenceHeight = JsonNumber(root, "referenceHeight", "designHeight", "viewportHeight", "height") ?? 0;
            var nodes = new Dictionary<string, OverlayUiResourceNode>(StringComparer.Ordinal);
            var budget = MaximumNodes * 2;
            WalkJson(root, 0, 0, true, 0, nodes, ref budget);
            return CreateDocument(referenceWidth, referenceHeight, nodes);
        }
        catch (OverlayUiResourceFormatException) { throw; }
        catch (JsonException) { throw new OverlayUiResourceFormatException("The UI resource is not valid JSON."); }
    }

    private static void WalkJson(JsonElement element, double parentX, double parentY, bool parentVisible, int depth,
        Dictionary<string, OverlayUiResourceNode> nodes, ref int budget)
    {
        if (--budget < 0) throw new OverlayUiResourceFormatException("The UI resource contains too many elements.");
        if (depth > MaximumDepth) throw new OverlayUiResourceFormatException("The UI resource nesting is too deep.");
        if (element.ValueKind != JsonValueKind.Object) throw new OverlayUiResourceFormatException("UI controls must be objects.");
        var x = JsonNumber(element, "x", "left") ?? 0;
        var y = JsonNumber(element, "y", "top") ?? 0;
        var width = JsonNumber(element, "width", "w");
        var height = JsonNumber(element, "height", "h");
        ValidateRectangle(x, y, width, height);
        var visible = parentVisible && JsonBoolean(element, "visible", "isVisible", true);
        var id = JsonString(element, "id", "name", "control", "key");
        ValidateId(id);
        ValidateRectangle(parentX + x, parentY + y, width, height);
        if (id is not null && width is > 0 && height is > 0)
        {
            if (nodes.Count >= MaximumNodes) throw new OverlayUiResourceFormatException("The UI resource contains too many controls.");
            if (!nodes.TryAdd(id, new OverlayUiResourceNode(id, parentX + x, parentY + y, width.Value, height.Value, visible)))
                throw new OverlayUiResourceFormatException($"The UI resource repeats control '{id}'.");
        }
        if (element.TryGetProperty("children", out var children))
        {
            if (children.ValueKind != JsonValueKind.Array) throw new OverlayUiResourceFormatException("UI children must be an array.");
            foreach (var child in children.EnumerateArray()) WalkJson(child, parentX + x, parentY + y, visible, depth + 1, nodes, ref budget);
        }
    }

    private static OverlayUiResourceDocument CreateDocument(double width, double height, Dictionary<string, OverlayUiResourceNode> nodes)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0 || (width == 0) != (height == 0))
            throw new OverlayUiResourceFormatException("The UI resource reference viewport is invalid.");
        if (nodes.Count == 0) throw new OverlayUiResourceFormatException("The UI resource contains no rectangular controls.");
        return new OverlayUiResourceDocument(width, height, new ReadOnlyDictionary<string, OverlayUiResourceNode>(nodes));
    }

    private static void ValidateRectangle(double x, double y, double? width, double? height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || width.HasValue != height.HasValue
            || width is <= 0 || height is <= 0 || width.HasValue && (!double.IsFinite(width.Value) || !double.IsFinite(height!.Value)))
            throw new OverlayUiResourceFormatException("UI controls require finite coordinates and a complete positive rectangle.");
    }

    private static void ValidateId(string? id)
    {
        if (id is { Length: > 128 }) throw new OverlayUiResourceFormatException("UI control names cannot exceed 128 characters.");
    }

    private static string? Attribute(XElement e, params string[] names) => names.Select(n => e.Attribute(n)).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a?.Value))?.Value;
    private static double? Number(XElement e, params string[] names) => ParseNumber(Attribute(e, names));
    private static bool Boolean(XElement e, string first, string second, bool fallback)
    {
        var text = Attribute(e, first, second);
        if (text is null) return fallback;
        return bool.TryParse(text, out var value) ? value : throw new OverlayUiResourceFormatException("UI visibility must be a boolean.");
    }
    private static double? JsonNumber(JsonElement e, params string[] names)
    {
        foreach (var name in names)
            if (e.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
                    ? number : throw new OverlayUiResourceFormatException($"UI field '{name}' must be a finite number.");
        return null;
    }
    private static string? JsonString(JsonElement e, params string[] names) => names.Select(n => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static bool JsonBoolean(JsonElement e, string first, string second, bool fallback)
    {
        if (!e.TryGetProperty(first, out var value) && !e.TryGetProperty(second, out value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new OverlayUiResourceFormatException("UI visibility must be a boolean."),
        };
    }
    private static double? ParseNumber(string? value) => value is null ? null
        : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? result : throw new OverlayUiResourceFormatException("UI coordinates must be finite numbers.");
}

/// <summary>Converts a parsed UI-resource control into the shared overlay anchor contract.</summary>
public static class OverlayUiResourceAnchors
{
    /// <summary>Resolves a control using the resource's design viewport and optional placement.</summary>
    public static OverlayAnchor Resolve(OverlayUiResourceDocument document, string controlId,
        OverlayAnchorPlacement placement = OverlayAnchorPlacement.Cover, double gap = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        if (!document.TryGetNode(controlId, out var node)) throw new KeyNotFoundException($"UI control '{controlId}' was not found.");
        if (!double.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        return new OverlayAnchor(node.X, node.Y, node.Width, node.Height)
        {
            ReferenceWidth = document.ReferenceWidth,
            ReferenceHeight = document.ReferenceHeight,
            Placement = placement,
            Gap = gap,
            Visible = node.Visible,
        };
    }
}

/// <summary>Publishes named rectangles from a parsed UI resource through the common anchor provider.</summary>
public sealed class OverlayUiResourceAnchorProvider : IOverlayAnchorProvider, IDisposable
{
    private readonly object _gate = new();
    private OverlayUiResourceDocument _document;
    private readonly Dictionary<string, List<Action<OverlayAnchor>>> _listeners = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>Creates a provider for one design-time UI resource document.</summary>
    public OverlayUiResourceAnchorProvider(OverlayUiResourceDocument document) => _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Replaces the resource after a game layout reload and publishes changed rectangles.</summary>
    public void Update(OverlayUiResourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<(Action<OverlayAnchor> Callback, OverlayAnchor Anchor)> callbacks = [];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _document = document;
            foreach (var (id, listeners) in _listeners)
            {
                var anchor = document.TryGetNode(id, out _)
                    ? OverlayUiResourceAnchors.Resolve(document, id)
                    : new OverlayAnchor(0, 0, 1, 1) { Visible = false };
                callbacks.AddRange(listeners.Select(listener => (listener, anchor)));
            }
        }
        foreach (var (callback, anchor) in callbacks) callback(anchor);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string targetId, Action<OverlayAnchor> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(changed);
        OverlayAnchor? current = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_document.TryGetNode(targetId, out _)) throw new KeyNotFoundException($"UI control '{targetId}' was not found.");
            if (_listeners.Sum(row => row.Value.Count) >= 128) throw new InvalidOperationException("The UI resource subscription limit has been reached.");
            if (!_listeners.TryGetValue(targetId, out var listeners)) _listeners[targetId] = listeners = [];
            listeners.Add(changed);
            current = OverlayUiResourceAnchors.Resolve(_document, targetId);
        }
        try { changed(current); }
        catch { Remove(targetId, changed); throw; }
        return new Subscription(this, targetId, changed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            lock (_gate) _listeners.Clear();
    }

    private void Remove(string targetId, Action<OverlayAnchor> callback)
    {
        lock (_gate)
            if (_listeners.TryGetValue(targetId, out var listeners))
            {
                listeners.Remove(callback);
                if (listeners.Count == 0) _listeners.Remove(targetId);
            }
    }

    private sealed class Subscription(OverlayUiResourceAnchorProvider owner, string targetId, Action<OverlayAnchor> callback) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(targetId, callback);
        }
    }
}
