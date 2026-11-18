using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.BanPick;

/// <summary>One room form of a build and where the Ban Rules button sits in it, in the form's own pixels.</summary>
/// <param name="Form">The <c>.ui</c> resource the client loads the room from.</param>
/// <param name="Style">The look of the native select-map button: <c>sheet</c> (a four-state yellow sprite) or <c>v3</c> (a plain text button).</param>
/// <param name="Host">The button's rectangle for the room host: beside the select-map button.</param>
/// <param name="Member">The button's rectangle for a member, who has no select-map button.</param>
internal sealed record BanPickLayout(string Form, string Style, RoomEntry Host, RoomEntry Member);

/// <summary>The room layouts of one build, read from the package's <c>room-layouts.json</c>.</summary>
internal sealed class BanPickRoomData
{
    internal IReadOnlyList<BanPickLayout> Layouts { get; }
    internal string Sprite { get; }
    internal string BuildId { get; }

    private BanPickRoomData(IReadOnlyList<BanPickLayout> layouts, string sprite, string buildId)
    { Layouts = layouts; Sprite = sprite; BuildId = buildId; }

    internal static BanPickRoomData Load(PluginDefinition plugin, BuildDefinition build)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(build);
        var path = Path.Combine(plugin.Folder, "room-layouts.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return Parse(document.RootElement, build.Id);
    }

    internal static BanPickRoomData Parse(JsonElement root, string build)
    {
        if (root.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("room-layouts.json: unsupported format.");
        var sprite = root.GetProperty("sprite").GetString() ?? throw new InvalidDataException("room-layouts.json: sprite is required.");
        var layouts = new List<BanPickLayout>();
        if (root.GetProperty("builds").TryGetProperty(build, out var rows))
            foreach (var row in rows.EnumerateArray())
            {
                var form = row.GetProperty("form").GetString();
                var style = row.GetProperty("style").GetString();
                if (string.IsNullOrWhiteSpace(form) || style is not ("sheet" or "v3"))
                    throw new InvalidDataException("room-layouts.json: each layout needs a form and a sheet or v3 style.");
                layouts.Add(new(form, style, Rect(row.GetProperty("host")), Rect(row.GetProperty("member"))));
            }
        if (layouts.DistinctBy(layout => layout.Form, StringComparer.OrdinalIgnoreCase).Count() != layouts.Count)
            throw new InvalidDataException("room-layouts.json: each form may appear once per build.");
        return new(layouts, sprite, build);
    }

    private static RoomEntry Rect(JsonElement value)
    {
        var entry = new RoomEntry(value.GetProperty("x").GetInt32(), value.GetProperty("y").GetInt32(),
            value.GetProperty("width").GetInt32(), value.GetProperty("height").GetInt32());
        if (entry.Width <= 0 || entry.Height <= 0) throw new InvalidDataException("room-layouts.json: a rectangle needs a positive size.");
        return entry;
    }
}
