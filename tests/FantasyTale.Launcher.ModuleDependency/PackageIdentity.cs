namespace FantasyTale.Launcher.ModuleDependency;

/// <summary>Reports the resolved dependency location so module acceptance can distinguish otherwise identical packages.</summary>
public static class PackageIdentity
{
    /// <summary>The installed directory of this dependency, independent of the caller's module location.</summary>
    public static string Folder => Path.GetDirectoryName(typeof(PackageIdentity).Assembly.Location)!;
}
