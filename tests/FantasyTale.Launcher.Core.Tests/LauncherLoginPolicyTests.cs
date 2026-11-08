using FantasyTale.Launcher.Core.Servers;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LauncherLoginPolicyTests
{
    [Fact]
    public void OnlyExplicitNameRoomsMayOverrideTheBuildTicketRequirement()
    {
        Assert.Equal("none", LauncherLoginPolicy.Resolve("required", "required", "none", room: true));
        Assert.Equal("required", LauncherLoginPolicy.Resolve("required", "required", "required", room: true));
        Assert.Equal("required", LauncherLoginPolicy.Resolve("required", "required", null, room: true));
        Assert.Equal("required", LauncherLoginPolicy.Resolve("required", "required", "none", room: false));
    }

    [Theory]
    [InlineData("required", "optional", "optional", "required")]
    [InlineData("required", "optional", "none", "required")]
    [InlineData("optional", "required", "none", "none")]
    [InlineData("optional", "required", "required", "required")]
    [InlineData("optional", "required", null, "optional")]
    [InlineData(null, "required", null, "required")]
    public void ClientCredentialRequirementsSurviveServerPreferences(string? build, string game, string? server, string expected) =>
        Assert.Equal(expected, LauncherLoginPolicy.Resolve(build, game, server));
}
