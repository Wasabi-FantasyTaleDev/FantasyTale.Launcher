using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class SessionMessengerTests
{
    private const string Ticket = "v1.eyJzdWIiOiJhIn0.c2lnbmF0dXJl";

    private static BuildDefinition Build(string id) =>
        GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(game => game.Id == "bubblefighter").Builds.Single(b => b.Id == id);

    private static SessionSpec Spec(SessionMessenger? messenger) =>
        new SessionSpec("bubblefighter", Build("kr-812"), "test", "test.log", "account", "fixture-private-ticket", new ServerEndpoint("127.0.0.1", 7100), [])
        {
            LaunchId = "0123456789abcdef0123456789abcdef",
            Messenger = messenger
        };

    [Fact]
    public void TheMessengerSectionNamesItsTransportUrlAndTicket()
    {
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec(new("http", "http://203.0.113.10:8091", Ticket))))!;
        var messenger = (TomlTable)parsed["messenger"];
        Assert.Equal("http", messenger["transport"]);
        Assert.Equal("http://203.0.113.10:8091", messenger["url"]);
        Assert.Equal(Ticket, messenger["ticket"]);
        parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec(null)))!;
        Assert.False(parsed.ContainsKey("messenger"));
    }

    [Theory]
    [InlineData("gateway", "http://h/", Ticket)]
    [InlineData("http", "ftp://h/", Ticket)]
    [InlineData("http", "http://user@h/", Ticket)]
    [InlineData("http", "http://h/#top", Ticket)]
    [InlineData("http", "http://h/", "bad ticket")]
    public void MalformedMessengerSectionsAreRefusedWithoutExposingTheTicket(string transport, string url, string ticket)
    {
        var error = Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec(new(transport, url, ticket))));
        Assert.DoesNotContain(ticket, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-private-ticket", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ALegacySessionCannotCarryTheMessenger() =>
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec(new("http", "http://h/", Ticket)) with { LaunchId = null }));

    [Fact]
    public void OnlyStandInBuildsReceiveTheServersMessengerSignIn()
    {
        var signIn = new LaunchMessenger("http://203.0.113.10:8091", Ticket, DateTimeOffset.UtcNow.AddMinutes(5));
        const string launch = "0123456789abcdef0123456789abcdef";
        Assert.Equal(new SessionMessenger("http", signIn.Url, Ticket), LaunchService.SessionMessengerFor(Build("kr-812"), launch, "3.5.0", signIn));
        Assert.Equal(new SessionMessenger("http", signIn.Url, Ticket), LaunchService.SessionMessengerFor(Build("cn-324"), launch, "3.5.0", signIn));
        Assert.Equal(new SessionMessenger("http", signIn.Url, Ticket), LaunchService.SessionMessengerFor(Build("cn-404"), launch, "3.5.0", signIn));
        Assert.Null(LaunchService.SessionMessengerFor(Build("cn-735"), launch, "3.5.0", signIn));
        Assert.Null(LaunchService.SessionMessengerFor(Build("kr-812"), null, "3.5.0", signIn));
        Assert.Null(LaunchService.SessionMessengerFor(Build("kr-812"), launch, "3.5.0", null));
        Assert.Null(LaunchService.SessionMessengerFor(Build("kr-812"), launch, "3.4.0", signIn));
        Assert.Null(LaunchService.SessionMessengerFor(Build("kr-812"), launch, null, signIn));
    }
}
