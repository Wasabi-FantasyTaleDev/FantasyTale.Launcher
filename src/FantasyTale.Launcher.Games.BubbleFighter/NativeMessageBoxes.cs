using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

/// <summary>Per-build data naming the <c>.ui</c> resources the client shows its own message boxes from.</summary>
internal static class NativeMessageBoxes
{
    private static readonly Dictionary<string, string[]> Resources;

    static NativeMessageBoxes()
    {
        using var stream = typeof(NativeMessageBoxes).Assembly.GetManifestResourceStream(
            "FantasyTale.Launcher.Games.BubbleFighter.native-message-boxes.json")
            ?? throw new InvalidDataException("The native message box descriptor is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException("The native message box descriptor has an unsupported schema.");
        Resources = new(StringComparer.Ordinal);
        foreach (var build in root.GetProperty("builds").EnumerateObject())
        {
            var list = build.Value.EnumerateArray().Select(item => item.GetString()).ToArray();
            if (list.Length == 0 || list.Any(item => string.IsNullOrWhiteSpace(item) || !item.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
                || list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
                throw new InvalidDataException("Native message box resources must be distinct nonempty .ui paths.");
            Resources.Add(build.Name, list!);
        }
    }

    internal static IReadOnlyList<string> For(string buildId) => Resources.TryGetValue(buildId, out var list) ? list : [];
}
