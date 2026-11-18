namespace FantasyTale.Launcher.Sdk;

/// <summary>Optional game capability naming the <c>.ui</c> resources a client build shows its own message boxes from.</summary>
public interface IGameNativeMessageBoxes
{
    /// <summary>
    /// The resources, as the build's native form tracking names them, whose forms are message boxes the overlay must not
    /// cover. A build the game has no confirmed list for returns an empty list.
    /// </summary>
    /// <param name="buildId">The client build's identity.</param>
    IReadOnlyList<string> MessageBoxResources(string buildId);
}
