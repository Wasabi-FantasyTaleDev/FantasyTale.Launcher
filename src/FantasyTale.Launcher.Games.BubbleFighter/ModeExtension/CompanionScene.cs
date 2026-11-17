using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

/// <summary>
/// One <c>game.session.state</c> snapshot or <c>game.session.changed</c> event from the server, as the room mode controller
/// reads it; the strict reading itself is the SDK's <see cref="GameCompanionScene"/>.
/// </summary>
internal sealed record CompanionScene(string State, bool InMatch, bool CanLeaveMatch, bool Tutorial, int? Mode)
{
    /// <summary>The scene event the server pushes on every scene change.</summary>
    internal const string Event = GameCompanionScene.Event;

    /// <summary>The request that returns the current scene.</summary>
    internal const string Request = GameCompanionScene.Request;

    /// <summary>Reads a scene payload with the SDK's strict rules.</summary>
    internal static bool TryParse(object? payload, out CompanionScene scene)
    {
        scene = null!;
        if (!GameCompanionScene.TryParse(payload, out var read)) return false;
        scene = new(read.State, read.InMatch, read.CanLeaveMatch, read.Tutorial, read.Mode);
        return true;
    }

    internal static bool TryObject(object? payload, out JsonElement value) => GameCompanionScene.TryObject(payload, out value);

    internal static bool TryBoolean(JsonElement value, string name, out bool result) => GameCompanionScene.TryBoolean(value, name, out result);
}
