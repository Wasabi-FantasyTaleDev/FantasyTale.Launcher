using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalComponentZipRetainsItsDeclaredChannelAndSurvivesRestart(bool newer)
    {
        var game = Game() with { StoreManagedPlugins = ["fixture.optional"] };
        var manifest = Manifest.Replace("[plugin]", "[plugin]\nupdate_source = \"fixture\"", StringComparison.Ordinal);
        var bundled = Path.Combine(game.Folder, "plugins", "optional");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), manifest);
        var candidate = newer ? manifest.Replace("1.0.0", "1.1.0", StringComparison.Ordinal) : manifest;
        var archive = Zip(("plugin.toml", candidate, 0));
        var root = Path.Combine(_root, "component-import-library");
        using (var library = new PluginLibrary(root))
        {
            var preview = library.Prepare(archive, game, game.Builds[0]);
            var installed = library.Install(preview.Token, game, game.Builds[0]);
            Assert.Equal(newer ? "1.1.0" : "1.0.0", installed.Version);
            Assert.Equal("fixture", installed.UpdateSource);
            Assert.Null(library.Origin(installed));
        }
        using var reopened = new PluginLibrary(root);
        Assert.Equal("fixture", reopened.Packages(game)[0].UpdateSource);
        Assert.Contains(reopened.Packages(game), plugin => plugin.Version == (newer ? "1.1.0" : "1.0.0"));
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("same-version-bytes")]
    [InlineData("restore-policy")]
    public void LocalComponentImportCannotChangeItsChannelSnapshotOrRestorationPolicy(string rejection)
    {
        var game = Game() with { StoreManagedPlugins = ["fixture.optional"] };
        var manifest = Manifest.Replace("[plugin]", "[plugin]\nupdate_source = \"fixture\"", StringComparison.Ordinal);
        var bundled = Path.Combine(game.Folder, "plugins", "optional");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), manifest);
        var candidate = rejection switch
        {
            "channel" => manifest.Replace("update_source = \"fixture\"", "update_source = \"other\"", StringComparison.Ordinal),
            "same-version-bytes" => manifest + "\n# Changed snapshot.\n",
            "restore-policy" => manifest.Replace("1.0.0", "1.1.0", StringComparison.Ordinal)
                .Replace("[plugin]", "[plugin]\nrestore_files = false", StringComparison.Ordinal),
            _ => manifest,
        };
        using var library = new PluginLibrary(Path.Combine(_root, "component-import-library"));
        if (rejection == "restore-policy")
            Assert.Throws<DefinitionException>(() => library.Prepare(Zip(("plugin.toml", candidate, 0)), game, game.Builds[0]));
        else Assert.Throws<InvalidDataException>(() => library.Prepare(Zip(("plugin.toml", candidate, 0)), game, game.Builds[0]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SourceDeclaredComponentsAcceptLocalZipOnlyWithMatchingUpdateChannel(bool matching)
    {
        var game = Game() with
        {
            StoreManagedPlugins = ["fixture.optional"],
            PluginSources = new Dictionary<string, string> { ["fixture.optional"] = "fixture" },
        };
        var manifest = Manifest.Replace("[plugin]", "[plugin]\nupdate_source = \"" + (matching ? "fixture" : "other") + "\"", StringComparison.Ordinal);
        using var library = new PluginLibrary(Path.Combine(_root, "declared-component-library"));
        var archive = Zip(("plugin.toml", manifest, 0));
        if (!matching)
        {
            Assert.Throws<InvalidDataException>(() => library.Prepare(archive, game, game.Builds[0]));
            Assert.Empty(library.Packages(game));
            return;
        }
        var preview = library.Prepare(archive, game, game.Builds[0]);
        var installed = library.Install(preview.Token, game, game.Builds[0]);
        Assert.Equal("fixture", installed.UpdateSource);
        Assert.Null(library.Origin(installed));
    }

    [Fact]
    public void LocalFileComponentUpgradeCannotDisableRestoration()
    {
        var game = Game() with { StoreManagedPlugins = ["fixture.optional"] };
        var manifest = Manifest[..Manifest.IndexOf("[[patches]]", StringComparison.Ordinal)]
            .Replace("kind = \"patch\"", "kind = \"files\"\nupdate_source = \"fixture\"", StringComparison.Ordinal);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("payload"u8)).ToLowerInvariant();
        manifest += $"\n[[files]]\nsource = \"payload.bin\"\ntarget = \"component.bin\"\nsize = 7\nsha256 = \"{hash}\"\n";
        var bundled = Path.Combine(game.Folder, "plugins", "optional");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), manifest);
        File.WriteAllText(Path.Combine(bundled, "payload.bin"), "payload");
        var next = manifest.Replace("1.0.0", "1.1.0", StringComparison.Ordinal);
        using var library = new PluginLibrary(Path.Combine(_root, "file-component-library"));
        var changed = Zip(("plugin.toml", next.Replace("[plugin]", "[plugin]\nrestore_files = false", StringComparison.Ordinal), 0),
            ("payload.bin", "payload", 0));
        Assert.Throws<InvalidDataException>(() => library.Prepare(changed, game, game.Builds[0]));
        var preview = library.Prepare(Zip(("plugin.toml", next, 0), ("payload.bin", "payload", 0)), game, game.Builds[0]);
        Assert.True(library.Install(preview.Token, game, game.Builds[0]).RestoreFiles);
    }
}
