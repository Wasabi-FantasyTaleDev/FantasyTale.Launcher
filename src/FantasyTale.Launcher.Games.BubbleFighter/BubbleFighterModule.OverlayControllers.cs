using FantasyTale.Launcher.Games.BubbleFighter.BanPick;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule : IGameOverlayControllerProvider, IGameNativeMessageBoxes
{
    /// <inheritdoc />
    public IReadOnlyList<string> MessageBoxResources(string buildId) => NativeMessageBoxes.For(buildId);

    /// <summary>Starts the installed package's declared game controller independently of the Overlay browser's visibility and timer scheduler.</summary>
    public IAsyncDisposable StartController(GameOverlayControllerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Plugin.Game != GameId || context.Plugin.Overlay?.Controller is not ("room-mode" or "ban-pick-room"))
            throw new NotSupportedException("The game module does not provide this Overlay controller.");
        return context.Plugin.Overlay.Controller == "ban-pick-room"
            ? new BanPickRoomController(context, BanPickRoomData.Load(context.Plugin, context.Build))
            : new OverlayRoomModeController(context, ModeExtensionData.Load(context.Plugin, context.Build, context.Locale));
    }
}
