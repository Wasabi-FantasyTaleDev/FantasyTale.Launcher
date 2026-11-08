using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

internal static class TestRepository
{
    internal static PluginDefinition InstallStorePlugin(string libraryRoot, string folder, GameDefinition game, BuildDefinition build, string source)
    {
        var plugin = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        var text = new StringBuilder($"schema = 1\n[release]\nid = {JsonSerializer.Serialize(plugin.Id)}\ngame = {JsonSerializer.Serialize(game.Id)}\nversion = {JsonSerializer.Serialize(plugin.Version)}\nbuilds = [{JsonSerializer.Serialize(build.Id)}]\n");
        long size = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(file);
            var path = JsonSerializer.Serialize(Path.GetRelativePath(folder, file).Replace('\\', '/'));
            size += bytes.Length;
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[[files]]\npath = {path}\nsource_path = {path}\nsize = {bytes.Length}\nsha256 = \"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}\"\n");
        }
        var manifest = Encoding.UTF8.GetBytes(text.ToString());
        var package = new StorePackage(plugin.Id, "plugin", game.Id, [build.Id], plugin.Version, plugin.Name.Values,
            plugin.Description?.Values ?? new Dictionary<string, string>(), "Fixture", size, "manifest.toml", Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant())
            { MinimumLauncherVersion = plugin.MinimumLauncherVersion ?? "" };
        using var library = new PluginLibrary(libraryRoot);
        return library.InstallFromStore(folder, game, build, package, manifest, source, new string('a', 64));
    }

    // Isolated build outputs can live on a different drive from the checked-out fixtures.
    internal static DirectoryInfo StartDirectory([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile) })
        {
            for (var directory = start is null ? null : new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "FantasyTale.Launcher.slnx")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "games"))) return directory;
        }
        throw new DirectoryNotFoundException("Repository fixtures require the Launcher source checkout beside the test sources or output.");
    }
}
