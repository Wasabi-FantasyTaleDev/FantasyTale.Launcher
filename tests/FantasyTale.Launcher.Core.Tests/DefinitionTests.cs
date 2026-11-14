using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DefinitionTests
{
    [Theory]
    [InlineData("[]", true)]
    [InlineData("[\"zh-CN\"]", true)]
    [InlineData("[\"zh-CN\", \"zh-cn\"]", false)]
    [InlineData("[\"../invalid\"]", false)]
    [InlineData("[\"---\"]", false)]
    [InlineData("[\"zh-\"]", false)]
    public void GameLocaleVisibilityIsOptionalValidatedMetadata(string regions, bool valid)
    {
        var folder = Directory.CreateTempSubdirectory("ft-game-visibility-");
        try
        {
            var path = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(path, $"schema=1\n[game]\nid=\"fixture\"\nstatus=\"placeholder\"\nhook_host=\"external\"\nname={{en=\"Fixture\"}}\nunsupported_regions={regions}\n");
            if (valid) Assert.Equal(regions == "[]" ? 0 : 1, GameCatalog.LoadGame(path).UnsupportedRegions.Count);
            else Assert.Equal("game.unsupported_regions", Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(path)).Key);
        }
        finally { folder.Delete(true); }
    }

    private static string RepoGames()
    {
        var dir = TestRepository.StartDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "games");
    }

    [Fact]
    public void FontWeightRecommendationsBelongOnlyToDeclaredLocalizationBuilds()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        Assert.Equal(["id-1809", "id-400", "kr-1256", "kr-812"], game.Builds.Where(build => FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Offered(build, "zh-TW").Contains("bubblefighter.fantasy.font-support-cn")).Select(build => build.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(game.Builds, build => FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Offered(build, "ko").Contains("bubblefighter.fantasy.font-support-cn"));
        var storePlugins = PluginCatalog.Load(Path.Combine(Directory.GetParent(RepoGames())!.FullName, "store-plugins", "BubbleFighter"));
        var font = storePlugins.Single(p => p.Id == "bubblefighter.fantasy.font-support-cn");
        Assert.Equal("中文字体支持", font.Name.Resolve("zh-CN"));
        Assert.False((bool)Assert.Single(font.Options).Default);
        Assert.All(game.Builds, build => Assert.Equal(build.Id is "id-400" or "id-1809" or "kr-812" or "kr-1256", PluginCatalog.IsAvailable(font, build)));
        var auth = storePlugins.Single(p => p.Id == "bubblefighter.fantasy.login-fix-cn");
        Assert.True(game.Builds.Where(build => build.Branch == "cn").All(build => FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, auth.Id, "latest")));
        Assert.Equal("fantasy-tale", auth.UpdateSource);
        Assert.All(game.Builds, build => Assert.Equal(build.Branch == "cn", FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Offered(build).Contains(auth.Id)));
    }

    /// <summary>Available native fonts retain independent Korean payloads and absent originals use the explicit fallback.</summary>
    [Fact]
    public void NativeFontVariantsHaveIndependentPayloads()
    {
        var root = Directory.GetParent(RepoGames())!.FullName;
        var relative = Path.Combine("BubbleFighter", "bubblefighter.fantasy.font-support-cn", "plugin.toml");
        var manifest = Path.Combine(root, "store-plugins", relative);
        var package = PluginCatalog.LoadFile(manifest);
        var nativeNames = new[] { "AritaB", "AritaL", "AritaM", "AritaSB", "NexonBTBold", "NexonGothicB", "NexonGothicL", "NexonGothicM", "NanumGothic", "NanumGothicBold" };
        var nativeFiles = nativeNames.Select(name => Assert.Single(package.Files, file => file.Source == $"fonts/fused-{name}.ttf")).ToArray();
        Assert.Equal(nativeNames.Length, nativeFiles.Select(file => file.Sha256).Distinct(StringComparer.Ordinal).Count());
        foreach (var file in nativeFiles)
            Assert.Equal(Path.GetFileNameWithoutExtension(file.Source)["fused-".Length..], Path.GetFileNameWithoutExtension(file.Target));
        foreach (var name in new[] { "ngulim", "Malgun", "Ulrungdo", "Ydygo150", "Ydyg530", "Ydygo550" })
            Assert.Equal("fonts/chinese-korean.ttf", Assert.Single(package.Files, file => file.Target == $"Data/{name}.ttf" && file.ClientLanguages.Count == 0).Source);
    }

    /// <summary>Native font deployment is independent of the Chinese bold gate and restores the preceding file.</summary>
    [Theory]
    [InlineData("kr-812", "NexonBTBold.otf", false)]
    [InlineData("kr-812", "NexonBTBold.otf", true)]
    [InlineData("id-400", "NanumGothicBold.ttf", false)]
    [InlineData("id-400", "NanumGothicBold.ttf", true)]
    [InlineData("id-1809", "NanumGothicBold.ttf", false)]
    [InlineData("id-1809", "NanumGothicBold.ttf", true)]
    public void NativeLanguageFontsDeployAndRestoreRegardlessOfBold(string buildId, string nativeFont, bool bold)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == buildId);
        var manifest = Path.Combine(Directory.GetParent(RepoGames())!.FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.font-support-cn", "plugin.toml");
        var package = PluginCatalog.LoadFile(manifest);
        var folder = Directory.CreateTempSubdirectory("ft-native-font-");
        try
        {
            var replacement = "verified replacement font"u8.ToArray();
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(replacement));
            var payloadFolder = Directory.CreateDirectory(Path.Combine(folder.FullName, "package")).FullName;
            File.WriteAllBytes(Path.Combine(payloadFolder, "font.ttf"), replacement);
            package = package with
            {
                Folder = payloadFolder,
                Files = package.Files.Select(file => file with { Source = "font.ttf", Size = replacement.Length, Sha256 = hash }).ToArray()
            };
            var selection = new InstancePlugin(package.Id, package.Version, true, false, new Dictionary<string, object> { ["force_bold"] = bold });
            var instance = new InstanceDefinition("font", "Font", game.Id, build.Id, null, [selection]) { Language = build.NativeLanguage };
            var files = FilePluginResolver.Resolve(instance, build, [package]);
            // kr-812 sub_AE60D0 and the native Fonts_id.csv select these defaults instead of ngulim.ttf.
            Assert.Contains(files, file => file.Target == "Data/" + nativeFont);
            var client = Directory.CreateDirectory(Path.Combine(folder.FullName, "client", "Data")).Parent!.FullName;
            var original = "preceding native font"u8.ToArray();
            var target = Path.Combine(client, "Data", nativeFont);
            File.WriteAllBytes(target, original);
            _ = RuntimeDeployment.Apply(client, game.Id, null, files);
            Assert.Equal(replacement, File.ReadAllBytes(target));
            RuntimeDeployment.RecoverStoppedClient(client);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.False(File.Exists(Path.Combine(client, "Data", "ngulim.ttf")));
            Assert.Empty(FilePluginResolver.Resolve(instance with { Plugins = [selection with { Enabled = false }] }, build, [package]));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StoreOnlySelectionsRemainIntactUntilTheirPackageIsDownloaded(bool enabled)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "id-400");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
            [new("bubblefighter.fantasy.font-support-cn", "1.0.1", enabled, false, new Dictionary<string, object> { ["force_bold"] = false })]);
        var updated = FilePluginResolver.EnsureComponents(instance, game, build, packages);
        var selected = Assert.Single(updated.Plugins, plugin => plugin.Id == "bubblefighter.fantasy.font-support-cn");
        Assert.Equal("1.0.1", selected.Version);
        Assert.Equal(enabled, selected.Enabled);
        Assert.Equal(false, selected.Options["force_bold"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOptionalSelectionIsRepairedBeforeConsideringRemoval(bool completelyMissing)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "id-400");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        var storeRoot = Path.Combine(Directory.GetParent(Directory.GetParent(game.Folder)!.FullName)!.FullName, "store-plugins", "BubbleFighter");
        var autoDash = PluginCatalog.Load(storeRoot).Single(plugin => plugin.Id == "bubblefighter.fantasy.auto-dash");
        packages = packages.Append(autoDash).ToArray();
        if (completelyMissing) packages = packages.Where(package => package.Id != autoDash.Id).ToArray();
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
            [new(autoDash.Id, "0.0.0", true, false, new Dictionary<string, object>()),
             new("bubblefighter.fantasy.font-support-cn", "1.0.1", false, false, new Dictionary<string, object> { ["force_bold"] = false })]);

        var updated = FilePluginResolver.EnsureComponents(instance, game, build, packages, out var notices);

        Assert.DoesNotContain(updated.Plugins, plugin => plugin.Id == "bubblefighter.fantasy.font-support-cn");
        Assert.Contains(notices, notice => notice.PluginId == "bubblefighter.fantasy.font-support-cn" && notice.Version == "1.0.1");
        if (!completelyMissing)
        {
            Assert.Contains(updated.Plugins, plugin => plugin.Id == autoDash.Id && plugin.Version == autoDash.Version && plugin.Enabled);
            Assert.Single(notices);
            return;
        }
        Assert.DoesNotContain(updated.Plugins, plugin => plugin.Id == autoDash.Id);
        var notice = Assert.Single(notices, item => item.PluginId == autoDash.Id);
        Assert.Equal(instance.Id, notice.InstanceId);
        Assert.Equal(instance.Game, notice.Game);
        Assert.Equal(autoDash.Id, notice.PluginId);
        Assert.Equal("0.0.0", notice.Version);
        Assert.Equal("missing", notice.Reason);
        Assert.NotEqual(autoDash.Version, notice.Version);
    }

    [Fact]
    public void RetiredBundledSelectionUsesTheInstalledVersionAndCurrentPolicy()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "id-400");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
            [new("bubblefighter.fantasy.overlay", "0.0.0", true, true, new Dictionary<string, object>())]);

        var updated = FilePluginResolver.EnsureComponents(instance, game, build, packages, out var notices);

        Assert.Contains(updated.Plugins, plugin => plugin.Id == "bubblefighter.fantasy.overlay" && plugin.Version == "0.2.0" && plugin.Enabled && !plugin.Required);
        Assert.Empty(notices);
    }

    [Fact]
    public void PluginAliasesMigrateSelectionsAndPreserveState()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "id-400");
        var storeRoot = Path.Combine(Directory.GetParent(Directory.GetParent(game.Folder)!.FullName)!.FullName, "store-plugins", "BubbleFighter");
        var package = PluginCatalog.Load(storeRoot).Single(plugin => plugin.Id == "bubblefighter.fantasy.auto-dash") with
        {
            Aliases = ["bubblefighter.fantasy.legacy-dash"]
        };
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
            [new("bubblefighter.fantasy.legacy-dash", package.Version, false, true,
                new Dictionary<string, object> { ["auto_dash"] = false })]);

        var updated = FilePluginResolver.EnsureComponents(instance, game, build, [package], out var notices);

        var selection = Assert.Single(updated.Plugins);
        Assert.Equal(package.Id, selection.Id);
        Assert.Equal(package.Version, selection.Version);
        Assert.False(selection.Enabled);
        Assert.False(selection.Required);
        Assert.Equal(false, selection.Options["auto_dash"]);
        Assert.Empty(notices);
    }

    [Fact]
    public void PluginAliasConflictsAreRejected()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var storeRoot = Path.Combine(Directory.GetParent(Directory.GetParent(game.Folder)!.FullName)!.FullName, "store-plugins", "BubbleFighter");
        var first = PluginCatalog.Load(storeRoot).Single(plugin => plugin.Id == "bubblefighter.fantasy.auto-dash") with { Aliases = ["bubblefighter.fantasy.legacy"] };
        var second = PluginCatalog.Load(storeRoot).Where(plugin => plugin.Id == "bubblefighter.fantasy.font-support-cn").OrderBy(plugin => plugin.Version, StringComparer.Ordinal).First() with { Aliases = ["bubblefighter.fantasy.legacy"] };

        Assert.Throws<DefinitionException>(() => PluginCatalog.ValidateIdentityConflicts([first, second]));
    }

    [Fact]
    public void PluginManifestLoadsFormerIdentifiers()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var source = Path.Combine(Directory.GetParent(Directory.GetParent(game.Folder)!.FullName)!.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.auto-dash", "plugin.toml");
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var text = File.ReadAllText(source).Replace("id = \"bubblefighter.fantasy.auto-dash\"\n", "id = \"bubblefighter.fantasy.auto-dash\"\naliases = [\"bubblefighter.fantasy.legacy-dash\"]\n", StringComparison.Ordinal);
            var file = Path.Combine(folder.FullName, "plugin.toml");
            File.WriteAllText(file, text);
            var plugin = PluginCatalog.LoadFile(file);
            Assert.Equal(["bubblefighter.fantasy.legacy-dash"], plugin.Aliases);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void RepositoryGamesLoad()
    {
        var games = GameCatalog.Load(RepoGames());
        Assert.Equal(["airrider", "bubblefighter", "talesrunner"], games.Select(g => g.Id));
        var bf = games.Single(g => g.Id == "bubblefighter");
        Assert.Equal(["home", "instances", "multiplayer", "hosting", "store", "downloads", "plugins", "accounts", "settings"], bf.NavigationEntries);
        Assert.All(games.Where(g => g.Id != "bubblefighter"), g => Assert.Equal(["home", "downloads", "accounts"], g.NavigationEntries));
        Assert.Equal(GameStatus.Enabled, bf.Status);
        Assert.False(bf.RoomsEnabled);
        Assert.True(bf.LocalHostingEnabled);
        Assert.Equal(["cn-404", "cn-408", "cn-735"], bf.Builds.Where(build => !build.Enabled || build.Hidden).Select(build => build.Id));
        Assert.All(bf.Builds.Where(build => build.Id == "cn-735"), build => { Assert.False(build.Enabled); Assert.True(build.Hidden); });
        Assert.Equal("泡泡战士", bf.Name.Resolve("zh-CN"));
        Assert.Equal("Crazy Shooter", bf.Name.Resolve("id-ID"));
        Assert.Contains(bf.Builds, b => b.Id == "id-400" && b.PeTimestamp == 0x52E07057u && b.Executable == "CrazyShooter.exe");
        var cn2011 = bf.Builds.Single(b => b.Id == "cn-279");
        Assert.Equal(new DateTime(2011, 4, 8, 18, 35, 38), cn2011.BuildDate);
        Assert.Equal(new DateTimeOffset(2011, 4, 8, 18, 35, 38, TimeSpan.FromHours(9)), cn2011.ReleaseDate);
        Assert.Equal("国服 2011-04-08", cn2011.Name.Resolve("zh-CN"));
        Assert.Contains("BubbleFighterU.exe", cn2011.ExecutableAliases);
        Assert.All(games.Where(g => g.Id != "bubblefighter"), g => Assert.Equal(GameStatus.Placeholder, g.Status));
        Assert.Equal(["bubblefighter", "airrider", "talesrunner"], games.Where(g => g.Status == GameStatus.Enabled || g.HomeOrder.HasValue)
            .OrderBy(g => g.HomeOrder ?? 0).Select(g => g.Id));
        var airRider = games.Single(g => g.Id == "airrider");
        Assert.Equal("跑跑飞空艇", airRider.Name.Resolve("zh-CN"));
        Assert.Equal("에어라이더", airRider.Name.Resolve("ko"));
        Assert.Equal("Air Rider", airRider.Name.Resolve("en"));
        Assert.Empty(airRider.Module);
        Assert.Empty(airRider.Builds);
        Assert.False(airRider.Preparing);
        Assert.Equal("Air Rider Fantasy", Assert.Single(FantasyTale.Launcher.Core.Servers.ServerStore.Bundled([airRider])).Name);
        Assert.Single(airRider.BackgroundVideos);
        var runner = games.Single(g => g.Id == "talesrunner");
        Assert.True(runner.Preparing);
        Assert.Equal("超级跑跑", runner.Name.Resolve("zh-CN"));
        Assert.Equal("跑Online", runner.Name.Resolve("zh-TW"));
        Assert.Equal("Fantasy Runner 目前不在中国大陆地区提供服务。", runner.ServiceNotice!.Resolve("zh-CN"));
        Assert.Empty(runner.BackgroundVideos);
    }

    [Theory]
    [InlineData("kr-1256", "2026-05-08")]
    [InlineData("cn-717", "2015-06-18")]
    [InlineData("cn-404", "2014-04-03")]
    [InlineData("cn-735", "2016-06-20")]
    public void ExternalSignedEvidenceAttachesToTheKnownWireBuild(string id, string date)
    {
        var build = GameCatalog.Load(RepoGames()).Single(game => game.Id == "bubblefighter").Builds.Single(build => build.Id == id);
        Assert.NotNull(build.SignerCertificateSha256);
        Assert.NotNull(build.ReleaseDate);
        Assert.Equal(TimeSpan.FromHours(9), build.ReleaseDate.Value.Offset);
        Assert.Equal(date, build.ReleaseDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(date, build.Name.Resolve("en"), StringComparison.Ordinal);
        Assert.Contains("BubbleFighterU.exe", build.ExecutableAliases);
    }

    [Theory]
    [InlineData("[\"home\", \"unknown\"]")]
    [InlineData("[\"home\", \"home\"]")]
    [InlineData("[\"downloads\"]")]
    [InlineData("[]")]
    public void InvalidNavigationDeclarationsNameTheConfigurationKey(string entries)
    {
        var folder = Directory.CreateTempSubdirectory("ft-navigation-");
        try
        {
            var file = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(file, $"schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = {{ en = \"X\" }}\nnavigation_entries = {entries}\n");
            Assert.Equal("game.navigation_entries", Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(file)).Key);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void OlderGameDefinitionsExposeOnlySharedMenus()
    {
        var folder = Directory.CreateTempSubdirectory("ft-navigation-legacy-");
        try
        {
            var file = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(file, "schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = { en = \"X\" }\n");
            Assert.Equal(["home", "downloads", "accounts"], GameCatalog.LoadGame(file).NavigationEntries);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("cn-406", "0.2.35.001", 0x51CD5DE5u, 7230)]
    [InlineData("cn-404", "0.2.46.001", 0x533CFD5Fu, 7240)]
    [InlineData("cn-774", "0.2.98.3", 0x5CEF8FF9u, 7190)]
    public void ChinaLoginBuildsDeployTheLoginBridgeAlongsideTheMessengerStandIn(string id, string version, uint timestamp, int port)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == id);
        Assert.Equal(version, build.ReleaseVersion);
        Assert.Equal(timestamp, build.PeTimestamp);
        Assert.Equal(port, build.DefaultPort);
        Assert.Equal("required", build.LauncherLogin);
        var context = new LaunchContext(game, build, @"C:\games\client", new("203.0.113.10", port), "player");
        var module = new BubbleFighterModule();
        var plan = module.CreateLaunchPlan(context);
        Assert.Equal(["--ip=203.0.113.10", $"--port={port}", "--areaid=6_1", "--groupid=1"], plan.Arguments);
        Assert.Equal("--port=7400", module.CreateLaunchPlan(context with { Server = new("203.0.113.10", 7400) }).Arguments[1]);
        Assert.Equal([new RuntimeFileBinding("nmcogame.dll", "nmcogame.dll"),
            new RuntimeFileBinding("login_bridge.dll", "sdologinentry.dll")], plan.Runtime!.Files);
        Assert.Throws<InvalidOperationException>(() => module.CreateLaunchPlan(context with { Build = build with { LoginModule = null } }));
        Assert.Throws<InvalidOperationException>(() => module.CreateLaunchPlan(context with { Build = build with { LoginModule = "nmcogame.dll" } }));
        Assert.Throws<InvalidOperationException>(() => module.CreateLaunchPlan(context with { Build = build with { Login = "nmco" } }));
    }

    [Fact]
    public void TaiwanModernBuildUsesTheTicketAwareMessengerWithoutAnOriginalForwardingDependency()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "tw-454");
        Assert.Equal("nmco", build.Login);
        Assert.Equal("standin", build.Messenger);
        Assert.Equal("required", build.LauncherLogin);
        Assert.Equal(7200, build.DefaultPort);
        var plan = new BubbleFighterModule().CreateLaunchPlan(new(game, build, @"C:\games\client", new("203.0.113.10", 7200), "player"));
        Assert.Equal(["player", "/sid=0"], plan.Arguments);
        Assert.Equal(new RuntimeFileBinding("nmcogame.dll", "nmcogame.dll"), Assert.Single(plan.Runtime!.Files));
    }

    [Fact]
    public void KoreanHookBuildNamesItsExplicitAuthenticationOwner()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "kr-1256");
        var plan = new BubbleFighterModule().CreateLaunchPlan(new(game, build, @"C:\games\kr", new("203.0.113.10", 7100), "player"));
        Assert.Equal("hook", build.Login);
        Assert.Equal("bubblefighter.fantasy.login-fix-kr", plan.AuthenticationPlugin);
    }

    [Theory]
    [InlineData("../login.dll")]
    [InlineData("login.exe")]
    [InlineData(".dll")]
    [InlineData("login.dll:payload")]
    public void LoginModuleCannotEscapeItsRuntimeDestination(string target)
    {
        var folder = Directory.CreateTempSubdirectory("ft-login-definition-").FullName;
        try
        {
            var source = File.ReadAllText(Path.Combine(RepoGames(), "BubbleFighter", "builds", "cn-406.toml"));
            var file = Path.Combine(folder, "cn-406.toml");
            File.WriteAllText(file, source.Replace("sdologinentry.dll", target, StringComparison.Ordinal));
            Assert.Equal("launch.login_module", Assert.Throws<DefinitionException>(() => GameCatalog.LoadBuild(file, "bubblefighter")).Key);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("background_video = \"media/first.mp4\"", 1)]
    [InlineData("background_videos = [\"media/first.mp4\", \"media/second.webm\"]", 2)]
    [InlineData("background_videos = []", 0)]
    public void VideoPlaylistsPreserveOrderAndLegacyFirstVideo(string media, int count)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(file, $"schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = {{ en = \"X\" }}\n[game.media]\n{media}\n");
            var game = GameCatalog.LoadGame(file);
            Assert.Equal(count, game.BackgroundVideos.Count);
            Assert.Equal(game.BackgroundVideos.Count == 0 ? null : game.BackgroundVideos[0], game.BackgroundVideo);
            if (count > 0) Assert.Equal("first.mp4", Path.GetFileName(game.BackgroundVideos[0]));
            if (count > 1) Assert.Equal("second.webm", Path.GetFileName(game.BackgroundVideos[1]));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("background_videos = [\"../outside.mp4\"]")]
    [InlineData("background_videos = [\"https://example.com/video.mp4\"]")]
    [InlineData("background_videos = [\"\"]")]
    [InlineData("background_videos = [\"media/image.png\"]")]
    [InlineData("background_video = \"media/first.mp4\"\nbackground_videos = []")]
    [InlineData("background_videos = [1]")]
    public void InvalidVideoPlaylistsAreRejected(string media)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(file, $"schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = {{ en = \"X\" }}\n[game.media]\n{media}\n");
            Assert.Equal("game.media.background_videos", Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(file)).Key);
            File.WriteAllText(file, File.ReadAllText(file).Split("[game.media]")[0] + "[game.media]\nbackground_videos = [" + string.Join(",", Enumerable.Repeat("\"media/video.mp4\"", 17)) + "]");
            Assert.Equal("game.media.background_videos", Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(file)).Key);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("10001")]
    [InlineData("\"first\"")]
    public void InvalidHomeOrderIsRejected(string order)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(folder.FullName, "game.toml");
            File.WriteAllText(file, $"schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = {{ en = \"X\" }}\nhome_order = {order}\n");
            var error = Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(file));
            Assert.Equal("game.home_order", error.Key);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void UnknownKeysAreRejected()
    {
        var folder = Directory.CreateTempSubdirectory();
        var file = Path.Combine(folder.FullName, "game.toml");
        File.WriteAllText(file, "schema = 1\n[game]\nid = \"x\"\nstatus = \"placeholder\"\nhook_host = \"external\"\nname = { en = \"X\" }\ntypo = 1\n");
        var error = Assert.Throws<DefinitionException>(() => GameCatalog.LoadGame(file));
        Assert.Equal("game.typo", error.Key);
        folder.Delete(true);
    }

    [Fact]
    public void LocalizedTextFallsBack()
    {
        var text = new LocalizedText(new Dictionary<string, string> { ["en"] = "A", ["zh"] = "B", ["zh-TW"] = "C" });
        Assert.Equal("C", text.Resolve("zh-TW"));
        Assert.Equal("B", text.Resolve("zh-CN"));
        Assert.Equal("A", text.Resolve("ko"));
        Assert.Throws<ArgumentException>(() => new LocalizedText(new Dictionary<string, string> { ["ko"] = "x" }));
    }

    [Fact]
    public void SessionRendersTheContract()
    {
        var build = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter").Builds.Single(b => b.Id == "cn-324");
        var spec = new SessionSpec("bubblefighter", build, "cn-324-main", @"C:\logs\fthook.log", "pl\"ayer", null,
            new ServerEndpoint("203.0.113.10", 7140),
            [new SessionPluginSpec("bubblefighter.fantasy.font-support-cn", "1.0.0", @"C:\plugins\fw", true, new Dictionary<string, object> { ["n"] = 5L, ["on"] = true })]);
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(spec))!;
        var session = (TomlTable)parsed["session"];
        Assert.Equal(1L, parsed["schema"]);
        Assert.Equal("cn-324", session["build"]);
        Assert.Equal("cn", session["branch"]);
        Assert.Equal("standin", session["messenger"]);
        Assert.Equal(@"C:\logs\fthook.log", session["log"]);
        Assert.Equal((long)0x505DD60C, ((TomlTable)session["fingerprint"])["pe_timestamp"]);
        Assert.Equal("pl\"ayer", ((TomlTable)parsed["login"])["account"]);
        var plugin = (TomlTable)((TomlTableArray)parsed["plugins"])[0];
        Assert.Equal(true, plugin["required"]);
        Assert.Equal(5L, ((TomlTable)plugin["options"])["n"]);
    }

    [Fact]
    public void BubbleFighterExpandsLaunchArguments()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var module = new BubbleFighterModule();
        var cn = module.CreateLaunchPlan(new LaunchContext(game, game.Builds.Single(b => b.Id == "cn-324"), @"C:\games\cn324",
            new ServerEndpoint("203.0.113.10", 7140), null));
        Assert.Equal(["--ip=203.0.113.10", "--port=7140", "--areaid=6_1", "--groupid=1"], cn.Arguments);
        Assert.Equal(@"C:\games\cn324\BubbleFighter.exe", cn.ExecutablePath);
        var id = module.CreateLaunchPlan(new LaunchContext(game, game.Builds.Single(b => b.Id == "id-400"), @"C:\games\id400", null, "player"));
        Assert.Equal(["/sid=0"], id.Arguments);
    }

    [Fact]
    public void BubbleFighterUsesGameEndpointWithoutReplacingAuthenticationEndpoint()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "cn-324");
        var authenticationEndpoint = new ServerEndpoint("203.0.113.10", 7140);
        var gameEndpoint = new ServerEndpoint("127.0.0.1", 49152);
        var context = new LaunchContext(game, build, @"C:\games\cn324", authenticationEndpoint, "player")
        { GameEndpoint = gameEndpoint };

        var plan = new BubbleFighterModule().CreateLaunchPlan(context);
        var spec = new SessionSpec(game.Id, build, "test", "", "player", "ticket", context.Server, []);

        Assert.Equal(["--ip=127.0.0.1", "--port=49152", "--areaid=6_1", "--groupid=1"], plan.Arguments);
        Assert.Contains("host = \"203.0.113.10\"", SessionWriter.Render(spec));
        Assert.DoesNotContain("49152", SessionWriter.Render(spec));
    }

    [Theory]
    [InlineData("cn-706", 7220)]
    [InlineData("cn-717", 7150)]
    [InlineData("cn-735", 7160)]
    public void ChineseSdoBuildsResolveOrderedEndpointsAndDeployExactlyOneLoginBridge(string buildId, int? defaultPort)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == buildId);
        var module = new BubbleFighterModule();
        Assert.Equal("gamewidget-ticket", build.Login);
        Assert.Equal("required", build.LauncherLogin);
        Assert.Equal(defaultPort, build.DefaultPort);
        var context = new LaunchContext(game, build, @"C:\games\client", new ServerEndpoint("203.0.113.10", 17001), "player");
        var plan = module.CreateLaunchPlan(context);
        Assert.Equal(["--ip=203.0.113.10", "--port=17001", "--areaid=6_1", "--groupid=1"], plan.Arguments);
        Assert.Contains(plan.Runtime!.Files, file => file.Payload == "login_bridge.dll" && file.Target == "sdologinentry.dll");
        Assert.Contains(plan.Runtime.Files, file => file.Payload == "nmcogame.dll"
            && file.OriginalAlias == (build.Messenger == "forward" ? "nmcogame.nexon.dll" : null));
        var componentPlan = module.CreateLaunchPlan(context with { FilePluginTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sdologinentry.dll" } });
        Assert.DoesNotContain(componentPlan.Runtime!.Files, file => file.Target == "sdologinentry.dll");
        Assert.Throws<InvalidOperationException>(() => module.CreateLaunchPlan(context with { Server = null }));
    }

    [Fact]
    public void Chinese2012KeepsGameWidgetTicketLoginSeparateFromMessenger()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "cn-324");
        Assert.Equal("gamewidget-ticket", build.Login);
        Assert.Equal("required", build.LauncherLogin);
        Assert.Equal("GameWidget.dll", build.LoginModule);
        var module = new BubbleFighterModule();
        var context = new LaunchContext(game, build, @"C:\games\client", new ServerEndpoint("127.0.0.1", 7140), "player");
        var plan = module.CreateLaunchPlan(context);
        Assert.Single(plan.Runtime!.Files, file => file.Payload == "login_bridge.dll" && file.Target == "GameWidget.dll");
        Assert.Contains(plan.Runtime.Files, file => file.Payload == "nmcogame.dll" && file.OriginalAlias is null);
        var componentPlan = module.CreateLaunchPlan(context with { FilePluginTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GameWidget.dll" } });
        Assert.DoesNotContain(componentPlan.Runtime!.Files, file => file.Target == "GameWidget.dll");
    }

    [Fact]
    public void EveryEnabledBuildHasAConfiguredLaunchPath()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        foreach (var build in game.Builds.Where(build => build.Enabled))
        {
            Assert.NotEqual("unverified", build.Login);
            var plan = new BubbleFighterModule().CreateLaunchPlan(new LaunchContext(game, build, @"C:\games\client", new ServerEndpoint("203.0.113.10", 17001), "player"));
            Assert.NotNull(plan.Runtime);
            Assert.DoesNotContain(plan.Arguments, argument => argument.Contains('{') || argument.Contains('}'));
        }
    }

    [Fact]
    public void EveryNativeControlAdapterHasAConfiguredLaunchPath()
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var descriptors = Directory.GetFiles(Path.Combine(game.Folder, "plugins", "bubblefighter.fantasy.native-ui-bridge", "native-ui"), "*.toml");
        foreach (var descriptor in descriptors)
        {
            var build = game.Builds.Single(b => b.Id == Path.GetFileNameWithoutExtension(descriptor));
            Assert.NotEqual("unverified", build.Login);
            var plan = new BubbleFighterModule().CreateLaunchPlan(new LaunchContext(game, build, @"C:\games\client", new ServerEndpoint("203.0.113.10", 17001), "player"));
            Assert.NotNull(plan.Runtime);
        }
    }

    [Theory]
    [InlineData("id-400")]
    [InlineData("id-1809")]
    public void IndonesianClientsStartWithoutALauncherAccount(string buildId)
    {
        var game = GameCatalog.Load(RepoGames()).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == buildId);
        Assert.Equal("optional", build.LauncherLogin);
        Assert.DoesNotContain(build.Arguments, argument => argument.Contains("{account}", StringComparison.Ordinal));
        var plan = new BubbleFighterModule().CreateLaunchPlan(new LaunchContext(game, build, @"C:\games\indonesia", null, null));
        Assert.Equal(["/sid=0"], plan.Arguments);
    }
}
