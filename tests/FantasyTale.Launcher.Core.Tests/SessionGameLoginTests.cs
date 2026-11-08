using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Verifies the game-login session contract, mode separation and credential validation.</summary>
public sealed class SessionGameLoginTests
{
    private static SessionGameLogin Login => new("https://game.example/api/", new string('a', 64), new string('b', 64),
        "0.1.32", "id-400", 400, "2014 release", "patch 1", "en", "id", "zh-CN");

    private static SessionSpec Spec()
    {
        var build = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games"))
            .Single(game => game.Id == "bubblefighter").Builds.Single(build => build.Id == "id-400");
        return new("bubblefighter", build, "fixture", "fixture.log", null, null, null, [])
        {
            LaunchId = "0123456789abcdef0123456789abcdef",
            GameLogin = Login,
        };
    }

    [Fact]
    public void ModuleDataPreservesUtf8JsonInItsOwnSessionTable()
    {
        const string payload = "{\"name\":\"正式服\",\"path\":\"a\\\\b\"}";
        var spec = Spec() with { ModuleData = new("bf-native-selector-presentation-v2", payload) };
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(spec))!;
        var module = (TomlTable)parsed["module_data"];
        Assert.Equal(spec.ModuleData.Capability, module["capability"]);
        Assert.Equal(payload, module["payload"]);
        Assert.False(((TomlTable)parsed["game_login"]).ContainsKey("payload"));
    }

    [Theory]
    [InlineData("uppercase")]
    [InlineData("missing-launch")]
    [InlineData("nul")]
    [InlineData("oversized-utf8")]
    public void ModuleDataRejectsInvalidNativeTransportValues(string fault)
    {
        var spec = Spec() with { ModuleData = new("fixture-capability", "data") };
        spec = fault switch
        {
            "uppercase" => spec with { ModuleData = new("Fixture", "data") },
            "missing-launch" => spec with { LaunchId = null, GameLogin = null },
            "nul" => spec with { ModuleData = new("fixture", "before\0after") },
            _ => spec with { ModuleData = new("fixture", new string('中', 10923)) },
        };
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(spec));
    }

    /// <summary>Separates the local readiness identity from the server grant and preserves launch metadata.</summary>
    [Fact]
    public void GameLoginCarriesLaunchMetadataWithoutConfusingTheServerAndReadinessIdentities()
    {
        var spec = Spec();
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(spec))!;
        Assert.Equal(2L, parsed["schema"]);
        Assert.Equal(spec.LaunchId, ((TomlTable)parsed["session"])["launch_id"]);
        var login = (TomlTable)parsed["game_login"];
        Assert.Equal(Login.ApiUrl, login["api_url"]);
        Assert.Equal(Login.LaunchId, login["launch_id"]);
        Assert.Equal(Login.LaunchKey, login["launch_key"]);
        Assert.Equal(Login.LauncherVersion, login["launcher_version"]);
        Assert.Equal("id-400", login["build"]);
        Assert.Equal(400L, login["protocol_version"]);
        Assert.Equal(Login.ReleaseVersion, login["release_version"]);
        Assert.Equal(Login.PatchVersion, login["patch_version"]);
        Assert.Equal("en", login["launcher_language"]);
        Assert.Equal("id", login["native_language"]);
        Assert.Equal("zh-CN", login["client_language"]);
        Assert.False(parsed.ContainsKey("login"));
        Assert.False(parsed.ContainsKey("messenger"));
    }

    /// <summary>Omits absent metadata without folding plugin records into the game-login table.</summary>
    /// <param name="version">An absent or empty optional version value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOptionalMetadataIsOmittedAndPluginSectionsStaySeparate(string? version)
    {
        var spec = Spec() with
        {
            GameLogin = Login with { ReleaseVersion = version, PatchVersion = version, LauncherLanguage = "", NativeLanguage = "", ClientLanguage = "" },
            Plugins = [new("fixture.plugin", "1.0.0", "C:/fixture", false, new Dictionary<string, object>())],
        };
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(spec))!;
        var login = (TomlTable)parsed["game_login"];
        Assert.Equal(6, login.Count);
        foreach (var key in new[] { "release_version", "patch_version", "launcher_language", "native_language", "client_language" })
            Assert.False(login.ContainsKey(key));
        Assert.Equal("fixture.plugin", ((TomlTable)Assert.Single((TomlTableArray)parsed["plugins"]))["id"]);
    }

    /// <summary>Rejects sessions that combine game-entered authentication with another login mode.</summary>
    /// <param name="mode">The incompatible session mode to construct.</param>
    [Theory]
    [InlineData("legacy-session")]
    [InlineData("ticket")]
    [InlineData("name-login")]
    public void OtherLoginModesCannotBeCombinedWithGameLogin(string mode)
    {
        var spec = mode switch
        {
            "legacy-session" => Spec() with { LaunchId = null },
            "ticket" => Spec() with { Ticket = "fixture-ticket" },
            "name-login" => Spec() with { NameLogin = true, Account = "fixture" },
            _ => throw new ArgumentException(mode),
        };
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(spec));
    }

    /// <summary>Rejects invalid game-login fields while keeping grant secrets out of diagnostics.</summary>
    /// <param name="scenario">The field validation case to exercise.</param>
    [Theory]
    [InlineData("api-scheme")]
    [InlineData("api-credentials")]
    [InlineData("api-query")]
    [InlineData("api-fragment")]
    [InlineData("api-whitespace")]
    [InlineData("short-id")]
    [InlineData("uppercase-id")]
    [InlineData("short-key")]
    [InlineData("uppercase-key")]
    [InlineData("empty-build")]
    [InlineData("invalid-build")]
    [InlineData("zero-protocol")]
    [InlineData("overflow-protocol")]
    [InlineData("empty-launcher-version")]
    [InlineData("control-launcher-version")]
    public void MalformedGameLoginSectionsFailWithoutExposingTheLaunchKey(string scenario)
    {
        var login = scenario switch
        {
            "api-scheme" => Login with { ApiUrl = "ftp://game.example/" },
            "api-credentials" => Login with { ApiUrl = "https://user@game.example/" },
            "api-query" => Login with { ApiUrl = "https://game.example/?key=secret" },
            "api-fragment" => Login with { ApiUrl = "https://game.example/#secret" },
            "api-whitespace" => Login with { ApiUrl = "https://game.example/has space" },
            "short-id" => Login with { LaunchId = new string('a', 32) },
            "uppercase-id" => Login with { LaunchId = new string('A', 64) },
            "short-key" => Login with { LaunchKey = new string('b', 63) },
            "uppercase-key" => Login with { LaunchKey = new string('B', 64) },
            "empty-build" => Login with { Build = "" },
            "invalid-build" => Login with { Build = "id/400" },
            "zero-protocol" => Login with { ProtocolVersion = 0 },
            "overflow-protocol" => Login with { ProtocolVersion = 65536 },
            "empty-launcher-version" => Login with { LauncherVersion = "" },
            "control-launcher-version" => Login with { LauncherVersion = "0.1.32\n" },
            _ => throw new ArgumentException(scenario),
        };
        var error = Assert.Throws<ArgumentException>(() => SessionWriter.Render(Spec() with { GameLogin = login }));
        Assert.DoesNotContain(login.LaunchKey, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Keeps the ticket-based launch format free of the game-login section.</summary>
    [Fact]
    public void TicketLaunchesDoNotAcquireAGameLoginSection()
    {
        var parsed = TomlSerializer.Deserialize<TomlTable>(SessionWriter.Render(Spec() with { GameLogin = null, Ticket = "fixture-ticket" }))!;
        Assert.False(parsed.ContainsKey("game_login"));
        Assert.Equal("fixture-ticket", ((TomlTable)parsed["login"])["ticket"]);
    }
}
