using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Definitions;

if ((args.Length == 2 || args.Length == 4 && args[2] == "--source-root") && args[0] == "--validate-catalog")
{
    try
    {
        var path = Path.GetFullPath(args[1]);
        var catalog = StoreCatalogFile.Parse(path, await File.ReadAllTextAsync(path));
        var root = args.Length == 4 ? Path.GetFullPath(args[3]) : Path.GetDirectoryName(path)!;
        var profiles = 0;
        foreach (var entry in catalog.Servers.Where(entry => entry.ProfilePath is not null))
        {
            var file = Path.GetFullPath(entry.ProfilePath!, root);
            var profile = ServerProfileFile.Parse(file, await ServerProfileFile.ReadAsync(file));
            _ = StoreServerDiscovery.ValidateProfile(profile, entry);
            profiles++;
        }
        Console.WriteLine($"Validated catalog: {catalog.Packages.Count} packages, {catalog.Profiles.Count} client profiles, {catalog.Servers.Count} servers/rooms, {profiles} server profile files.");
        return 0;
    }
    catch (Exception error) when (error is DefinitionException or IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Catalog validation failed: {error.Message}");
        return 1;
    }
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: pass a publication TOML containing the build input and public output directories.");
    return 2;
}
Console.WriteLine(await LauncherReleasePublisher.PublishAsync(args[0]));
return 0;
