using FantasyTale.Launcher.Web;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class TrustedWebDocumentTests
{
    [Theory]
    [InlineData("https://fantasytale.invalid/index.html")]
    [InlineData("HTTPS://FANTASYTALE.INVALID:443/index.html#/settings")]
    [InlineData("https://fantasytale.invalid/index.html#route%2fsegment")]
    public void AllowsOnlyTheStartupDocumentAndItsHashRoutes(string source) =>
        Assert.True(new TrustedWebDocument("https://fantasytale.invalid/index.html").Allows(source));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" https://fantasytale.invalid/index.html")]
    [InlineData("https://fantasytale.invalid/")]
    [InlineData("https://fantasytale.invalid/Index.html")]
    [InlineData("https://fantasytale.invalid/index.html?extra=true")]
    [InlineData("https://fantasytale.invalid:444/index.html")]
    [InlineData("http://fantasytale.invalid/index.html")]
    [InlineData("https://fantasytale.invalid.example/index.html")]
    [InlineData("https://user@fantasytale.invalid/index.html")]
    [InlineData("https://fantasytale.invalid\\index.html")]
    [InlineData("https://fantasytale.invalid/%2findex.html")]
    [InlineData("https://fantasytale.invalid/%5Cindex.html")]
    [InlineData("file:///index.html")]
    [InlineData("data:text/html,test")]
    [InlineData("blob:https://fantasytale.invalid/test")]
    [InlineData("about:blank")]
    public void RejectsOtherDocumentsAndAmbiguousSources(string? source) =>
        Assert.False(new TrustedWebDocument("https://fantasytale.invalid/index.html").Allows(source));

    [Fact]
    public void ExplicitDevelopmentAndOverlayQueriesRemainPartOfTheDocument()
    {
        var development = new TrustedWebDocument("http://localhost:5173/?test=1");
        Assert.True(development.Allows("http://localhost:5173/?test=1#/home"));
        Assert.False(development.Allows("http://localhost:5174/?test=1"));
        Assert.False(development.Allows("http://127.0.0.1:5173/?test=1"));
        Assert.False(development.Allows("http://localhost:5173/"));
        var overlay = new TrustedWebDocument("https://overlay.fantasytale.local/index.html?theme=v2&preview=game-ui");
        Assert.True(overlay.Allows("https://overlay.fantasytale.local/index.html?theme=v2&preview=game-ui#panel"));
        Assert.False(overlay.Allows("https://overlay.fantasytale.local/index.html?theme=v3&preview=game-ui"));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("https://user@host/index.html")]
    [InlineData("https://host/%2findex.html")]
    public void RefusesInvalidStartupConfiguration(string document) =>
        Assert.Throws<ArgumentException>(() => new TrustedWebDocument(document));
}
