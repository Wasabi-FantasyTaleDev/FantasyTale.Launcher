namespace FantasyTale.Launcher.Sdk;

/// <summary>A persisted, version-pinned plugin selection in instance load order.</summary>
/// <param name="Id">Plugin identifier resolved from the package catalog.</param>
/// <param name="Version">Exact installed package version.</param>
/// <param name="Enabled">Whether the plugin is selected for launch.</param>
/// <param name="Required">Whether this instance's distribution requires the plugin.</param>
/// <param name="Options">Typed overrides; unspecified options use manifest defaults.</param>
public sealed record InstancePlugin(string Id, string Version, bool Enabled, bool Required, IReadOnlyDictionary<string, object> Options);

/// <summary>One independent client copy. Credentials are never part of this persisted definition.</summary>
/// <param name="Id">Stable identifier and directory name below the instance root.</param>
/// <param name="Name">User-selected display name.</param>
/// <param name="Game">Game identifier.</param>
/// <param name="Build">Client build identifier.</param>
/// <param name="Endpoint">Manual game endpoint, or null for the client's default.</param>
/// <param name="Plugins">Plugin selections in activation order.</param>
public sealed record InstanceDefinition(string Id, string Name, string Game, string Build, ServerEndpoint? Endpoint,
    IReadOnlyList<InstancePlugin> Plugins)
{
    /// <summary>Absolute client installation directory, or null for the managed client subdirectory.</summary>
    public string? InstallationPath { get; init; }

    /// <summary>Client language preference consumed by the game module, independent of launcher UI language.</summary>
    public string? Language { get; init; }

    /// <summary>Whether automatic update discovery may recommend replacing this client installation.</summary>
    public bool CheckIntegrity { get; init; } = true;
}
