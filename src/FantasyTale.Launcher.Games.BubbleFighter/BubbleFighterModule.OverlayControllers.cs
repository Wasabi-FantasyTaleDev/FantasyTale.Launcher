using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule : IGameOverlayControllerProvider
{
    /// <summary>Starts the installed package's declared game controller independently of the Overlay browser's visibility and timer scheduler.</summary>
    public IAsyncDisposable StartController(GameOverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Plugin.Game != GameId || context.Plugin.Overlay?.Controller != "room-mode")
            throw new NotSupportedException("The game module does not provide this Overlay controller.");
        return new OverlayRoomModeController(context, ModeExtensionData.Load(context.Plugin, context.Build, context.Locale));
    }
}
