using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BubbleFighterOverlayThemeTests
{
    [Theory]
    [InlineData("2017-02-06", "v1", "v1")]
    [InlineData("2017-02-06", "v2", "v2")]
    [InlineData("2017-02-07", "v1", "v3")]
    [InlineData("2017-02-07", "v2", "v3")]
    [InlineData("2026-05-08", "v2", "v3")]
    [InlineData(null, "v1", "v1")]
    public void ClientRevisionResolvesBeforeOverlayStartupAndRetainsBuildTargets(string? date, string theme, string expected)
    {
        var profile = new OverlayBuildProfile(theme, ["room.mode.extra"], [new("room.shell", "hook", null, null, null)]);
        var build = Build(date) with { Overlay = profile };
        var resolved = new BubbleFighterModule().GetOverlayProfile(build);
        Assert.Equal(expected, resolved.Theme);
        Assert.Same(profile.Capabilities, resolved.Capabilities);
        Assert.Same(profile.Targets, resolved.Targets);
        Assert.Equal(theme, build.Overlay.Theme);
    }

    [Theory]
    [InlineData("2011-01-01", "v1")]
    [InlineData("2015-04-22", "v2")]
    [InlineData("2017-02-07", "v3")]
    [InlineData(null, "v2")]
    public void MissingProfileUsesTheClientEraWithoutChangingEarlierExplicitChoices(string? date, string expected)
        => Assert.Equal(expected, new BubbleFighterModule().GetOverlayProfile(Build(date)).Theme);

    private static BuildDefinition Build(string? date) => new("fixture", "cn", 1,
        new(new Dictionary<string, string> { ["en"] = "Fixture" }), "game.exe", "", "", null, null, "none", [], [])
    {
        BuildDate = date is null ? null : DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
    };
}
