using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;
using Xunit;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerSectionsTests
{
    private static ServerDirectoryTarget Node(string id, ServerDirectoryMaintenance? maintenance = null) =>
        new(id, id + ".example", 7140, "formal", "", true, false, 1, 100) { Maintenance = maintenance };

    private static ServerDirectory Grouped() => new(1, "kr-1256",
    [
        new(2, "Beijing", false, [Node("beijing", new(DateTimeOffset.UnixEpoch, null, "**Weekly** update"))]) { Section = "main" },
        new(5, "Oversea", false, [Node("oversea")]) { Section = "oversea" },
    ])
    { Sections = [new("main", "Main"), new("oversea", "Oversea Server", "https://oversea.example/")] };

    [Fact]
    public void GroupedDirectoriesAndMaintenanceAreAccepted() => LauncherApiClient.ValidateDirectory(Grouped(), "kr-1256");

    [Fact]
    public void SelectorsMustNameAKnownSectionWhenGrouped()
    {
        var directory = Grouped();
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(directory with
            { Servers = [directory.Servers[0] with { Section = "unknown" }] }, "kr-1256"));
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(directory with
            { Servers = [directory.Servers[0] with { Section = null }] }, "kr-1256"));
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(directory with { Sections = null }, "kr-1256"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user@oversea.example/")]
    [InlineData("relative/path")]
    public void SectionApisMustBePlainHttpOrigins(string api) =>
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(Grouped() with
            { Sections = [new("main", "Main"), new("oversea", "Oversea Server", api)] }, "kr-1256"));

    [Fact]
    public void SectionCacheKeepsDirectoriesSectionsAndTheActiveSectionAcrossRestarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "ft-sections-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ServerSectionCache(root);
            cache.Store("cluster", Grouped());
            cache.SetActiveSection("cluster", "oversea");
            var reopened = new ServerSectionCache(root);
            Assert.Equal(["main", "oversea"], reopened.Sections("cluster").Select(section => section.Id));
            Assert.Equal("https://oversea.example/", reopened.Sections("cluster")[1].ApiUrl);
            Assert.Equal("oversea", reopened.ActiveSection("cluster"));
            var directory = Assert.IsType<ServerDirectory>(reopened.Directory("cluster", "kr-1256"));
            Assert.Equal("**Weekly** update", directory.Servers[0].Targets[0].Maintenance?.Description);
            Assert.Null(reopened.Directory("cluster", "kr-812"));
            reopened.Forget("cluster");
            Assert.Empty(new ServerSectionCache(root).Sections("cluster"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SeenNoticesAreRememberedPerServerAndPrunedToPublishedOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ft-notices-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SeenNoticeStore(root);
            Assert.Equal(["a", "b"], store.Unseen("cluster", ["a", "b"]).Order());
            store.MarkSeen("cluster", ["a"]);
            var reopened = new SeenNoticeStore(root);
            Assert.Equal(["b"], reopened.Unseen("cluster", ["a", "b"]));
            Assert.Equal(["a", "b"], reopened.Unseen("other", ["a", "b"]).Order());
            Assert.Equal(["c"], reopened.Unseen("cluster", ["c"]));
            Assert.Equal(["a", "c"], new SeenNoticeStore(root).Unseen("cluster", ["a", "c"]).Order());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
