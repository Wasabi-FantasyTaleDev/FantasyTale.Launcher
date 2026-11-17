using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerRuleBuildReleaseTests
{
    private static StorePackage Package(string id, params string[] builds) =>
        new(id, "plugin", "bubblefighter", builds, "1.0.0", new Dictionary<string, string> { ["en"] = id },
            new Dictionary<string, string>(), "fixture", 1, "manifest.toml", new string('0', 64));

    [Fact]
    public void AServerRuleForEveryBuildIsOfferedOnlyWhereThePluginHasARelease()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var tw410 = game.Builds.Single(row => row.Id == "tw-410");
        var kr812 = game.Builds.Single(row => row.Id == "kr-812");
        IReadOnlyList<IReadOnlyList<StorePackage>> catalogs =
            [[Package("bubblefighter.fantasy.mid-match-exit", "kr-812", "tw-454"), Package("bubblefighter.fantasy.character-models")]];

        Assert.False(ClientPolicyActivation.HasReleaseFor(game.Id, tw410, "bubblefighter.fantasy.mid-match-exit", [], catalogs));
        Assert.True(ClientPolicyActivation.HasReleaseFor(game.Id, kr812, "bubblefighter.fantasy.mid-match-exit", [], catalogs));
        // A package that names no builds is made for every build.
        Assert.True(ClientPolicyActivation.HasReleaseFor(game.Id, tw410, "bubblefighter.fantasy.character-models", [], catalogs));
        Assert.False(ClientPolicyActivation.HasReleaseFor(game.Id, tw410, "bubblefighter.fantasy.unknown", [], catalogs));
        // Before any store catalog is cached nothing is known, so nothing is hidden.
        Assert.True(ClientPolicyActivation.HasReleaseFor(game.Id, tw410, "bubblefighter.fantasy.mid-match-exit", [], []));
    }
}
