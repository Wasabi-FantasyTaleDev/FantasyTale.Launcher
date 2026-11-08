using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Downloads;
using System.Net;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-plugin-import-").FullName;
    private const string Manifest = """
        schema = 1
        [plugin]
        id = "fixture.optional"
        game = "fixture"
        version = "1.0.0"
        name = { en = "Optional patch" }
        kind = "patch"
        runtime = ">=3.0.0 <4.0.0"
        [compatibility]
        builds = ["test-1"]
        [[patches]]
        id = "fixture"
        locate = { pattern = "AA BB CC" }
        expect = "AA"
        write = "90"
        """;
    private GameDefinition Game()
    {
        var folder = Path.Combine(_root, "games", "Fixture"); Directory.CreateDirectory(Path.Combine(folder, "builds"));
        File.WriteAllText(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
        File.WriteAllText(Path.Combine(folder, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
        return Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
    }
    private string Folder(string text = Manifest)
    {
        var folder = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), text);
        return folder;
    }

    private static (StorePackage Package, byte[] Manifest) StoreRelease(string folder)
    {
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var release = Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"fixture.optional\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\n[[files]]\npath = \"plugin.toml\"\nsource_path = \"plugin.toml\"\nsize = {bytes.Length}\nsha256 = \"{hash}\"\n");
        return (new("fixture.optional", "plugin", "fixture", ["test-1"], "1.0.0", new Dictionary<string, string> { ["en"] = "Fixture" },
            new Dictionary<string, string>(), "Fixture", bytes.Length, "manifest.toml", Convert.ToHexString(SHA256.HashData(release)).ToLowerInvariant()), release);
    }

    [Fact]
    public void ImportReportsMeasuredCopyAndVerificationBeforeCommitting()
    {
        var game = Game(); var source = Folder();
        File.WriteAllBytes(Path.Combine(source, "payload.bin"), new byte[262144]);
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var updates = new List<PluginImportProgress>();
        var preview = library.Prepare(source, game, game.Builds[0], updates.Add);
        Assert.Contains(updates, update => update.Stage == "copying" && update.Percent is > 0 and < 100);
        Assert.Contains(updates, update => update.Stage == "verifying" && update.Percent is > 0 and < 100);
        Assert.Equal(new PluginImportProgress("completed", 100), updates[^1]);
        updates.Clear();
        var installed = library.Install(preview.Token, game, game.Builds[0], updates.Add);
        Assert.Contains(updates, update => update.Stage == "verifying" && update.Percent == 100);
        Assert.Contains(updates, update => update.Stage == "installing" && update.Percent is null);
        Assert.Equal(new PluginImportProgress("completed", 100), updates[^1]);
        Assert.True(File.Exists(Path.Combine(installed.Folder, "payload.bin")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoreInstallationIsIdempotentAndKeepsAnOpenLocalImportPreview(bool zipped)
    {
        var game = Game(); var source = Folder(); var release = StoreRelease(source);
        if (zipped) (source, release) = ArchiveRelease(Zip(("plugin.toml", Manifest, 0)));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var pending = library.Prepare(Folder(Manifest.Replace("fixture.optional", "fixture.other", StringComparison.Ordinal)), game, game.Builds[0]);
        var installed = library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64));
        Assert.Equal(installed.Folder, library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)).Folder);
        Assert.Equal("fixture.other", library.Install(pending.Token, game, game.Builds[0]).Id);
        Assert.Equal(2, library.Packages(game).Count);
        Assert.Equal(installed.Folder, library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "other", new string('b', 64)).Folder);
        File.AppendAllText(Path.Combine(installed.Folder, "plugin.toml"), "\n# external edit\n");
        Assert.Throws<InvalidDataException>(() => library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("changed")]
    [InlineData("identity")]
    public void StoreInstallationRejectsUnlistedChangedOrMisidentifiedPayloads(string failure)
    {
        var game = Game(); var source = Folder(); var release = StoreRelease(source);
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        if (failure == "extra") File.WriteAllText(Path.Combine(source, "extra.txt"), "unexpected");
        if (failure == "changed") File.AppendAllText(Path.Combine(source, "plugin.toml"), "\n# changed\n");
        if (failure == "identity")
        {
            File.WriteAllText(Path.Combine(source, "plugin.toml"), Manifest.Replace("fixture.optional", "fixture.other", StringComparison.Ordinal));
            release = StoreRelease(source);
        }
        Assert.Throws<InvalidDataException>(() => library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)));
        Assert.Empty(library.Packages(game));
    }
    private (string Folder, (StorePackage Package, byte[] Manifest) Release) ArchiveRelease(string zip)
    {
        var source = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(source);
        File.Copy(zip, Path.Combine(source, "plugin.zip"));
        var bytes = File.ReadAllBytes(zip);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var manifest = Encoding.UTF8.GetBytes($"schema = 1\n[release]\nformat = \"zip\"\nid = \"fixture.optional\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\n[[files]]\npath = \"plugin.zip\"\nsource_path = \"plugin.zip\"\nsize = {bytes.Length}\nsha256 = \"{hash}\"\n");
        var package = new StorePackage("fixture.optional", "plugin", "fixture", ["test-1"], "1.0.0", new Dictionary<string, string> { ["en"] = "Fixture" },
            new Dictionary<string, string>(), "Fixture", bytes.Length, "manifest.toml", Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant());
        return (source, (package, manifest));
    }

    [Theory]
    [InlineData("../escape.txt", 0)]
    [InlineData("CON.txt", 0)]
    [InlineData("linked", 0xa000 << 16)]
    [InlineData("PLUGIN.TOML", 0)]
    public void StoreZipRejectsUnsafeContentsWithoutLosingLocalPreview(string path, int attributes)
    {
        var game = Game();
        var (source, release) = ArchiveRelease(Zip(("plugin.toml", Manifest, 0), (path, "unexpected", attributes)));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var pending = library.Prepare(Folder(Manifest.Replace("fixture.optional", "fixture.other", StringComparison.Ordinal)), game, game.Builds[0]);
        Assert.ThrowsAny<Exception>(() => library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)));
        Assert.Empty(library.Packages(game));
        Assert.Equal("fixture.other", library.Install(pending.Token, game, game.Builds[0]).Id);
    }

    [Fact]
    public void StoreZipRejectsChangedTransportAndUnsupportedFormat()
    {
        var game = Game(); var (source, release) = ArchiveRelease(Zip(("plugin.toml", Manifest, 0)));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        File.AppendAllText(Path.Combine(source, "plugin.zip"), "changed");
        Assert.Throws<InvalidDataException>(() => library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)));
        var unsupported = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(release.Manifest).Replace("format = \"zip\"", "format = \"exe\"", StringComparison.Ordinal));
        var package = release.Package with { ManifestSha256 = Convert.ToHexString(SHA256.HashData(unsupported)).ToLowerInvariant() };
        Assert.Throws<DefinitionException>(() => ClientRelease.Parse(package, unsupported));
        Assert.Empty(library.Packages(game));
    }

    private string Zip(params (string Path, string Text, int Attributes)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Path); entry.ExternalAttributes = item.Attributes;
            using var writer = new StreamWriter(entry.Open()); writer.Write(item.Text);
        }
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FolderAndWrappedZipAreCopiedWithoutSelectingOrOverwriting(bool zipped)
    {
        var game = Game(); var build = game.Builds[0]; var source = Folder();
        Directory.CreateDirectory(Path.Combine(source, "data")); File.WriteAllText(Path.Combine(source, "data", "options.txt"), "original");
        if (zipped) source = Zip(("enclosing/plugin.toml", Manifest, 0), ("enclosing/data/options.txt", "original", 0));
        var libraryRoot = Path.Combine(_root, "library");
        using (var library = new PluginLibrary(libraryRoot))
        {
            var preview = library.Prepare(source, game, build);
            Assert.Equal(2, preview.FileCount); Assert.Empty(library.Packages(game));
            var installed = library.Install(preview.Token, game, build);
            Assert.Equal("original", File.ReadAllText(Path.Combine(installed.Folder, "data", "options.txt")));
            Assert.Empty(PluginResolver.Defaults(game, build, library.Packages(game)));
            Assert.Throws<InvalidOperationException>(() => library.Install(preview.Token, game, build));
            Assert.Throws<InvalidOperationException>(() => library.Prepare(source, game, build));
            Assert.Single(library.Packages(game));
            var next = library.Prepare(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), game, build);
            library.Install(next.Token, game, build);
            var client = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
                [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]);
            Assert.Equal("1.0.0", Assert.Single(PluginResolver.Resolve(client, build, library.Packages(game))).Version);
        }
        using var reopened = new PluginLibrary(libraryRoot);
        Assert.Equal(["2.0.0", "1.0.0"], reopened.Packages(game).Select(p => p.Version));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("sub\\escape.txt")]
    [InlineData("sub/CON.txt")]
    [InlineData("sub/file:stream")]
    [InlineData("sub/file. ")]
    [InlineData("sub/../file")]
    [InlineData("session.toml")]
    public void ZipRejectsUnsafePathsAndLeavesLibraryEmpty(string path)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.ThrowsAny<Exception>(() => library.Prepare(Zip(("plugin.toml", Manifest, 0), (path, "unsafe", 0)), game, game.Builds[0]));
        Assert.Empty(library.Packages(game));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "library", "staging")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Theory]
    [InlineData("plugin.toml", "PLUGIN.toml")]
    [InlineData("data/a.txt", "DATA/b.txt")]
    [InlineData("data", "data/file.txt")]
    public void ZipRejectsCaseAliasesAndFileDirectoryConflicts(string first, string second)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var entries = new List<(string, string, int)> { (first, Manifest, 0), (second, Manifest, 0) };
        if (first != "plugin.toml") entries.Add(("plugin.toml", Manifest, 0));
        Assert.Throws<InvalidDataException>(() => library.Prepare(Zip(entries.ToArray()), game, game.Builds[0]));
        Assert.Empty(library.Packages(game));
    }

    [Theory]
    [InlineData(0xa000 << 16)]
    [InlineData((int)FileAttributes.ReparsePoint)]
    public void ZipRejectsLinks(int attributes)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.Throws<InvalidDataException>(() => library.Prepare(Zip(("plugin.toml", Manifest, 0), ("linked", "target", attributes)), game, game.Builds[0]));
    }

    [Theory]
    [InlineData("game = \"fixture\"", "game = \"other\"")]
    [InlineData("builds = [\"test-1\"]", "builds = [\"other-1\"]")]
    [InlineData("[compatibility]", "[compatibility]\nrequired_on = [\"test-1\"]")]
    [InlineData(">=3.0.0 <4.0.0", ">=99.0.0")]
    public void ImportRejectsWrongGameBuildRuntimeAndGlobalRequirements(string from, string to)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.ThrowsAny<Exception>(() => library.Prepare(Folder(Manifest.Replace(from, to, StringComparison.Ordinal)), game, game.Builds[0]));
        Assert.Empty(library.Packages(game));
    }

    [Fact]
    public void ImportCannotShadowBundledIdentityEvenWithHigherVersion()
    {
        var game = Game() with { StoreManagedPlugins = ["fixture.optional"] };
        var folder = Path.Combine(game.Folder, "plugins", "optional"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), Manifest);
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.Throws<InvalidDataException>(() => library.Prepare(Folder(Manifest.Replace("1.0.0", "99.0.0", StringComparison.Ordinal)), game, game.Builds[0]));
        Assert.Equal("1.0.0", Assert.Single(library.Packages(game)).Version);
    }

    [Fact]
    public void MissingOrInvalidNativeLibraryCannotBeInstalled()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var folder = Folder(Manifest.Replace("kind = \"patch\"", "kind = \"native\"", StringComparison.Ordinal) + "\n[native]\nlibrary = \"test.dll\"\n");
        Assert.Throws<FileNotFoundException>(() => library.Prepare(folder, game, game.Builds[0]));
        File.WriteAllText(Path.Combine(folder, "test.dll"), "not a PE image");
        Assert.Throws<BadImageFormatException>(() => library.Prepare(folder, game, game.Builds[0]));
        File.Copy(typeof(PluginLibrary).Assembly.Location, Path.Combine(folder, "test.dll"), overwrite: true);
        Assert.Throws<InvalidDataException>(() => library.Prepare(folder, game, game.Builds[0]));
        Assert.Empty(library.Packages(game));
    }

    [Fact]
    public void PreviewPinsAllBytesAndCancellationDiscardsOnlyStaging()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library")); var folder = Folder();
        var preview = library.Prepare(folder, game, game.Builds[0]);
        File.WriteAllText(Path.Combine(preview.Plugin.Folder, "extra.txt"), "changed after preview");
        Assert.Throws<IOException>(() => library.Install(preview.Token, game, game.Builds[0]));
        library.Discard(preview.Token);
        Assert.Empty(library.Packages(game));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.Prepare(folder, game, game.Builds[0], canceled.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "library", "staging")));
        Assert.True(File.Exists(Path.Combine(folder, "plugin.toml")));
    }

    [Fact]
    public void LargePluginArchiveRetainsEveryPayloadAcrossLibraryRestart()
    {
        var game = Game();
        var entries = Enumerable.Range(0, 520).Select(index => ($"assets/{index:D4}.txt", $"payload {index}", 0))
            .Prepend(("plugin.toml", Manifest, 0)).ToArray();
        var root = Path.Combine(_root, "library");
        using (var library = new PluginLibrary(root))
        {
            var preview = library.Prepare(Zip(entries), game, game.Builds[0]);
            var installed = library.Install(preview.Token, game, game.Builds[0]);
            Assert.Equal(entries.Length, Directory.GetFiles(installed.Folder, "*", SearchOption.AllDirectories).Length);
        }
        using var restarted = new PluginLibrary(root);
        var retained = Assert.Single(restarted.Packages(game));
        Assert.Equal("payload 0", File.ReadAllText(Path.Combine(retained.Folder, "assets/0000.txt")));
        Assert.Equal("payload 519", File.ReadAllText(Path.Combine(retained.Folder, "assets/0519.txt")));
        Assert.Equal(entries.Length, Directory.GetFiles(retained.Folder, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void LargeStoreReceiptSupportsIdempotentReinstallationAndLibraryRestart()
    {
        var game = Game();
        var entries = Enumerable.Range(0, 520).Select(index => ($"assets/{new string('x', 80)}-{index:D4}.txt", $"payload {index}", 0))
            .Prepend(("plugin.toml", Manifest, 0)).ToArray();
        var (source, release) = ArchiveRelease(Zip(entries));
        var root = Path.Combine(_root, "library");
        string installedFolder;
        using (var library = new PluginLibrary(root))
        {
            var installed = library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64));
            installedFolder = installed.Folder;
            Assert.True(new FileInfo(Path.Combine(installedFolder, ".fantasytale-source.json")).Length > 65536);
            Assert.True(library.MatchesInstalledSnapshot(installed));
            Assert.Equal(installedFolder, library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)).Folder);
        }
        using var restarted = new PluginLibrary(root);
        Assert.Equal(installedFolder, Assert.Single(restarted.Packages(game)).Folder);
        var retained = restarted.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64));
        Assert.Equal(installedFolder, retained.Folder);
        Assert.True(restarted.MatchesInstalledSnapshot(retained));
    }

    [Fact]
    public void LargePackagesAndInterruptedStagingRemainUsable()
    {
        var game = Game(); var source = Folder();
        using (var file = File.Create(Path.Combine(source, "large.bin"))) file.SetLength(128L * 1024 * 1024 + 1);
        var libraryRoot = Path.Combine(_root, "library");
        using (var library = new PluginLibrary(libraryRoot))
        {
            var preview = library.Prepare(source, game, game.Builds[0]);
            Assert.Equal("fixture.optional", library.Install(preview.Token, game, game.Builds[0]).Id);
        }
        var abandoned = Path.Combine(libraryRoot, "staging", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(abandoned);
        for (var index = 0; index < 520; index++) File.WriteAllText(Path.Combine(abandoned, $"partial-{index}.tmp"), "interrupted");
        using var restarted = new PluginLibrary(libraryRoot);
        Assert.False(Directory.Exists(abandoned)); Assert.Single(restarted.Packages(game));
    }

    [Fact]
    public void ZipRejectsHiddenEntriesAndAcceptsLargeExpandedPackages()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var path = Zip(("plugin.toml", Manifest, 0), ("extra.txt", "data", 0));
        var bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 12), 1);
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => library.Prepare(path, game, game.Builds[0]));
        path = Zip(("plugin.toml", Manifest, 0));
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var output = zip.CreateEntry("large.bin", CompressionLevel.Fastest).Open();
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 129; i++) output.Write(block);
        }
        var preview = library.Prepare(path, game, game.Builds[0]);
        var installed = library.Install(preview.Token, game, game.Builds[0]);
        Assert.Equal(129L * 1024 * 1024, new FileInfo(Path.Combine(installed.Folder, "large.bin")).Length);
        Assert.Single(library.Packages(game));
    }

    [Fact]
    public async Task BridgeImportsAndPinsVersionsIndependentlyForEachClient()
    {
        var game = Game();
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), Path.Combine(_root, "data"));
        var replies = new List<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object? arguments = null)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]); return reply["result"];
        }
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var preview = await Call("plugins.prepareImport", new { game = "fixture", path = Folder(Manifest.Replace("1.0.0", version, StringComparison.Ordinal)) });
            await Call("plugins.installImport", new { game = "fixture", token = preview!["token"]!.GetValue<string>() });
        }
        foreach (var id in new[] { "first", "second" }) await Call("instances.create", new { id, name = id, game = game.Id, build = game.Builds[0].Id });
        var initial = (await Call("plugins.list", new { instanceId = "first" }))!.AsArray();
        Assert.Equal(2, initial.Count); Assert.All(initial, p => { Assert.Null(p!["selection"]); Assert.False(p["builtIn"]!.GetValue<bool>()); });
        foreach (var id in new[] { "first", "second" }) await Call("plugins.configure", new { instanceId = id, id = "fixture.optional", version = "1.0.0", enabled = true });
        await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "2.0.0", enabled = true });
        var first = (await Call("plugins.list", new { instanceId = "first" }))!.AsArray();
        Assert.Equal("2.0.0", Assert.Single(first, p => p!["selection"] is not null)!["version"]!.GetValue<string>());
        var second = (await Call("plugins.list", new { instanceId = "second" }))!.AsArray();
        Assert.Equal("1.0.0", Assert.Single(second, p => p!["selection"] is not null)!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task BridgeImportSelectsNewerReleaseForExistingCompatibleClients()
    {
        var game = Game();
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), Path.Combine(_root, "import-upgrade-data"));
        var replies = new List<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object? arguments = null)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]); return reply["result"];
        }

        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        var v1 = Folder(Manifest + "\n[[options]]\nkey = \"font_color\"\ntype = \"enum\"\ndefault = \"white\"\nvalues = [\"white\", \"yellow\"]\nlabel = { en = \"Color\" }\n");
        var preview = await Call("plugins.prepareImport", new { game = "fixture", path = v1 });
        await Call("plugins.installImport", new { game = "fixture", token = preview!["token"]!.GetValue<string>() });
        await Call("instances.create", new { id = "client", name = "Client", game = game.Id, build = game.Builds[0].Id });
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "1.0.0", enabled = true, options = new { font_color = "yellow" } });

        var v2 = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal) + "\n[[options]]\nkey = \"font_color\"\ntype = \"enum\"\ndefault = \"white\"\nvalues = [\"white\", \"yellow\"]\nlabel = { en = \"Color\" }\n");
        preview = await Call("plugins.prepareImport", new { game = "fixture", path = v2 });
        await Call("plugins.installImport", new { game = "fixture", token = preview!["token"]!.GetValue<string>() });

        var listed = await Call("plugins.list", new { instanceId = "client" });
        var selection = Assert.Single(listed!.AsArray(), item => item!["selection"] is not null)!["selection"]!;
        Assert.Equal("2.0.0", selection!["version"]!.GetValue<string>());
        Assert.Equal("yellow", selection["options"]!["font_color"]!.GetValue<string>());
    }

    [Fact]
    public async Task InstallationInspectionAcceptsOptionalGameAndRejectsUnrecognizedParameters()
    {
        var game = Game();
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), Path.Combine(_root, "inspection-data"));
        var replies = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 1, method = "instances.inspectInstallation", @params = new { id = "probe", game = game.Id } }), default);
        var reply = replies.Single(item => item["id"]?.GetValue<int>() == 1);
        Assert.Null(reply["error"]);
        Assert.Empty(reply["result"]!["detectedBuildIds"]!.AsArray());
        await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 2, method = "instances.inspectInstallation", @params = new { id = "legacy-probe" } }), default);
        Assert.Null(replies.Single(item => item["id"]?.GetValue<int>() == 2)["error"]);
        await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 3, method = "instances.inspectInstallation", @params = new { id = "invalid-probe", unexpected = true } }), default);
        Assert.NotNull(replies.Single(item => item["id"]?.GetValue<int>() == 3)["error"]);
    }

    [Fact]
    public async Task PluginLibraryCanBeBrowsedBeforeClientRegistrationAndSharesPackagesAcrossBuilds()
    {
        var game = Game();
        var secondBuild = Path.Combine(game.Folder, "builds", "test-2.toml");
        File.WriteAllText(secondBuild, File.ReadAllText(Path.Combine(game.Folder, "builds", "test-1.toml")).Replace("test-1", "test-2", StringComparison.Ordinal));
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var folder = Path.Combine(game.Folder, "plugins", version); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "plugin.toml"), Manifest.Replace("1.0.0", version, StringComparison.Ordinal)
                .Replace("[\"test-1\"]", "[\"test-1\", \"test-2\"]", StringComparison.Ordinal));
        }
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), Path.Combine(_root, "library-browse"));
        var replies = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object arguments)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]); return reply["result"];
        }
        var library = (await Call("plugins.list", new { }))!.AsArray();
        Assert.Equal(2, library.Count);
        Assert.Equal("2.0.0", library[0]!["version"]!.GetValue<string>());
        Assert.All(library, plugin =>
        {
            Assert.Equal(game.Id, plugin!["game"]!.GetValue<string>());
            Assert.Equal("fixture.optional", plugin["id"]!.GetValue<string>());
            Assert.Null(plugin["selection"]);
            Assert.False(plugin["available"]!.GetValue<bool>());
            Assert.False(plugin["required"]!.GetValue<bool>());
        });
        Assert.Empty((await Call("instances.list", new { }))!.AsArray());
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        foreach (var build in new[] { "test-1", "test-2" })
        {
            await Call("instances.create", new { id = build, name = build, game = game.Id, build });
            var scoped = (await Call("plugins.list", new { instanceId = build }))!.AsArray();
            Assert.Equal(2, scoped.Count);
            Assert.All(scoped, plugin => Assert.True(plugin!["available"]!.GetValue<bool>()));
        }
    }

    [Fact]
    public async Task PluginVersionSwitchPreservesSharedOptionsAndRequiresExplicitRepairForIncompatibleValues()
    {
        var game = Game();
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), Path.Combine(_root, "option-upgrade-data"));
        var replies = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object arguments, bool fails = false)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
            if (fails) Assert.NotNull(reply["error"]); else Assert.Null(reply["error"]);
            return reply["result"];
        }
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        await Call("instances.create", new { id = "client", name = "Client", game = game.Id, build = game.Builds[0].Id });
        const string color = "\n[[options]]\nkey = \"font_color\"\ntype = \"enum\"\ndefault = \"white\"\nvalues = [\"white\", \"yellow\"]\nlabel = { en = \"Color\" }\n";
        const string removed = "\n[[options]]\nkey = \"old_setting\"\ntype = \"bool\"\ndefault = false\nlabel = { en = \"Old setting\" }\n";
        foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
        {
            var schema = version == "3.0.0" ? color.Replace("[\"white\", \"yellow\"]", "[\"white\"]", StringComparison.Ordinal) : color;
            var path = Folder(Manifest.Replace("1.0.0", version, StringComparison.Ordinal) + schema + (version == "1.0.0" ? removed : ""));
            var preview = await Call("plugins.prepareImport", new { game = "fixture", path });
            await Call("plugins.installImport", new { game = "fixture", token = preview!["token"]!.GetValue<string>() });
        }
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "1.0.0", enabled = true, options = new { font_color = "yellow", old_setting = true } });
        var switched = await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "2.0.0", enabled = true });
        var selection = Assert.Single(switched!["plugins"]!.AsArray())!;
        Assert.Equal("yellow", selection["options"]!["font_color"]!.GetValue<string>());
        Assert.Null(selection["options"]!["old_setting"]);
        AssertSession("2.0.0", "yellow");
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "3.0.0", enabled = true }, fails: true);
        var listed = await Call("plugins.list", new { instanceId = "client" });
        Assert.Equal("2.0.0", Assert.Single(listed!.AsArray(), item => item!["selection"] is not null)!["version"]!.GetValue<string>());
        AssertSession("2.0.0", "yellow");
        var reset = await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "3.0.0", enabled = true, options = new { } });
        Assert.Empty(Assert.Single(reset!["plugins"]!.AsArray())!["options"]!.AsObject());
        AssertSession("3.0.0", "white");
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "2.0.0", enabled = true, options = new { font_color = "yellow" } });
        AssertSession("2.0.0", "yellow");
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "2.0.0", enabled = false });
        var disabled = new InstanceStore(Path.Combine(_root, "option-upgrade-data", "instances")).Load("client");
        Assert.Equal("yellow", Assert.Single(disabled.Plugins).Options["font_color"]);
        using var installed = new PluginLibrary(Path.Combine(_root, "option-upgrade-data", "plugins"));
        Assert.Empty(PluginResolver.Resolve(disabled, game.Builds[0], installed.Packages(game)));
        await Call("plugins.configure", new { instanceId = "client", id = "fixture.optional", version = "2.0.0", enabled = true });
        AssertSession("2.0.0", "yellow");

        void AssertSession(string version, string colorValue)
        {
            var persisted = new InstanceStore(Path.Combine(_root, "option-upgrade-data", "instances")).Load("client");
            using var library = new PluginLibrary(Path.Combine(_root, "option-upgrade-data", "plugins"));
            var packages = library.Packages(game);
            Assert.Equal(3, packages.Count);
            var plugins = PluginResolver.Resolve(persisted, game.Builds[0], packages);
            var spec = new SessionSpec(game.Id, game.Builds[0], persisted.Id, Path.Combine(_root, "hook.log"), null, null, null, plugins);
            var session = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(spec))!;
            var entry = Assert.IsType<TomlTable>(Assert.Single(Assert.IsType<TomlTableArray>(session["plugins"])));
            Assert.Equal(version, entry["version"]);
            Assert.Equal(Assert.Single(packages, package => package.Version == version).Folder, entry["path"]);
            var values = Assert.IsType<TomlTable>(entry["options"]);
            Assert.Equal(colorValue, values["font_color"]);
            Assert.False(values.ContainsKey("old_setting"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomConfigurationImportsWithoutActivationAndPersistsValidatedClientOptions(bool declaredPages)
    {
        var game = Game();
        var source = Folder(Manifest + "\n[[options]]\nkey = \"font_color\"\ntype = \"enum\"\ndefault = \"white\"\nvalues = [\"white\", \"yellow\"]\nlabel = { en = \"Color\" }\n");
        const string page = "<html><body><output>100</output></body></html>";
        File.WriteAllText(Path.Combine(source, "settings.html"), page);
        File.WriteAllText(Path.Combine(source, "plugin-ui.toml"), declaredPages ? """
            schema = 1
            [[pages]]
            id = "colors"
            kind = "html"
            path = "settings.html"
            title = { en = "Colors" }
            options = ["font_color"]
            """ : "schema = 1\n[ui]\npage = \"settings.html\"\n");

        var data = Path.Combine(_root, "data");
        await using (var backend = new LauncherBackend(Path.Combine(_root, "games"), data))
        {
            var replies = new List<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
            long sequence = 0;
            async Task<JsonNode> Request(string method, object arguments)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
                return replies.Single(item => item["id"]?.GetValue<long>() == id);
            }
            async Task<JsonNode?> Call(string method, object arguments)
            {
                var reply = await Request(method, arguments); Assert.Null(reply["error"]); return reply["result"];
            }
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
            foreach (var id in new[] { "first", "second" })
                await Call("instances.create", new { id, name = id, game = game.Id, build = game.Builds[0].Id });
            var preview = await Call("plugins.prepareImport", new { game = "fixture", path = source });
            await Call("plugins.installImport", new { game = "fixture", token = preview!["token"]!.GetValue<string>() });
            var initial = Assert.Single((await Call("plugins.list", new { instanceId = "first" }))!.AsArray())!;
            Assert.True(initial["configurationPage"]!.GetValue<bool>()); Assert.Null(initial["selection"]);
            if (declaredPages)
            {
                var declared = Assert.Single(initial["configurationPages"]!.AsArray())!;
                Assert.Equal("html", declared["kind"]!.GetValue<string>());
                Assert.Equal("font_color", Assert.Single(declared["options"]!.AsArray())!.GetValue<string>());
                Assert.Equal(page, (await Call("plugins.configurationPage", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", pageId = "colors" }))!.GetValue<string>());
                Assert.NotNull((await Request("plugins.configurationPage", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", pageId = "missing" }))["error"]);
            }
            else Assert.Equal(page, (await Call("plugins.configurationPage", new { instanceId = "first", id = "fixture.optional", version = "1.0.0" }))!.GetValue<string>());
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false, options = new { font_color = "yellow" } });
            foreach (var options in new object[] { new { font_color = "red" }, new { undeclared = true } })
                Assert.NotNull((await Request("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false, options }))["error"]);
            Assert.Null(Assert.Single((await Call("plugins.list", new { instanceId = "second" }))!.AsArray())!["selection"]);
        }
        await using var reopened = new LauncherBackend(Path.Combine(_root, "games"), data);
        JsonNode? result = null;
        var reopenedBridge = new BridgeRouter(raw => { var reply = JsonNode.Parse(raw)!; if (reply["id"] is not null) { Assert.Null(reply["error"]); result = reply["result"]; } });
        reopened.Register(reopenedBridge);
        await reopenedBridge.HandleAsync(JsonSerializer.Serialize(new { id = 1, method = "plugins.list", @params = new { instanceId = "first" } }), default);
        var saved = Assert.Single(result!.AsArray())!["selection"]!;
        Assert.False(saved["enabled"]!.GetValue<bool>());
        Assert.Equal("yellow", saved["options"]!["font_color"]!.GetValue<string>());
    }

    [Fact]
    public void CustomConfigurationPageCannotChangeAfterImportPreview()
    {
        var game = Game(); var source = Folder();
        File.WriteAllText(Path.Combine(source, "plugin-ui.toml"), "schema = 1\n[ui]\npage = \"settings.html\"\n");
        File.WriteAllText(Path.Combine(source, "settings.html"), "<output>original</output>");
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(source, game, game.Builds[0]);
        File.WriteAllText(Path.Combine(preview.Plugin.Folder, "settings.html"), "<output>changed</output>");
        Assert.Throws<IOException>(() => library.Install(preview.Token, game, game.Builds[0]));
        Assert.Empty(library.Packages(game));
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredOptionalBundledPluginsAcceptStoreVersionsAndRetainTheirOrigin(bool sameVersion)
    {
        var game = Game() with { StoreManagedPlugins = ["fixture.optional"] };
        var bundled = Path.Combine(game.Folder, "plugins", "optional"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), sameVersion ? Manifest : Manifest.Replace("1.0.0", "0.9.0", StringComparison.Ordinal));
        var source = Folder(); var release = StoreRelease(source); var root = Path.Combine(_root, "library");
        using (var library = new PluginLibrary(root))
        {
            Assert.Throws<InvalidDataException>(() => library.Prepare(source, game, game.Builds[0]));
            var installed = library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64));
            var selected = Assert.Single(library.Packages(game), p => p.Version == "1.0.0");
            Assert.Equal(installed.Folder, selected.Folder); Assert.NotNull(library.Origin(selected));
        }
        using var reopened = new PluginLibrary(root);
        Assert.NotNull(reopened.Origin(Assert.Single(reopened.Packages(game), p => p.Version == "1.0.0")));
        Assert.Equal(sameVersion ? 1 : 2, reopened.Packages(game).Count);
    }

    [Theory]
    [InlineData("undeclared")]
    [InlineData("required")]
    [InlineData("different-bytes")]
    public void StoreOverlaysCannotReplaceReservedOrChangedBundledPackages(string rejection)
    {
        var game = Game() with { StoreManagedPlugins = rejection == "undeclared" ? [] : ["fixture.optional"] };
        var bundled = Path.Combine(game.Folder, "plugins", "optional"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), rejection == "required"
            ? Manifest.Replace("[plugin]", "[plugin]\nupdate_source = \"owner\"", StringComparison.Ordinal) : Manifest);
        var source = Folder(rejection == "different-bytes" ? Manifest + "\n# changed\n" : Manifest);
        var release = StoreRelease(source); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        Assert.Throws<InvalidDataException>(() => library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("enabled")]
    [InlineData("empty")]
    [InlineData("uninstalled")]
    public async Task RecommendationsAreDeclaredPerBuildAndAcknowledgedOnceAcrossRestart(string scenario)
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Recommendations(scenario == "empty" ? [] : ["fixture.optional"]) };
        var game = original with { Builds = [build] }; using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(), game, build); library.Install(preview.Token, game, build);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var selected = scenario is "enabled" or "disabled" ? new[] { new InstancePlugin("fixture.optional", "1.0.0", scenario == "enabled", false, new Dictionary<string, object>()) } : [];
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null, selected));
        if (scenario != "uninstalled") File.WriteAllText(Path.Combine(store.ClientFolder("first"), "client.exe"), "fixture");
        var claim = ClientPolicyActivation.Claim(store, "first", game, library.Packages(game));
        if (scenario is "missing" or "disabled") Assert.Equal(["fixture.optional"], claim); else Assert.Empty(claim);
        Assert.Empty(ClientPolicyActivation.Claim(new InstanceStore(Path.Combine(_root, "instances")), "first", game, library.Packages(game)));
        Assert.Equal(scenario is not ("empty" or "uninstalled"), File.Exists(Path.Combine(_root, "instances", "first", "client-policy.seen")));
        if (scenario == "enabled")
        {
            store.Save(store.Load("first") with { Plugins = [selected[0] with { Enabled = false }] });
            Assert.Empty(ClientPolicyActivation.Claim(store, "first", game, library.Packages(game)));
        }
        await store.CreateAsync(new("second", "Second", game.Id, build.Id, null, []));
        File.WriteAllText(Path.Combine(store.ClientFolder("second"), "client.exe"), "fixture");
        if (scenario != "empty") Assert.Single(ClientPolicyActivation.Claim(store, "second", game, library.Packages(game)));
    }

    [Fact]
    public void EnabledSelectionsWithoutTheirExactPackageAreMissing()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        library.Install(library.Prepare(Folder(), game, build).Token, game, build);
        var instance = new InstanceDefinition("first", "First", game.Id, build.Id, null,
        [
            new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>()),
            new("fixture.gone", "1.0.0", true, true, new Dictionary<string, object>()),
            new("fixture.off", "1.0.0", false, false, new Dictionary<string, object>()),
        ]);
        Assert.Equal(["fixture.gone"], ClientPolicyActivation.MissingSelections(instance, library.Packages(game)));
        Assert.Equal(["fixture.optional", "fixture.gone"], ClientPolicyActivation.MissingSelections(instance with
            { Plugins = [instance.Plugins[0] with { Version = "2.0.0" }, instance.Plugins[1], instance.Plugins[2]] }, library.Packages(game)));
    }

    [Fact]
    public async Task NewlyDeclaredRecommendationsHaveTheirOwnOneTimeAcknowledgement()
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Recommendations("fixture.optional") };
        var game = original with { Builds = [build] }; var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null, []));
        File.WriteAllText(Path.Combine(store.ClientFolder("first"), "client.exe"), "fixture");
        Assert.Equal(["fixture.optional"], ClientPolicyActivation.Claim(store, "first", game, []));
        game = game with { Builds = [build with { ClientPolicy = PolicyFixture.Recommendations("fixture.optional", "fixture.later") }] };
        Assert.Equal(["fixture.later"], ClientPolicyActivation.Claim(store, "first", game, []));
        Assert.Empty(ClientPolicyActivation.Claim(new InstanceStore(Path.Combine(_root, "instances")), "first", game, []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecommendationActivationPreservesOptionsAndRejectsNewerChoices(bool serverRecommended)
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Recommendations(serverRecommended ? [] : ["fixture.optional"]) }; var game = original with { Builds = [build] };
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(Manifest + "\n[[options]]\nkey = \"bold\"\ntype = \"bool\"\ndefault = true\nlabel = { en = \"Bold\" }\n"), game, build);
        library.Install(preview.Token, game, build);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null, [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object> { ["bold"] = false })]));
        var initial = store.Load("first"); var stamp = ClientPolicyActivation.SelectionStamp(initial, "fixture.optional");
        store.Save(initial with { Name = "Renamed" });
        if (serverRecommended) Assert.Throws<InvalidOperationException>(() => ClientPolicyActivation.Enable(store, "first", game, library.Packages(game), "fixture.optional", "1.0.0", stamp));
        ClientPolicyActivation.Enable(store, "first", game, library.Packages(game), "fixture.optional", "1.0.0", stamp, serverRecommended);
        var selected = Assert.Single(store.Load("first").Plugins); Assert.True(selected.Enabled); Assert.Equal(false, selected.Options["bold"]);
        // Replaying a completed activation is harmless after a restart.
        ClientPolicyActivation.Enable(store, "first", game, library.Packages(game), "fixture.optional", "1.0.0", stamp, serverRecommended);
        store.Save(store.Load("first") with { Plugins = [selected with { Enabled = false, Options = new Dictionary<string, object> { ["bold"] = true } }] });
        Assert.Throws<InvalidOperationException>(() => ClientPolicyActivation.Enable(store, "first", game, library.Packages(game), "fixture.optional", "1.0.0", stamp, serverRecommended));
        Assert.False(Assert.Single(store.Load("first").Plugins).Enabled);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("unsupported-hook")]
    [InlineData("enabled")]
    public async Task RequiredServerPluginsAreRecheckedBeforeModulePreparationWithoutPersistingServerLocks(string scenario)
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(), game, build); library.Install(preview.Token, game, build);
        var packages = library.Packages(game);
        if (scenario == "unsupported-hook") game = game with { SupportsPlugins = false };
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null, scenario == "missing" ? [] :
            [new("fixture.optional", "1.0.0", scenario != "disabled", false, new Dictionary<string, object>())]));
        var module = new RejectingModule();
        var service = new LaunchService(store);
        var error = Assert.Throws<InvalidOperationException>(() => service.Start("first", game, module, packages, requiredPlugins: ["fixture.optional"]));
        Assert.Equal(scenario == "enabled", module.Prepared);
        Assert.Contains(scenario == "enabled" ? "Fixture preparation" : scenario == "unsupported-hook" ? "required hook" : "server_plugin_required", error.Message, StringComparison.Ordinal);
        Assert.All(store.Load("first").Plugins, selection => Assert.False(selection.Required));
        Assert.False(service.HasPendingStarts);
        using var lease = store.Acquire("first");
    }

    private sealed class RejectingModule : IGameModule
    {
        public string GameId => "fixture";
        internal bool Prepared { get; private set; }
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        { Prepared = true; throw new InvalidOperationException("Fixture preparation stopped before process creation."); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("changed")]
    [InlineData("corrupt")]
    [InlineData("intent-corrupt")]
    [InlineData("intent-mismatch")]
    [InlineData("server-missing")]
    [InlineData("server-changed")]
    [InlineData("server-corrupt")]
    [InlineData("server-intent-mismatch")]
    public async Task RecommendedDownloadsRestoreActivationIntentAndEnableOnlyVerifiedUnchangedSelections(string scenario)
    {
        var serverRecommended = scenario.StartsWith("server-", StringComparison.Ordinal);
        if (serverRecommended) scenario = scenario[7..];
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Recommendations(serverRecommended ? [] : ["fixture.optional"]) }; var game = original with { Builds = [build] };
        var sourceFolder = Folder(); var release = StoreRelease(sourceFolder);
        var bytes = File.ReadAllBytes(Path.Combine(sourceFolder, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null, []));
        if (scenario == "disabled")
        {
            library.InstallFromStore(sourceFolder, game, build, release.Package, release.Manifest, source.Id,
                StoreSourceIdentity.Of(source));
            store.Save(store.Load("first") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        }
        var tasks = Path.Combine(_root, "tasks");
        using (var stalled = new RecommendationHandler(bytes, stalled: true))
        using (var http = new HttpClient(stalled))
        {
            await using var downloads = new ClientDownloadService(store, http, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
            if (serverRecommended) Assert.Throws<ArgumentException>(() => downloads.Start(source, release.Package, release.Manifest, "first", serverRecommended: true));
            downloads.Start(source, release.Package, release.Manifest, "first", recommendationSelection: ClientPolicyActivation.SelectionStamp(store.Load("first"), "fixture.optional"), serverRecommended: serverRecommended);
            await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(store.Load("first").Plugins, p => p.Enabled);
        }
        var record = Assert.Single(Directory.GetFiles(tasks, "*.json"));
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(record)))
        {
            Assert.False(document.RootElement.TryGetProperty("RecommendationSelection", out _));
            Assert.False(document.RootElement.TryGetProperty("ServerRecommended", out _));
            Assert.Equal(1, document.RootElement.GetProperty("Schema").GetInt32());
        }
        var intentPath = Path.ChangeExtension(record, ".recommendation");
        Assert.True(File.Exists(intentPath));
        using (var intent = JsonDocument.Parse(File.ReadAllBytes(intentPath)))
            Assert.Equal(["Schema", "Identity", "Selection"], intent.RootElement.EnumerateObject().Select(property => property.Name));
        if (serverRecommended) { intentPath = Path.ChangeExtension(record, ".server-recommendation"); Assert.True(File.Exists(intentPath)); }
        if (scenario == "intent-corrupt") File.WriteAllText(intentPath, "{}");
        if (scenario == "intent-mismatch") File.WriteAllText(intentPath, File.ReadAllText(intentPath).Replace("\"Identity\":\"", "\"Identity\":\"bad", StringComparison.Ordinal));
        if (scenario == "changed") store.Save(store.Load("first") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        using var handler = new RecommendationHandler(scenario == "corrupt" ? new byte[bytes.Length] : bytes);
        using var nextHttp = new HttpClient(handler);
        await using var reopened = new ClientDownloadService(store, nextHttp, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
        if (scenario is "intent-corrupt" or "intent-mismatch")
        {
            Assert.Throws<IOException>(() => reopened.List());
            Assert.DoesNotContain(store.Load("first").Plugins, p => p.Enabled);
            return;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Assert.Single(reopened.List()).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        var final = Assert.Single(reopened.List());
        Assert.Equal(scenario is "changed" or "corrupt" ? "failed" : "completed", final.Stage);
        if (scenario is "missing" or "disabled") Assert.True(Assert.Single(store.Load("first").Plugins).Enabled);
        else Assert.DoesNotContain(store.Load("first").Plugins, p => p.Enabled);
        if (scenario == "corrupt") Assert.Empty(library.Packages(game));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SharedDownloadRestoresTargetsAcrossBuildsAndPreservesLaterChoices(bool changeSecond, bool sharedActivation)
    {
        var original = Game();
        var game = original with { Builds = [original.Builds[0], original.Builds[0] with { Id = "test-2" }] };
        var folder = Folder(Manifest.Replace("builds = [\"test-1\"]", "builds = [\"test-1\", \"test-2\"]", StringComparison.Ordinal));
        var release = StoreRelease(folder);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(release.Manifest)
            .Replace("builds = [\"test-1\"]", "builds = [\"test-1\", \"test-2\"]", StringComparison.Ordinal));
        var package = release.Package with { Builds = ["test-1", "test-2"], ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, "test-1", null, []));
        await store.CreateAsync(new("second", "Second", game.Id, "test-2", null, []));
        var tasks = Path.Combine(_root, "tasks");
        using (var stalled = new RecommendationHandler(bytes, stalled: true))
        using (var http = new HttpClient(stalled))
        {
            await using var downloads = new ClientDownloadService(store, http, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
            if (sharedActivation)
            {
                var activations = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads, _ => game, _ => true, _ => source);
                var firstTask = activations.Enqueue(source, package, manifest, "first", explicitSelection: true);
                Assert.Equal(firstTask.Id, activations.Enqueue(source, package, manifest, "second", explicitSelection: true).Id);
                Assert.Equal([firstTask.Id], activations.TasksFor("first"));
            }
            else downloads.Start(source, package, manifest, "first", activationTargets: ["first", "second"]);
            await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(downloads.List());
        }
        var record = Assert.Single(Directory.GetFiles(tasks, "*.json"));
        if (!sharedActivation)
        {
            using var intent = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(record, ".plugin-targets")));
            Assert.Equal(2, intent.RootElement.GetProperty("Schema").GetInt32());
            Assert.Equal(2, intent.RootElement.GetProperty("Selections").EnumerateObject().Count());
        }
        if (changeSecond) store.Save(store.Load("second") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        using var handler = new RecommendationHandler(bytes); using var nextHttp = new HttpClient(handler);
        await using var reopened = new ClientDownloadService(store, nextHttp, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Assert.Single(reopened.List()).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", Assert.Single(reopened.List()).Stage);
        if (sharedActivation)
        {
            var restored = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, reopened, _ => game, _ => false, _ => source);
            if (changeSecond) Assert.Empty(restored.TasksFor("second"));
            restored.ApplyReady();
            Assert.Null(restored.Failure); Assert.Equal(0, restored.PendingCount);
        }
        Assert.Single(library.Packages(game));
        Assert.True(Assert.Single(store.Load("first").Plugins).Enabled);
        Assert.Equal(!changeSecond, Assert.Single(store.Load("second").Plugins).Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredOffersRemainPendingUntilTheInstalledPackageIsEnabled(bool enabled)
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Rules("fixture.optional", "require") };
        var game = original with { Builds = [build] };
        var package = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", enabled, enabled, new Dictionary<string, object>())]));
        File.WriteAllText(Path.Combine(store.ClientFolder("first"), "client.exe"), "fixture");
        var expected = enabled ? Array.Empty<string>() : ["fixture.optional"];
        Assert.Equal(expected, ClientPolicyActivation.Claim(store, "first", game, [package]));
        var restored = new InstanceStore(Path.Combine(_root, "instances"));
        Assert.Equal(expected, ClientPolicyActivation.Claim(restored, "first", game, [package]));
        if (enabled)
        {
            using var lease = store.Acquire("first");
            Assert.Empty(ClientPolicyActivation.Claim(restored, "first", game, [package]));
        }
        else
        {
            Assert.Equal(["fixture.optional"], ClientPolicyActivation.Claim(restored, "first", game, []));
        }
    }

    [Fact]
    public async Task MandatoryRecommendationsAppearOnceEvenWhenAlreadyEnabled()
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Recommendations("fixture.optional") };
        var game = original with { Builds = [build] };
        var package = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml")) with { Warning = new LocalizedText(new Dictionary<string, string> { ["en"] = "Keep enabled" }) };
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        File.WriteAllText(Path.Combine(store.ClientFolder("first"), "client.exe"), "fixture");
        Assert.True(ClientPolicyActivation.IsMandatory(package, build));
        Assert.Equal(["fixture.optional"], ClientPolicyActivation.Claim(store, "first", game, [package]));
        Assert.Empty(ClientPolicyActivation.Claim(new InstanceStore(Path.Combine(_root, "instances")), "first", game, [package]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedUpgradesDownloadWhileRunningAndRestoreDeferredSelections(bool changeWhileRunning)
    {
        var game = Game(); var build = game.Builds[0];
        var first = Folder(); var nextFolder = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var release = StoreRelease(nextFolder);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(release.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var package = release.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var bytes = File.ReadAllBytes(Path.Combine(nextFolder, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(first, game, build); library.Install(preview.Token, game, build);
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var id in new[] { "first", "second" })
            await store.CreateAsync(new(id, id, game.Id, build.Id, null, [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        var tasks = Path.Combine(_root, "tasks"); var intents = Path.Combine(_root, "activations");
        using (var stalled = new RecommendationHandler(bytes, stalled: true))
        using (var http = new HttpClient(stalled))
        {
            await using var downloads = new ClientDownloadService(store, http, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
            var upgrades = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game, _ => true, _ => source);
            var one = upgrades.Enqueue(source, package, manifest, "first");
            Assert.Equal(one.Id, upgrades.Enqueue(source, package, manifest, "second").Id);
            Assert.Equal(2, upgrades.PendingCount); Assert.Single(downloads.List());
            await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using var handler = new RecommendationHandler(bytes); using var nextHttp = new HttpClient(handler);
        await using var reopened = new ClientDownloadService(store, nextHttp, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
        var running = true;
        var restored = new PluginUpgradeActivationService(intents, store, library, reopened, _ => game, id => id == "first" && running, _ => source);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Assert.Single(reopened.List()).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", Assert.Single(reopened.List()).Stage);
        using (var lease = store.Acquire("first"))
        {
            restored.ApplyReady();
            Assert.Null(restored.Failure); Assert.Equal(1, restored.PendingCount);
            Assert.Equal("1.0.0", Assert.Single(store.Load("first").Plugins).Version);
            Assert.Equal("2.0.0", Assert.Single(store.Load("second").Plugins).Version);
            running = false;
            restored.ApplyReady();
            Assert.Null(restored.Failure); Assert.Equal(1, restored.PendingCount);
            Assert.Equal("1.0.0", Assert.Single(store.Load("first").Plugins).Version);
            running = true;
        }
        if (changeWhileRunning) store.Save(store.Load("first") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        source = source with { Enabled = false }; running = false;
        restored.ApplyReady();
        Assert.Null(restored.Failure); Assert.Equal(0, restored.PendingCount);
        var selected = Assert.Single(store.Load("first").Plugins);
        Assert.Equal("2.0.0", selected.Version); Assert.Equal(!changeWhileRunning, selected.Enabled);
        source = source with { Enabled = true };
        restored.ApplyReady();
        Assert.Null(restored.Failure); Assert.Equal(0, restored.PendingCount);
        Assert.Equivalent(selected, Assert.Single(store.Load("first").Plugins), strict: true);
        Assert.Equal(0, new PluginUpgradeActivationService(intents, store, library, reopened, _ => game, _ => false, _ => source).PendingCount);
        Assert.Equal("completed", reopened.PreloadPlugin(source, package, manifest).Stage);
        Assert.Single(reopened.List());
        var damagedRoot = Path.Combine(_root, "damaged-activations"); Directory.CreateDirectory(damagedRoot);
        var damagedPath = Path.Combine(damagedRoot, "plugin-upgrades.json"); File.WriteAllText(damagedPath, "[null]");
        var damaged = new PluginUpgradeActivationService(damagedRoot, store, library, reopened, _ => game, _ => false, _ => source);
        damaged.ApplyReady();
        Assert.Equal("integrity", damaged.Failure);
        Assert.Throws<IOException>(() => damaged.Enqueue(source, package, manifest, "first"));
        Assert.Equal("[null]", File.ReadAllText(damagedPath));
    }

    [Fact]
    public async Task DeferredUpgradesRestoreAndSaveBeyondFormerCountAndByteCeilings()
    {
        var game = Game(); var build = game.Builds[0];
        var folder = Folder(); var release = StoreRelease(folder);
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("client-0", "Client", game.Id, build.Id, null, []));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var payload = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        using var handler = new RecommendationHandler(payload, stalled: true);
        using var http = new HttpClient(handler);
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(payload.Length), library, _ => game);
        var intents = Directory.CreateDirectory(Path.Combine(_root, "activations")).FullName;
        var path = Path.Combine(intents, "plugin-upgrades.json");
        var records = Enumerable.Range(0, 777).Select(i => new
        {
            Instance = "client-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Game = game.Id, Build = build.Id, Plugin = release.Package.Id, Version = release.Package.Version,
            Selection = new string('a', 64), Task = Guid.NewGuid().ToString("N"), Source = source.Id,
            Identity = StoreSourceIdentity.Of(source), Hash = release.Package.ManifestSha256, Explicit = true,
        });
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(records) + new string(' ', 256 * 1024));
        var upgrades = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game, _ => true, _ => source);
        Assert.Null(upgrades.Failure); Assert.Equal(777, upgrades.PendingCount);
        var task = upgrades.Enqueue(source, release.Package, release.Manifest, "client-0", explicitSelection: true);
        Assert.Equal(task.Id, Assert.Single(upgrades.TasksFor("client-0")));
        Assert.Equal(777, upgrades.PendingCount);
        Assert.True(new FileInfo(path).Length > 256 * 1024);
        var restored = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game, _ => true, _ => source);
        Assert.Null(restored.Failure); Assert.Equal(777, restored.PendingCount);
        Assert.Equal(task.Id, Assert.Single(restored.TasksFor("client-0")));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SupersededStoreVersionsRetainReferencedReleases()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "retirement-library"));
        Assert.False(library.HasSupersededStorePackages());
        var oldFolder = Folder(); var old = StoreRelease(oldFolder);
        var nextFolder = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var next = StoreRelease(nextFolder);
        var nextManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(next.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var nextPackage = next.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(nextManifest)).ToLowerInvariant() };
        var identity = new string('a', 64);
        library.InstallFromStore(oldFolder, game, build, old.Package, old.Manifest, "fixture", identity);
        Assert.False(library.HasSupersededStorePackages());
        library.InstallFromStore(nextFolder, game, build, nextPackage, nextManifest, "fixture", identity);
        Assert.True(library.HasSupersededStorePackages());
        library.RetireSuperseded(new HashSet<(string, string, string)> { (game.Id, "fixture.optional", "1.0.0") });
        Assert.Equal(2, library.Packages(game).Count);
        library.RetireSuperseded(new HashSet<(string, string, string)>());
        Assert.Equal("2.0.0", Assert.Single(library.Packages(game)).Version);
        Assert.False(library.HasSupersededStorePackages());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RestartRetriesSupersededCleanupWithoutPendingActivations(bool retainOldSelection, bool removeOld)
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "retirement-library"));
        var oldFolder = Folder(); var old = StoreRelease(oldFolder);
        var nextFolder = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var next = StoreRelease(nextFolder);
        var nextManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(next.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var nextPackage = next.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(nextManifest)).ToLowerInvariant() };
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var identity = StoreSourceIdentity.Of(source);
        library.InstallFromStore(oldFolder, game, build, old.Package, old.Manifest, source.Id, identity);
        library.InstallFromStore(nextFolder, game, build, nextPackage, nextManifest, source.Id, identity);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", retainOldSelection ? "1.0.0" : "2.0.0", true, false, new Dictionary<string, object>())]));
        using var http = new HttpClient(new RecommendationHandler(File.ReadAllBytes(Path.Combine(nextFolder, "plugin.toml"))));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(0), library, _ => game);
        var intents = Path.Combine(_root, "activations"); Directory.CreateDirectory(intents);
        File.WriteAllText(Path.Combine(intents, "plugin-upgrades.json"), "[]");
        var restored = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game, _ => false, _ => source, () => removeOld);
        using (store.Acquire("first"))
        {
            restored.ApplyReady();
            Assert.Null(restored.Failure);
            Assert.Equal(2, library.Packages(game).Count);
        }
        restored.ApplyReady();
        Assert.Null(restored.Failure);
        Assert.Equal(0, restored.PendingCount);
        Assert.Equal(removeOld ? 1 : 2, library.Packages(game).Count);
        Assert.Equal(removeOld || !retainOldSelection ? "2.0.0" : "1.0.0", Assert.Single(store.Load("first").Plugins).Version);
        Assert.Contains(library.Packages(game), plugin => plugin.Version == "2.0.0");
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("disabled")]
    [InlineData("changed")]
    [InlineData("corrupt")]
    [InlineData("intent-mismatch")]
    public async Task OrdinaryPluginUpgradesRestoreIntentAndPreserveLaterChoices(string scenario)
    {
        var game = Game(); var build = game.Builds[0];
        var first = Folder(); var next = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var release = StoreRelease(next);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(release.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var package = release.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var bytes = File.ReadAllBytes(Path.Combine(next, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(first, game, build); library.Install(preview.Token, game, build);
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", scenario != "disabled", false, new Dictionary<string, object>())]));
        var tasks = Path.Combine(_root, "tasks");
        using (var stalled = new RecommendationHandler(bytes, stalled: true))
        using (var http = new HttpClient(stalled))
        {
            await using var downloads = new ClientDownloadService(store, http, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
            downloads.Start(source, package, manifest, "first");
            await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var record = Assert.Single(Directory.GetFiles(tasks, "*.json"));
        using (var document = JsonDocument.Parse(File.ReadAllText(record))) Assert.False(document.RootElement.TryGetProperty("UpgradeSelection", out _));
        var intent = Path.ChangeExtension(record, ".plugin-selection");
        Assert.True(File.Exists(intent));
        if (scenario == "intent-mismatch") File.WriteAllText(intent, File.ReadAllText(intent).Replace("\"Identity\":\"", "\"Identity\":\"bad", StringComparison.Ordinal));
        if (scenario == "changed") store.Save(store.Load("first") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        using var handler = new RecommendationHandler(scenario == "corrupt" ? new byte[bytes.Length] : bytes);
        using var nextHttp = new HttpClient(handler);
        await using var reopened = new ClientDownloadService(store, nextHttp, tasks, _ => source, _ => new RecommendationSource(bytes.Length), library, _ => game);
        if (scenario == "intent-mismatch") { Assert.Throws<IOException>(() => reopened.List()); return; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Assert.Single(reopened.List()).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal(scenario == "corrupt" ? "failed" : "completed", Assert.Single(reopened.List()).Stage);
        var selected = Assert.Single(store.Load("first").Plugins);
        Assert.Equal(scenario is "enabled" or "disabled" ? "2.0.0" : "1.0.0", selected.Version);
        Assert.Equal(scenario is "enabled" or "corrupt", selected.Enabled);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("source")]
    [InlineData("policy")]
    [InlineData("warning")]
    public void BundledLatestComponentsAcceptPolicyPreservingUpdatesAcrossSources(string scenario)
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Rules("fixture.optional", "recommend", "latest") }; var game = original with { StoreManagedPlugins = ["fixture.optional"], Builds = [build] };
        var policy = Manifest.Replace("kind = \"patch\"", "kind = \"patch\"\nupdate_source = \"fixture\"\nwarning = { en = \"Keep enabled\" }", StringComparison.Ordinal)
            .Replace("builds = [\"test-1\"]", "builds = [\"test-1\"]", StringComparison.Ordinal);
        var bundled = Path.Combine(game.Folder, "plugins", "component"); Directory.CreateDirectory(bundled); File.WriteAllText(Path.Combine(bundled, "plugin.toml"), policy);
        var next = policy.Replace("1.0.0", "2.0.0", StringComparison.Ordinal);
        if (scenario == "policy") next = next.Replace("[plugin]", "[plugin]\nrequire_latest = true", StringComparison.Ordinal);
        if (scenario == "warning") next = next.Replace("[compatibility]", "[compatibility]\nwarn_on = []", StringComparison.Ordinal);
        var folder = Folder(next); var firstRelease = StoreRelease(folder);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firstRelease.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var package = firstRelease.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        if (scenario is "valid" or "source")
        {
            var installed = library.InstallFromStore(folder, game, build, package, manifest, scenario == "source" ? "another" : "fixture", new string('a', 64));
            Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, installed.Id, "latest"));
            Assert.Equal("fixture", installed.UpdateSource);
            Assert.True(library.MatchesInstalledSnapshot(installed));
            Assert.Equal(2, library.Packages(game).Count);
            Assert.Throws<InvalidOperationException>(() => library.Prepare(folder, game, build));
            Assert.True(library.MatchesInstalledSnapshot(installed));
        }
        else Assert.Throws<DefinitionException>(() => library.InstallFromStore(folder, game, build, package, manifest, "fixture", new string('a', 64)));
    }

    [Fact]
    public void OlderStoreComponentRemainsReadableWhenBundledUpdateChangesFileRetentionAndAddsBuilds()
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Rules("fixture.optional", "recommend", "latest") }; var game = original with { StoreManagedPlugins = ["fixture.optional"], Builds = [build] };
        var policy = Manifest.Replace("kind = \"patch\"", "kind = \"files\"\nupdate_source = \"fixture\"\nwarning = { en = \"Keep enabled\" }", StringComparison.Ordinal);
        policy = policy[..policy.IndexOf("[[patches]]", StringComparison.Ordinal)];
        policy = policy.Replace("builds = [\"test-1\"]", "builds = [\"test-1\"]", StringComparison.Ordinal);
        var payload = "fixture"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        policy += $"\n[[files]]\nsource = \"payload.bin\"\ntarget = \"component.bin\"\nsize = {payload.Length}\nsha256 = \"{hash}\"\n";
        var bundled = Path.Combine(game.Folder, "plugins", "component"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), policy);
        File.WriteAllBytes(Path.Combine(bundled, "payload.bin"), payload);
        var source = Folder(policy); File.WriteAllBytes(Path.Combine(source, "payload.bin"), payload);
        var release = StoreRelease(source);
        var text = Encoding.UTF8.GetString(release.Manifest) + $"\n[[files]]\npath = \"payload.bin\"\nsource_path = \"payload.bin\"\nsize = {payload.Length}\nsha256 = \"{hash}\"\n";
        var manifest = Encoding.UTF8.GetBytes(text);
        var package = release.Package with { Size = release.Package.Size + payload.Length, ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var root = Path.Combine(_root, "library");
        using (var library = new PluginLibrary(root))
            library.InstallFromStore(source, game, build, package, manifest, "fixture", new string('a', 64));
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), policy.Replace("1.0.0", "1.0.1", StringComparison.Ordinal)
            .Replace("builds = [\"test-1\"]", "builds = [\"test-1\", \"test-2\"]", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(bundled, "file-install.toml"), "schema = 1\nrestore_files = false\n");
        using var reopened = new PluginLibrary(root);
        var packages = reopened.Packages(game);
        Assert.Equal(2, packages.Count);
        Assert.True(packages.Single(p => p.Version == "1.0.0").RestoreFiles);
        Assert.False(packages.Single(p => p.Version == "1.0.1").RestoreFiles);
        var nextBuild = build with { Id = "test-2", DefaultPlugins = ["fixture.optional"] };
        Assert.Equal("1.0.1", Assert.Single(PluginResolver.Defaults(game, nextBuild, packages)).Version);
        var existing = new InstanceDefinition("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]);
        Assert.Contains("plugin_latest_pending", Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(existing, build, packages)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlySupportedDefaultActivatesWithoutOverwritingExplicitDisable(bool explicitlyDisabled)
    {
        var original = Game();
        var game = original with { DefaultPlugins = ["fixture.optional"] };
        var build = game.Builds[0];
        var initial = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml")) with { Builds = ["other-build"] };
        Assert.Empty(PluginResolver.Defaults(game, build, [initial]));
        var instance = new InstanceDefinition("first", "First", game.Id, build.Id, null,
            explicitlyDisabled ? [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] : []);
        var next = initial with { Version = "1.1.0", Builds = [build.Id] };
        var updated = FilePluginResolver.EnsureComponents(instance, game, build, [initial, next]);
        var selection = Assert.Single(updated.Plugins);
        Assert.Equal(!explicitlyDisabled, selection.Enabled);
        Assert.Equal(explicitlyDisabled ? "1.0.0" : "1.1.0", selection.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewLauncherBundleReplacesAnOlderInstalledReleaseButRetainsActivation(bool enabled)
    {
        var original = Game();
        var game = original with { StoreManagedPlugins = ["fixture.optional"] };
        var source = Folder(); var release = StoreRelease(source);
        using var library = new PluginLibrary(Path.Combine(_root, "bundle-upgrade-library"));
        library.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64));
        var bundled = Path.Combine(game.Folder, "plugins", "optional"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), Manifest.Replace("1.0.0", "1.1.0", StringComparison.Ordinal));
        var instance = new InstanceDefinition("first", "First", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "1.0.0", enabled, false, new Dictionary<string, object>())]);
        var updated = FilePluginResolver.EnsureComponents(instance, game, game.Builds[0], library.Packages(game));
        var selection = Assert.Single(updated.Plugins);
        Assert.Equal("1.1.0", selection.Version);
        Assert.Equal(enabled, selection.Enabled);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("source")]
    [InlineData("policy")]
    public void StoreOnlyComponentsRetainRequiredPolicyAcrossSourcesWithoutBundledPayloads(string scenario)
    {
        var original = Game();
        var game = original with
        {
            StoreManagedPlugins = ["fixture.optional"],
            PluginSources = new Dictionary<string, string> { ["fixture.optional"] = "fixture" },
        };
        var text = Manifest.Replace("kind = \"patch\"", "kind = \"patch\"\nupdate_source = \"fixture\"", StringComparison.Ordinal);
        if (scenario == "policy") text = text.Replace("update_source = \"fixture\"", "update_source = \"other\"", StringComparison.Ordinal);
        var folder = Folder(text); var release = StoreRelease(folder);
        using var library = new PluginLibrary(Path.Combine(_root, "store-component-library"));
        Assert.Empty(library.Packages(game));
        if (scenario is "valid" or "source")
        {
            var installed = library.InstallFromStore(folder, game, game.Builds[0], release.Package, release.Manifest, scenario == "source" ? "other" : "fixture", new string('a', 64));
            Assert.Equal(installed.Id, Assert.Single(library.Packages(game)).Id);
        }
        else Assert.Throws<InvalidDataException>(() => library.InstallFromStore(folder, game, game.Builds[0], release.Package, release.Manifest,
            scenario == "source" ? "other" : "fixture", new string('a', 64)));
    }

    [Fact]
    public async Task OptionalBundledComponentsDiscoverIndependentStoreUpdates()
    {
        var original = Game(); var game = original with { StoreManagedPlugins = ["fixture.optional"] };
        var bundled = Path.Combine(game.Folder, "plugins", "optional"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), Manifest.Replace("kind = \"patch\"", "kind = \"patch\"\nupdate_source = \"fixture\"", StringComparison.Ordinal));
        using var library = new PluginLibrary(Path.Combine(_root, "independent-update-library"));
        Assert.False(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(game.Builds[0], Assert.Single(library.Packages(game)).Id, "latest"));
        var instances = new InstanceStore(Path.Combine(_root, "independent-update-instances"));
        await instances.CreateAsync(new("first", "First", game.Id, game.Builds[0].Id, null, []));
        var sources = new StoreSourceStore(Path.Combine(_root, "independent-update-sources"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false); sources.Save(source);
        var release = StoreRelease(Folder()).Package with { Version = "2.0.0" };
        var catalog = new StoreCatalog("fixture", new Dictionary<string, string> { ["en"] = "Fixture" }, [release]);
        using var http = new HttpClient();
        await using var updates = new PluginUpdateService(library, instances, sources, http, _ => throw new InvalidOperationException(), _ => game,
            _ => false, (_, _, _, _) => throw new InvalidOperationException(), readCatalog: (_, _) => Task.FromResult(catalog));
        var result = Assert.Single(await updates.CheckAsync("first"));
        Assert.Equal("available", result.Status);
        Assert.Equal("2.0.0", result.Version);
    }

    private sealed class RecommendationSource(long size) : IDownloadSource
    {
        public string Kind => "http";
        public Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new ResolvedDownload(new Uri("https://storage.example/plugin"), new Dictionary<string, string>(), size));
    }

    private sealed class RecommendationHandler(byte[] bytes, bool stalled = false) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (stalled) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
