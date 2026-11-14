namespace FantasyTale.Launcher.Sdk;

/// <summary>Starts game-owned background behavior selected by an enabled overlay package's controller declaration.</summary>
public interface IGameOverlayControllerProvider
{
    /// <summary>Subscribes and starts one controller before its entry HTML is dispatched; returns promptly with a lifetime that stops and awaits all background work.</summary>
    IAsyncDisposable StartController(GameOverlayControllerContext context);
}

/// <summary>Supplies an installed package and launch-scoped services to its game module's overlay controller.</summary>
/// <param name="Plugin">The enabled exact-version package whose overlay declaration selects the controller.</param>
/// <param name="Build">The client build running in this launch.</param>
/// <param name="Panel">The package's reserved presentation endpoint; subscribe immediately and send page messages after its readiness event.</param>
/// <param name="NativeUi">The package-scoped native UI service, or null when this launch has no native channel.</param>
/// <param name="Locale">The selected presentation language for this overlay.</param>
public sealed record GameOverlayControllerContext(PluginDefinition Plugin, BuildDefinition Build, IOverlayPanel Panel,
    IGameNativeUi? NativeUi, string? Locale)
{
    /// <summary>Gets the stable owner-scoped companion view for this package's declared grants, or null when no service is available.</summary>
    public ICompanionChannel? Companion { get; init; }

    /// <summary>Gets an optional sink for fixed diagnostic categories and status identities, excluding game text and credentials.</summary>
    public Action<string, string>? Record { get; init; }
}
