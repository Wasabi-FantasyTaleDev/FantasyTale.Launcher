using System.Text.Json;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

/// <summary>
/// One <c>game.session.state</c> snapshot or <c>game.session.changed</c> event from the server: the player's scene
/// (<c>main_menu</c>, <c>plaza</c>, <c>room_*</c>, <c>match</c>, ...) and the flags the server sends with it.
/// </summary>
internal sealed record CompanionScene(string State, bool InMatch, bool CanLeaveMatch, bool Tutorial, int? Mode)
{
    /// <summary>The scene event the server pushes on every scene change.</summary>
    internal const string Event = "game.session.changed";

    /// <summary>The request that returns the current scene.</summary>
    internal const string Request = "game.session.state";

    /// <summary>
    /// Reads a scene payload strictly: a nonblank state, the three flags as booleans, an optional integer mode, no
    /// duplicate keys, and a plaza that claims no match, tutorial or mode. Anything else is rejected.
    /// </summary>
    internal static bool TryParse(object? payload, out CompanionScene scene)
    {
        scene = null!;
        if (!TryObject(payload, out var value) || !value.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(state.GetString()) || !TryBoolean(value, "in_match", out var inMatch)
            || !TryBoolean(value, "can_leave_match", out var canLeave) || !TryBoolean(value, "is_tutorial", out var tutorial)) return false;
        // Companion omits nullable mode identifiers outside rooms.
        int? modeId = null;
        if (value.TryGetProperty("mode_id", out var mode) && mode.ValueKind != JsonValueKind.Null)
        {
            if (mode.ValueKind != JsonValueKind.Number || !mode.TryGetInt32(out var number)) return false;
            modeId = number;
        }
        var name = state.GetString()!;
        if (name == "plaza" && (inMatch || canLeave || tutorial || modeId is not null)) return false;
        scene = new(name, inMatch, canLeave, tutorial, modeId);
        return true;
    }

    internal static bool TryObject(object? payload, out JsonElement value)
    {
        try
        {
            value = payload is JsonElement json ? json : JsonSerializer.SerializeToElement(payload);
            return value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException)
        { value = default; return false; }
    }

    internal static bool TryBoolean(JsonElement value, string name, out bool result)
    {
        result = false;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        result = property.GetBoolean();
        return true;
    }
}
