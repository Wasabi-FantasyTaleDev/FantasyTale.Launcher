using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule : IGameResourceModule
{
    private static readonly CharacterModelCapabilities Models = new();

    /// <summary>Shared game-owned model capability for optional character and equipment providers.</summary>
    public static ICharacterModelCapabilities CharacterModels => Models;

    /// <summary>Reusable archive composer for game resource providers; callers stage outputs before shared deployment.</summary>
    public IArchiveResourceComposer ArchiveResources { get; } = new ArchiveResourceComposer();

    /// <summary>Read-only parser of native .ui forms for client plugins that need control layout parameters.</summary>
    public IGameUiForms UiForms { get; } = new UiForms();

    /// <summary>Build-aware targets, resource manifest builders and native control inspection for resource plugins.</summary>
    public IBubbleFighterResourceEdits ResourceEdits { get; } = new BubbleFighterResourceEdits();

    /// <inheritdoc />
    public T? GetCapability<T>() where T : class => Models as T ?? ArchiveResources as T ?? UiForms as T ?? ResourceEdits as T ?? this as T;

    /// <inheritdoc />
    public IPreparedGameResources PrepareResources(LaunchContext context, IReadOnlyList<GameResourceSelection> selections) =>
        new CharacterModelResources(Models).Prepare(context, selections);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> ResourceSettings(LaunchContext context, PluginDefinition package) =>
        new CharacterModelResources(Models).Settings(context, package);

    /// <inheritdoc />
    public void ValidateResourceSettings(LaunchContext context, GameResourceSelection selection) =>
        new CharacterModelResources(Models).Validate(context, selection);
}
