using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    private const string AssetsManifest = """
        schema = 1
        [plugin]
        id = "fixture.previews"
        game = "fixture"
        version = "1.0.0"
        kind = "assets"
        name = { en = "Previews" }
        [compatibility]
        builds = ["test-1"]
        [assets]
        root = "data"
        """;

    private string AssetsFolder(bool data)
    {
        var folder = Folder(AssetsManifest);
        if (!data) return folder;
        Directory.CreateDirectory(Path.Combine(folder, "data", "atlas"));
        File.WriteAllText(Path.Combine(folder, "data", "pack.json"), "{}");
        File.WriteAllBytes(Path.Combine(folder, "data", "atlas", "0123456789abcdef.webp"), new byte[300_000]);
        return folder;
    }

    [Fact]
    public void AnAssetsPackageInstallsWithItsDataAndIsRefusedWithout()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.Throws<InvalidDataException>(() => library.Prepare(AssetsFolder(data: false), game, build));
        var preview = library.Prepare(AssetsFolder(data: true), game, build);
        Assert.Equal(3, preview.FileCount);
        var installed = library.Install(preview.Token, game, build);
        Assert.Equal("assets", installed.Kind);
        Assert.True(File.Exists(Path.Combine(PluginRequirements.AssetsFolder(installed), "atlas", "0123456789abcdef.webp")));
        Assert.Contains(library.Packages(game), package => package.Id == "fixture.previews");
    }

    [Fact]
    public void SharedDataStaysInstalledWhileAnInstalledPackageStillRequiresIt()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        _ = library.Install(library.Prepare(AssetsFolder(data: true), game, build).Token, game, build);
        var dependent = Manifest.Replace("fixture.optional", "fixture.dependent", StringComparison.Ordinal)
            .Replace("kind = \"patch\"", "kind = \"patch\"\nrequires = [\"fixture.previews\"]", StringComparison.Ordinal);
        _ = library.Install(library.Prepare(Folder(dependent), game, build).Token, game, build);

        var error = Assert.Throws<InvalidOperationException>(() => library.Uninstall(game, "fixture.previews", store, _ => false));
        Assert.Contains("'fixture.dependent'", error.Message, StringComparison.Ordinal);
        Assert.Contains(library.Packages(game), package => package.Id == "fixture.previews");

        library.Uninstall(game, "fixture.dependent", store, _ => false);
        library.Uninstall(game, "fixture.previews", store, _ => false);
        Assert.DoesNotContain(library.Packages(game), package => package.Id == "fixture.previews");
    }
}
