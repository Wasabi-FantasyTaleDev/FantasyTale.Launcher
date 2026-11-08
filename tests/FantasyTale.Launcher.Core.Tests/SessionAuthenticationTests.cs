using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class SessionAuthenticationTests
{
    private static SessionSpec Spec()
    {
        var build = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(game => game.Id == "bubblefighter").Builds[0];
        return new SessionSpec("bubblefighter", build, "test", "test.log", "account", "fixture-private-ticket",
            new ServerEndpoint("127.0.0.1", 7100), [new("test.auth", "1.0.0", "C:/plugins/test.auth", true, new Dictionary<string, object>())])
        {
            LaunchId = "0123456789abcdef0123456789abcdef",
            AuthenticationPlugin = "test.auth"
        };
    }

    [Fact]
    public void NameModeIsExplicitAndHasNoTicket()
    {
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec() with { NameLogin = true, Ticket = null, Account = "Player_1" }))!;
        var login = (TomlTable)parsed["login"];
        Assert.Equal("name", login["mode"]); Assert.Equal("Player_1", login["account"]);
        Assert.False(login.ContainsKey("ticket"));
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec() with { NameLogin = true }));
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec() with { NameLogin = true, Ticket = null, Account = "" }));
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec() with { NameLogin = true, Ticket = null, Account = "bad name" }));
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec() with { NameLogin = true, Ticket = null, Account = new string('a', 33) }));
    }

    [Fact]
    public void GrantNamesOnlyTheExplicitlySelectedOwner()
    {
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec()))!;
        Assert.Equal("test.auth", ((TomlTable)parsed["login"])["credential_plugin"]);
        parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec() with { AuthenticationPlugin = null }))!;
        Assert.False(((TomlTable)parsed["login"]).ContainsKey("credential_plugin"));
    }

    [Theory]
    [InlineData("missing-owner")]
    [InlineData("empty-owner")]
    [InlineData("duplicate-owner")]
    [InlineData("no-ticket")]
    [InlineData("no-endpoint")]
    [InlineData("no-launch")]
    public void IncompleteOrAmbiguousGrantsFailWithoutExposingCredentials(string scenario)
    {
        var spec = Spec();
        spec = scenario switch
        {
            "missing-owner" => spec with { AuthenticationPlugin = "test.other" },
            "empty-owner" => spec with { AuthenticationPlugin = "" },
            "duplicate-owner" => spec with { Plugins = [spec.Plugins[0], spec.Plugins[0]] },
            "no-ticket" => spec with { Ticket = null },
            "no-endpoint" => spec with { Endpoint = null },
            "no-launch" => spec with { LaunchId = null },
            _ => throw new ArgumentException(scenario)
        };
        var error = Assert.Throws<ArgumentException>(() => SessionWriter.Render(spec));
        Assert.DoesNotContain("fixture-private-ticket", error.ToString(), StringComparison.Ordinal);
    }
}
