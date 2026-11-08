using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Verifies persistence of the per-server HTTPS preference and the HTTP default.</summary>
public sealed class ServerProfileHttpsTests
{
    private static ServerDefinition Server => new("fixture", "bubblefighter", "Fixture", "http://game.example/api/", null, true, false);

    /// <summary>Round-trips the transport preference independently of the entered API address.</summary>
    /// <param name="useHttps">The transport preference to persist.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HttpsPreferenceRoundTripsWithoutChangingTheEnteredAddress(bool useHttps)
    {
        var profile = Server with { UseHttps = useHttps };
        var text = ServerProfileFile.Format(profile);
        var parsed = ServerProfileFile.Parse("fixture.toml", text);
        Assert.Equal(useHttps, parsed.UseHttps);
        Assert.Equal(Server.ApiUrl, parsed.ApiUrl);
        Assert.Equal(text, ServerProfileFile.Format(parsed));
        Assert.Equal(useHttps, text.Contains("use_https = true", StringComparison.Ordinal));
    }

    /// <summary>Preserves HTTP when a profile omits the preference or explicitly disables it.</summary>
    /// <param name="preference">The optional TOML assignment following the server address.</param>
    [Theory]
    [InlineData("")]
    [InlineData("use_https = false\n")]
    public void ExistingAndExplicitlyDisabledProfilesKeepHttp(string preference)
    {
        var text = "schema = 1\n[server]\nid = \"fixture\"\ngame = \"bubblefighter\"\nname = \"Fixture\"\napi_url = \"http://game.example/api/\"\n" + preference;
        var parsed = ServerProfileFile.Parse("fixture.toml", text);
        Assert.False(parsed.UseHttps);
        Assert.Equal(Server.ApiUrl, parsed.ApiUrl);
    }
}
