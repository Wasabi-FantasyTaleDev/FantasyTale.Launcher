namespace FantasyTale.Launcher.Sdk;

/// <summary>Data-selected presentation and capabilities for one build; behavior remains shared by plugins.</summary>
public sealed record OverlayBuildProfile(string Theme, IReadOnlyList<string> Capabilities, IReadOnlyList<OverlayTargetDefinition> Targets);

/// <summary>A named UI target resolved by a hook, image locator or optional UI-resource layout provider.</summary>
public sealed record OverlayTargetDefinition(string Id, string Locator, string? Resource, string? Control, string? ParentTarget);

/// <summary>Immutable session identity exposed to game-UI plugins and the shared page.</summary>
public sealed record OverlayBuildContext(string BuildId, string Branch, string? BuildDate, int ProtocolVersion, OverlayBuildProfile Profile);
