using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerDescriptionTests
{
    [Fact]
    public void LocalDescriptionsRoundTripWithoutEnglishAndCanBeCleared()
    {
        var root = Directory.CreateTempSubdirectory("ft-server-description-").FullName;
        try
        {
            var game = new GameDefinition("fixture", GameStatus.Enabled, new(new Dictionary<string, string> { ["en"] = "Fixture" }), "fixture", "external", "", null, root, []);
            var server = new ServerDefinition("custom", game.Id, "Custom", null, null, false, false)
            {
                Presentation = new() { MotdTranslations = new Dictionary<string, string> { ["zh-CN"] = "Welcome \"friends\"\nSecond line" }, SummaryTranslations = new Dictionary<string, string> { ["ko"] = "Short introduction" } },
            };
            var store = new ServerStore(root); store.Save(server);
            var saved = Assert.Single(new ServerStore(root).List([game])).Presentation!;
            Assert.Equal(server.Presentation.MotdTranslations, saved.MotdTranslations);
            Assert.Equal(server.Presentation.SummaryTranslations, saved.SummaryTranslations);
            store.Save(server with { Presentation = null });
            Assert.Null(Assert.Single(store.List([game])).Presentation);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BundledDescriptionsComeFromDefinitionsAndSurviveConnectionOnlyOverrides()
    {
        var root = Directory.CreateTempSubdirectory("ft-server-description-default-").FullName;
        try
        {
            var games = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games"));
            var game = games.Single(game => game.Id == "bubblefighter");
            Assert.Equal("BF Online", game.Name.Resolve("vi"));
            var bundled = Assert.Single(ServerStore.Bundled([game]));
            Assert.Equal(["en", "zh"], bundled.Presentation!.MotdTranslations.Keys.Order(StringComparer.Ordinal));
            Assert.Contains("681849714", bundled.Presentation.MotdTranslations["zh"]);
            foreach (var language in new[] { "en", "ko", "id", "vi" })
            {
                var description = bundled.Presentation.MotdTranslations["en"];
                Assert.DoesNotContain("681849714", description);
                Assert.Contains("Wasabi", description);
            }
            var store = new ServerStore(root);
            var server = Assert.Single(store.List([game]));
            store.Save(server with { Name = "Local name", Presentation = null });
            var restored = Assert.Single(store.List([game]));
            Assert.Equal("Local name", restored.Name);
            Assert.Equal(bundled.Presentation.MotdTranslations, restored.Presentation!.MotdTranslations);
            Assert.Equal(bundled.Presentation.SummaryTranslations, restored.Presentation.SummaryTranslations);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StoreCatalogCarriesBothDescriptionMapsWithoutRequiringEnglish()
    {
        var catalog = StoreCatalogFile.Parse("source.toml", """
            schema=2
            [catalog]
            id="fixture"
            name={en="Fixture"}
            [[servers]]
            id="community"
            kind="server"
            game="fixture"
            name="Community"
            api_url="https://game.example/"
            builds=["kr-1"]
            [servers.presentation.motd_translations]
            zh-CN="Full introduction"
            [servers.presentation.summary_translations]
            ko="Short introduction"
            [servers.presentation.icon_url_translations]
            en="https://images.example/icon.png"
            [servers.presentation.images_translations]
            zh-CN=["https://images.example/screenshot.png"]
            """);
        var presentation = Assert.Single(catalog.Servers).Details.Presentation!;
        Assert.Equal("Full introduction", presentation.MotdTranslations["zh-CN"]);
        Assert.Equal("Short introduction", presentation.SummaryTranslations["ko"]);
        Assert.Equal("https://images.example/icon.png", presentation.IconUrlTranslations["en"]);
        Assert.Equal(["https://images.example/screenshot.png"], presentation.ImagesTranslations["zh-CN"]);
    }

    [Fact]
    public void ProfileNamesUseIdentityAndStillLoadAfterRenaming()
    {
        var root = Directory.CreateTempSubdirectory("ft-server-profile-name-").FullName;
        try
        {
            var game = new GameDefinition("fixture", GameStatus.Enabled, new(new Dictionary<string, string> { ["en"] = "Fixture" }), "fixture", "external", "", null, root, []);
            var first = new ServerDefinition("first", game.Id, "First", null, new("127.0.0.1", 80), false, false);
            var second = first with { Id = "second", Endpoint = new("127.0.0.1", 8080) };
            var store = new ServerStore(root); store.Save(first); store.Save(second);
            Assert.True(File.Exists(Path.Combine(root, "first.toml")));
            Assert.True(File.Exists(Path.Combine(root, "second.toml")));
            File.Move(Path.Combine(root, "first.toml"), Path.Combine(root, "Renamed.toml"));
            store.Save(first with { Name = "Changed" });
            Assert.Equal(2, store.List([game]).Count);
            Assert.Equal("Changed", store.List([game]).Single(server => server.Id == first.Id).Name);
            store.Delete(first);
            Assert.False(File.Exists(Path.Combine(root, "Renamed.toml")));
            Assert.Equal(second.Id, Assert.Single(store.List([game])).Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("bad_tag", "Description")]
    [InlineData("en", "Invalid\0description")]
    public void InvalidTranslationMetadataIsRejected(string language, string text)
    {
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ServerPresentation { MotdTranslations = new Dictionary<string, string> { [language] = text } }));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ServerPresentation { SummaryTranslations = new Dictionary<string, string> { [language] = text } }));
    }

    [Fact]
    public async Task PublicCatalogRetainsDescriptionsAndEnabledServerFeatures()
    {
        var presentation = new ServerPresentation { OnlinePlayers = 0, PeakOnlinePlayers = 10, RosterEnabled = true,
            MotdTranslations = new Dictionary<string, string> { ["ko"] = "Full introduction" }, SummaryTranslations = new Dictionary<string, string> { ["ko"] = "Short introduction" } };
        using var handler = new CatalogHandler(presentation);
        using var http = new HttpClient(handler); using var api = new LauncherApiClient(http);
        var catalog = await api.CatalogAsync(new("fixture", "fixture", "Fixture", "https://game.example/", null, false, false));
        Assert.Equal(presentation.MotdTranslations, catalog.Presentation!.MotdTranslations);
        Assert.Equal(presentation.SummaryTranslations, catalog.Presentation.SummaryTranslations);
        Assert.Equal(0, catalog.Presentation.OnlinePlayers);
        Assert.True(catalog.Presentation.RosterEnabled);
    }

    private sealed class CatalogHandler(ServerPresentation presentation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LauncherCatalog(1, "fixture", "Fixture", false, [], []) { Presentation = presentation }) });
    }
}
