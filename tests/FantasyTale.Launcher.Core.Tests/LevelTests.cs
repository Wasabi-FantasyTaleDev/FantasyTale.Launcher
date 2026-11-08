using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LevelTests
{
    // Original level tables and rank artwork ship with release packages, not with the public source tree.
    private const string ReleaseAssetsSkip = "Requires the release level tables and rank artwork under games/*/levels.";
    private static readonly string[] ReleaseAssets =
    [
        "BubbleFighter/levels/rank.png", "AirRider/levels/default-001.png", "AirRider/levels/star-001.png",
        "TalesRunner/levels/essenlevelinfo.txt", "TalesRunner/levels/rankicon.png", "TalesRunner/levels/rankicon2.png",
    ];
    private static bool HasReleaseAssets() => ReleaseAssets.All(path => File.Exists(Path.Combine(Games(), path)));

    private sealed class ReleaseAssetsFactAttribute : FactAttribute
    {
        public ReleaseAssetsFactAttribute() { if (!HasReleaseAssets()) Skip = ReleaseAssetsSkip; }
    }

    private sealed class ReleaseAssetsTheoryAttribute : TheoryAttribute
    {
        public ReleaseAssetsTheoryAttribute() { if (!HasReleaseAssets()) Skip = ReleaseAssetsSkip; }
    }

    private static string Games()
    {
        var directory = TestRepository.StartDirectory();
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "games");
    }

    private static void WritePng(string path, int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        File.WriteAllBytes(path, bytes);
    }

    private static string SyntheticManifest(string folder, bool optional)
    {
        var manifest = Path.Combine(folder, "levels.toml");
        File.WriteAllText(manifest, (optional ? "schema = 1\noptional_assets = true\n" : "schema = 1\n") +
            "[source]\nfile = \"levels.csv\"\nlevel_column = 0\nexp_column = 1\n" +
            "[[atlases]]\nfile = \"rank.png\"\nfirst_level = 1\ncolumns = 2\ncell_width = 10\ncell_height = 10\n");
        return manifest;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void OptionalAssetsYieldNoCatalogWhenTableOrArtworkIsAbsent(bool table, bool image)
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-optional-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: true);
            if (table) File.WriteAllText(Path.Combine(temporary.FullName, "levels.csv"), "1,0\n2,10\n");
            if (image) WritePng(Path.Combine(temporary.FullName, "rank.png"), 20, 10);
            Assert.Null(LevelCatalog.LoadAvailable(manifest, temporary.FullName));
            Assert.Throws<FileNotFoundException>(() => LevelCatalog.Load(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public void OptionalAssetsStillLoadAndValidateFilesThatArePresent()
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-present-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: true);
            File.WriteAllText(Path.Combine(temporary.FullName, "levels.csv"), "1,0\n2,10\n");
            WritePng(Path.Combine(temporary.FullName, "rank.png"), 20, 10);
            var catalog = LevelCatalog.LoadAvailable(manifest, temporary.FullName)!;
            Assert.Equal([1, 2], catalog.Entries.Select(entry => entry.Level));
            Assert.Equal((10, 0), (catalog.Entries[1].Icons["default"].X, catalog.Entries[1].Icons["default"].Y));

            // Presence is not validity: a truncated image is still rejected with the manifest key.
            File.WriteAllBytes(Path.Combine(temporary.FullName, "rank.png"), [1, 2, 3]);
            Assert.Throws<DefinitionException>(() => LevelCatalog.LoadAvailable(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public void ManifestsWithoutTheOptInStillFailWhenAFileIsMissing()
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-required-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: false);
            File.WriteAllText(Path.Combine(temporary.FullName, "levels.csv"), "1,0\n2,10\n");
            Assert.ThrowsAny<IOException>(() => LevelCatalog.LoadAvailable(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Theory]
    [InlineData("schema = 1", "schema = 2")]
    [InlineData("schema = 1", "schema = 1\nmisspelled = true")]
    [InlineData("schema = 1", "schema = 1\ndisplay_offset = 2")]
    [InlineData("schema = 1", "schema = 1\nnames_pending = 1")]
    [InlineData("level_column = 0", "level_column = -1")]
    [InlineData("level_column = 0", "level_column = 0\nencoding = \"unknown\"")]
    [InlineData("levels.csv", "../missing.csv")]
    [InlineData("rank.png", "../missing.png")]
    [InlineData("columns = 2", "columns = 0")]
    [InlineData("first_level = 1", "first_level = 1\nlast_level = 0")]
    [InlineData("cell_width = 10", "cell_width = 10\nmisspelled = true")]
    public void MissingOptionalAssetsNeverBypassDeclarationValidation(string original, string invalid)
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-invalid-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: true);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(original, invalid, StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => LevelCatalog.LoadAvailable(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOptionalAssetDoesNotHideAnotherMalformedAsset(bool tablePresent)
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-partial-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: true);
            if (tablePresent) File.WriteAllText(Path.Combine(temporary.FullName, "levels.csv"), "invalid,experience\n");
            else File.WriteAllBytes(Path.Combine(temporary.FullName, "rank.png"), [1, 2, 3]);
            Assert.Throws<DefinitionException>(() => LevelCatalog.LoadAvailable(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public void OptionalAssetDirectoryIsNotTreatedAsAMissingFile()
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-directory-");
        try
        {
            var manifest = SyntheticManifest(temporary.FullName, optional: true);
            Directory.CreateDirectory(Path.Combine(temporary.FullName, "levels.csv"));
            Assert.Throws<UnauthorizedAccessException>(() => LevelCatalog.LoadAvailable(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public void BuiltInGamesLoadWithoutReleaseLevelAssets()
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-source-only-");
        try
        {
            foreach (var game in new[] { "AirRider", "BubbleFighter", "TalesRunner" })
            {
                var target = Directory.CreateDirectory(Path.Combine(temporary.FullName, game, "levels")).Parent!.FullName;
                File.Copy(Path.Combine(Games(), game, "game.toml"), Path.Combine(target, "game.toml"));
                File.Copy(Path.Combine(Games(), game, "levels/levels.toml"), Path.Combine(target, "levels/levels.toml"));
                var definition = GameCatalog.LoadGame(Path.Combine(target, "game.toml"));
                Assert.Null(definition.Levels);
            }
        }
        finally { temporary.Delete(true); }
    }

    [ReleaseAssetsFact]
    public void BuiltInTablesPinExperienceAndDoNotInventMissingLevels()
    {
        var games = GameCatalog.Load(Games());
        var bf = games.Single(g => g.Id == "bubblefighter").Levels!;
        Assert.Equal(240, bf.Entries.Count); Assert.True(bf.NamesPending);
        Assert.Null(bf.Entries[0].Name); Assert.Equal(5259518, bf.Entries[^1].MinimumExperience);
        Assert.Equal(1, LevelCatalog.Resolve(bf, null, 34)!.Level); Assert.Equal(2, LevelCatalog.Resolve(bf, null, 35)!.Level);
        Assert.Equal(1, LevelCatalog.Resolve(bf, 1, 5259518)!.Level); Assert.Null(LevelCatalog.Resolve(bf, 241, 5259518));
        Assert.Equal((92, 1222), (bf.Entries[^1].Icons["default"].X, bf.Entries[^1].Icons["default"].Y));
        var ar = games.Single(g => g.Id == "airrider").Levels!;
        Assert.Equal(30, ar.Entries.Count);
        Assert.Equal(1, LevelCatalog.Resolve(ar, null, 99)!.Level); Assert.Equal(2, LevelCatalog.Resolve(ar, null, 100)!.Level);
        Assert.Equal(116150, ar.Entries[^1].MinimumExperience);
        Assert.All(ar.Entries, entry => { Assert.Equal(2, entry.Icons.Count); Assert.All(entry.Icons.Values, icon => Assert.All(icon.Path, c => Assert.True(char.IsAscii(c)))); });
    }

    [ReleaseAssetsFact]
    public void RoleLevelsContinueAcrossSheetsAndSpecialRanksStayOutsideProgression()
    {
        var tr = GameCatalog.LoadGame(Path.Combine(Games(), "TalesRunner/game.toml")).Levels!;
        Assert.Equal(126, tr.Entries.Count);
        Assert.Equal(0, LevelCatalog.Resolve(tr, null, 24)!.Level); Assert.Equal(1, LevelCatalog.Resolve(tr, null, 25)!.Level);
        var lastFirstSheet = tr.Entries.Single(e => e.Level == 97).Icons["default"];
        var firstSecondSheet = tr.Entries.Single(e => e.Level == 98).Icons["default"];
        Assert.Equal("levels/rankicon.png", lastFirstSheet.Path); Assert.Equal((182, 210), (lastFirstSheet.X, lastFirstSheet.Y));
        Assert.Equal("levels/rankicon2.png", firstSecondSheet.Path); Assert.Equal((0, 0), (firstSecondSheet.X, firstSecondSheet.Y));
        Assert.Equal(52638337794L, tr.Entries[^1].MinimumExperience); Assert.Equal("보라 인페르노윙부츠", tr.Entries[^1].Name);
        Assert.Equal(10001, tr.Badges["unknown"].IconId); Assert.Equal(10000, tr.Badges["piero"].IconId);
        Assert.Equal((26, 231), (tr.Badges["piero"].Icon.X, tr.Badges["piero"].Icon.Y));
        Assert.Equal(125, LevelCatalog.Resolve(tr, null, long.MaxValue)!.Level);
        Assert.Null(LevelCatalog.Resolve(tr, 10000, 0));
    }

    [ReleaseAssetsFact]
    public void ReplacingOriginalTableAddsLevelsWithoutChangingCodeOrAtlasRanges()
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-data-");
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Games(), "TalesRunner/levels"))) File.Copy(file, Path.Combine(temporary.FullName, Path.GetFileName(file)));
            File.AppendAllText(Path.Combine(temporary.FullName, "essenlevelinfo.txt"), "\n1,126,60000000000,\"Future, \"\"rank\"\"\"\n2,999,1,\"Other type\"\n", Encoding.Unicode);
            var ranks = LevelCatalog.Load(Path.Combine(temporary.FullName, "levels.toml"), temporary.FullName);
            Assert.Equal(127, ranks.Entries.Count); Assert.Equal("Future, \"rank\"", ranks.Entries[^1].Name);
            Assert.Equal(60000000000L, ranks.Entries[^1].MinimumExperience);
            Assert.Equal((26, 63), (ranks.Entries[^1].Icons["default"].X, ranks.Entries[^1].Icons["default"].Y));
            Assert.Equal(10000, ranks.Badges["piero"].IconId);
            File.AppendAllText(Path.Combine(temporary.FullName, "essenlevelinfo.txt"), "1,126,70000000000,\"Duplicate\"\n", Encoding.Unicode);
            Assert.Throws<DefinitionException>(() => LevelCatalog.Load(Path.Combine(temporary.FullName, "levels.toml"), temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }

    [ReleaseAssetsTheory]
    [InlineData("file = \"rankicon2.png\"", "file = \"../rankicon2.png\"")]
    [InlineData("first_level = 98", "first_level = 97")]
    [InlineData("columns = 9", "columns = 200")]
    public void InvalidAssetsAndOverlappingRangesFailAtLoad(string before, string after)
    {
        var temporary = Directory.CreateTempSubdirectory("ft-level-invalid-");
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Games(), "TalesRunner/levels"))) File.Copy(file, Path.Combine(temporary.FullName, Path.GetFileName(file)));
            var manifest = Path.Combine(temporary.FullName, "levels.toml");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(before, after, StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => LevelCatalog.Load(manifest, temporary.FullName));
        }
        finally { temporary.Delete(true); }
    }
}
