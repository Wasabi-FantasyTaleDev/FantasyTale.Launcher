using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

/// <summary>Projects optional directory labels for clients whose original chooser uses fixed local titles.</summary>
internal static class NativeCenterNames
{
    private static readonly HashSet<string> Builds;
    private static readonly string Capability;

    static NativeCenterNames()
    {
        using var stream = typeof(NativeCenterNames).Assembly.GetManifestResourceStream(
            "FantasyTale.Launcher.Games.BubbleFighter.native-center-names.json")
            ?? throw new InvalidDataException("The native Center name descriptor is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException("The native Center name descriptor has an unsupported schema.");
        Capability = root.GetProperty("capability").GetString()
            ?? throw new InvalidDataException("The native Center name capability is missing.");
        Builds = new(StringComparer.Ordinal);
        foreach (var build in root.GetProperty("builds").EnumerateArray())
            if (build.GetString() is not { Length: > 0 } id || !Builds.Add(id))
                throw new InvalidDataException("Native Center name builds must be distinct and nonempty.");
    }

    internal static LaunchBootstrapMetadata? Create(string build, IReadOnlyList<GameServerTarget> targets,
        IAutomaticServerSelection? automatic = null)
    {
        if (!Builds.Contains(build)) return null;
        return Project(targets, automatic);
    }

    internal static LaunchBootstrapMetadata? Project(IReadOnlyList<GameServerTarget> targets,
        IAutomaticServerSelection? automatic = null)
    {
        var names = new Dictionary<int, string>();
        if (automatic is not null) Add(automatic.Automatic.Id, automatic.Automatic.Name);
        foreach (var target in targets)
            if (target.NativeCenterId is { } id) Add(id, target.Name);
        if (names.Count == 0) return null;
        var text = new StringBuilder("schema = 1\n");
        foreach (var (id, name) in names.OrderBy(row => row.Key))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n[[centers]]\nid = {id}\nname = \"");
            text.Append(name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal));
            text.Append("\"\n");
        }
        if (Encoding.UTF8.GetByteCount(text.ToString()) > 32768)
            throw new InvalidDataException("Native Center names exceed the module metadata transport budget.");
        return new(Capability, text.ToString());

        void Add(int id, string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (id < 1 || name.Length > 128 || name.Any(char.IsControl) || !names.TryAdd(id, name))
                throw new InvalidDataException("Native Center names require distinct positive identities and valid directory labels.");
        }
    }
}
