using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ReleaseClientPolicyTests
{
    [Theory]
    [InlineData("cn-279", "login-fix-cn", "mid-match-exit,pirate-expression-fix", "auto-dash,scene-styles,character-models")]
    [InlineData("cn-324", "login-fix-cn", "mid-match-exit,pirate-expression-fix", "auto-dash,scene-styles,character-models")]
    [InlineData("cn-406", "login-fix-cn", "mid-match-exit,pirate-expression-fix,resolution-extension", "auto-dash,scene-styles,character-models")]
    [InlineData("cn-404", "login-fix-cn", "mid-match-exit,pirate-expression-fix,resolution-extension", "auto-dash,character-models")]
    [InlineData("cn-408", "login-fix-cn", "mid-match-exit,pirate-expression-fix,resolution-extension", "auto-dash,character-models")]
    [InlineData("cn-706", "login-fix-cn", "mid-match-exit,pirate-expression-fix,resolution-extension", "auto-dash,character-models")]
    [InlineData("cn-717", "login-fix-cn", "mid-match-exit,pirate-expression-fix,resolution-extension", "auto-dash,character-models")]
    [InlineData("cn-774", "login-fix-cn", "mid-match-exit,pirate-expression-fix", "auto-dash,character-models")]
    [InlineData("kr-812", "", "mid-match-exit,font-support-cn,resolution-extension", "auto-dash,character-models")]
    [InlineData("id-400", "", "mid-match-exit,font-support-cn,resolution-extension", "auto-dash,character-models")]
    [InlineData("id-1809", "", "mid-match-exit,font-support-cn,resolution-extension", "auto-dash,character-models")]
    [InlineData("tw-454", "", "mid-match-exit", "auto-dash,character-models")]
    [InlineData("kr-1256", "login-fix-kr,shop-unlock", "mid-match-exit,font-support-cn", "character-models")]
    public void ProfilesMatchApprovedRequiredAndInitialRecommendationMatrix(string buildId, string required, string selected, string uncheckedItems)
    {
        var root = TestRepository.StartDirectory();
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var build = GameCatalog.LoadBuild(Path.Combine(root!.FullName, "games/BubbleFighter/builds", buildId + ".toml"), "bubblefighter");
        // The approved matrix is for a Chinese launcher; the Chinese font is recommended to Chinese launchers only.
        var rules = ProfileClientPolicy.Rules(build, "zh-CN");
        Assert.DoesNotContain(ProfileClientPolicy.Rules(build, "en"), rule => rule.Id == "bubblefighter.fantasy.font-support-cn");
        static string[] Ids(string values) => values.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => "bubblefighter.fantasy." + value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Ids(required), rules.Where(rule => rule.Required).Select(rule => rule.Id).Order(StringComparer.Ordinal));
        Assert.Equal(Ids(selected), rules.Where(rule => rule.Recommendation is not null && rule.DefaultSelected).Select(rule => rule.Id).Order(StringComparer.Ordinal));
        Assert.Equal(Ids(uncheckedItems), rules.Where(rule => rule.Recommendation is not null && !rule.DefaultSelected).Select(rule => rule.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(rules, rule => rule.Required && rule.Recommendation is not null);
    }
}
