using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Projects validated live catalog data onto a saved server profile without changing its identity.</summary>
public static class ServerProfileRefresh
{
    /// <summary>Applies the server's current display, build and plugin declarations while retaining local connection identity.</summary>
    public static ServerDefinition Apply(ServerDefinition current, LauncherCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(catalog);
        return current with
        {
            Name = catalog.Name,
            Presentation = catalog.Presentation,
            Builds = catalog.Instances.Select(instance => instance.Build).Distinct(StringComparer.Ordinal).ToArray(),
            ClientPolicy = catalog.ClientPolicy,
        };
    }
}
