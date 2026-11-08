using System.Text.Json;

namespace FantasyTale.Launcher.Games.BubbleFighter;

/// <summary>Reads reviewed adapter support without granting server deployment or automatic-selection authority.</summary>
internal static class GatewayBuildCapabilities
{
    private static readonly HashSet<string> Bootstrap;
    private static readonly HashSet<string> Automatic;

    static GatewayBuildCapabilities()
    {
        using var stream = typeof(GatewayBuildCapabilities).Assembly.GetManifestResourceStream(
            "FantasyTale.Launcher.Games.BubbleFighter.gateway-capabilities.json")
            ?? throw new InvalidDataException("The Gateway adapter capability descriptor is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1)
            throw new InvalidDataException("The Gateway adapter capability descriptor has an unsupported schema.");
        Bootstrap = Read(root, "bootstrap");
        Automatic = Read(root, "automatic");
        if (!Automatic.IsSubsetOf(Bootstrap))
            throw new InvalidDataException("An automatic Gateway adapter requires a native bootstrap adapter.");
    }

    internal static bool SupportsBootstrap(string build) => Bootstrap.Contains(build);

    internal static bool SupportsAutomaticSelection(string build) => Automatic.Contains(build);

    private static HashSet<string> Read(JsonElement root, string name)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty(name).EnumerateArray())
        {
            if (item.GetString() is not { Length: > 0 } value || !result.Add(value))
                throw new InvalidDataException("Gateway adapter capabilities require distinct nonempty build identities.");
        }
        return result;
    }
}
