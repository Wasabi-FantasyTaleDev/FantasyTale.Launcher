using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerProfilePublicationTests
{
    private static readonly ServerDefinition Profile = new("Community_Server", "fixture", "Full name",
        "https://game.example/", new("game.example", 10101), true, false)
    {
        Presentation = new() { Motd = "Full introduction", Summary = "Short introduction" },
    };
    private static readonly DirectoryEntry Entry = new("Community_Server", "server",
        new("fixture", "Catalog name", "https://game.example/", ["kr-1"], "Catalog introduction")
        { Endpoint = new("game.example", 10101) }, null);

    [Fact]
    public void PublicationUsesPortableIdentityAndRetainsFullPresentationWithAdvertisedBuildFallback()
    {
        var accepted = StoreServerDiscovery.ValidateProfile(Profile, Entry);
        Assert.Equal("Community_Server", accepted.Id);
        Assert.Equal("Full name", accepted.Name);
        Assert.Equal("Full introduction", accepted.Presentation!.Motd);
        Assert.Equal("Short introduction", accepted.Presentation.Summary);
        Assert.Equal(["kr-1"], accepted.Builds);
        Assert.Empty(Profile.Builds);
        Assert.Equal(["kr-2"], StoreServerDiscovery.ValidateProfile(Profile with { Builds = ["kr-2"] }, Entry).Builds);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("game")]
    [InlineData("api")]
    [InlineData("missing-api")]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("missing-endpoint")]
    public void PublicationRejectsConnectionsOrIdentityThatDifferFromTheAdvertisedServer(string field)
    {
        var changed = field switch
        {
            "identity" => Profile with { Id = "Other" },
            "game" => Profile with { Game = "other" },
            "api" => Profile with { ApiUrl = "https://other.example/" },
            "missing-api" => Profile with { ApiUrl = null },
            "host" => Profile with { Endpoint = new("other.example", 10101) },
            "port" => Profile with { Endpoint = new("game.example", 10102) },
            _ => Profile with { Endpoint = null },
        };
        Assert.Throws<InvalidDataException>(() => StoreServerDiscovery.ValidateProfile(changed, Entry));
    }

    [Fact]
    public void ApiComparisonAcceptsEquivalentDefaultPortsHostCaseAndTrailingSlashes()
    {
        Assert.Equal(Profile.Id, StoreServerDiscovery.ValidateProfile(Profile with { ApiUrl = "https://GAME.example:443" }, Entry).Id);
    }

    [Fact]
    public void PublicationRejectsRoomsLocalArtworkAndBundledAuthority()
    {
        Assert.Throws<ArgumentException>(() => StoreServerDiscovery.ValidateProfile(Profile, Entry with { Kind = "room" }));
        Assert.Throws<ArgumentException>(() => StoreServerDiscovery.ValidateProfile(Profile with { BuiltIn = true }, Entry));
        Assert.Throws<ArgumentException>(() => StoreServerDiscovery.ValidateProfile(Profile with { IconAsset = "assets/icon.png" }, Entry));
        Assert.Throws<ArgumentException>(() => StoreServerDiscovery.ValidateProfile(Profile with
        { Presentation = new() { IconUrl = "file:///D:/private.png" } }, Entry));
        Assert.Throws<ArgumentException>(() => StoreServerDiscovery.ValidateProfile(Profile with
        { Presentation = new() { ImagesTranslations = new Dictionary<string, IReadOnlyList<string>> { ["en"] = ["file:///D:/private.png"] } } }, Entry));
    }
}
