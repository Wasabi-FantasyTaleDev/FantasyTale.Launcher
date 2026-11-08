using FantasyTale.Launcher.Host;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class StaticFilesTests : IDisposable
{
    private const string Origin = "https://fantasytale.invalid/";
    private readonly string _root = Directory.CreateTempSubdirectory("ft-local-assets-").FullName;

    [Theory]
    [InlineData("", "text/html; charset=utf-8", false)]
    [InlineData("assets/index.js", "text/javascript; charset=utf-8", false)]
    [InlineData("games/Test/media/clip.mp4", "video/mp4", true)]
    public void LocalPageScriptsAndGameMediaResolveWithoutNetwork(string path, string expectedType, bool gameAsset)
    {
        var web = Path.Combine(_root, "web");
        var games = Path.Combine(_root, "games");
        var file = Path.Combine(gameAsset ? games : web, gameAsset ? path[6..] : path.Length == 0 ? "index.html" : path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "local-resource");
        using var stream = StaticFiles.Open(web, games, Origin, Origin + path, out var type);
        Assert.NotNull(stream);
        Assert.Equal(expectedType, type);
        using var reader = new StreamReader(stream);
        Assert.Equal("local-resource", reader.ReadToEnd());
    }

    [Theory]
    [InlineData("https://example.com/index.html")]
    [InlineData("https://fantasytale.invalid.example.com/index.html")]
    [InlineData("http://fantasytale.invalid/index.html")]
    [InlineData("https://fantasytale.invalid:444/index.html")]
    [InlineData("https://user@fantasytale.invalid/index.html")]
    public void OtherOriginsCannotReadPackagedFiles(string url)
    {
        Assert.Null(StaticFiles.Open(_root, _root, Origin, url, out _));
    }

    [Theory]
    [InlineData("missing.js")]
    [InlineData("session.toml")]
    [InlineData("module.dll")]
    [InlineData("%2e%2e%2fsecret.txt")]
    [InlineData("%2e%2e%5csecret.txt")]
    [InlineData("index.html%3asecret")]
    public void MissingAndForbiddenResourcesAreHandledLocally(string path)
    {
        File.WriteAllText(Path.Combine(_root, "session.toml"), "secret");
        File.WriteAllText(Path.Combine(_root, "module.dll"), "binary");
        using var stream = StaticFiles.Open(_root, _root, Origin, Origin + path, out _);
        Assert.NotNull(stream);
        Assert.Equal(0, stream.Length);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
