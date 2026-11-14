using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class OverlayPackageFilesTests : IDisposable
{
    private const string Hash = "0123456789abcdef0123";
    private readonly string _root = Directory.CreateTempSubdirectory("ft-package-files-").FullName;
    private readonly OverlayPackageFiles _files = new();

    public OverlayPackageFilesTests()
    {
        var data = Path.Combine(_root, "assets", "data");
        Directory.CreateDirectory(Path.Combine(data, "atlas")); Directory.CreateDirectory(Path.Combine(data, "builds"));
        File.WriteAllBytes(Path.Combine(data, "atlas", Hash + ".webp"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(data, "pack.json"), "{}");
        File.WriteAllText(Path.Combine(data, "builds", "kr-812.json"), "{}");
        File.WriteAllText(Path.Combine(data, "a b.png"), "x");
        File.WriteAllText(Path.Combine(_root, "assets", "secret.txt"), "outside the data folder");
        Directory.CreateDirectory(Path.Combine(_root, "overlay"));
        File.WriteAllText(Path.Combine(_root, "overlay", "style.css"), "a{}");
        _files.SetRoots(new Dictionary<string, string>
        { ["fixture.previews"] = Path.Combine(_root, "assets", "data"), ["fixture.panel"] = Path.Combine(_root, "overlay") });
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static string Url(string path) => "https://packages.fantasytale.local/" + path;

    [Theory]
    [InlineData("fixture.previews/pack.json", "application/json; charset=utf-8", false)]
    [InlineData("fixture.previews/builds/kr-812.json", "application/json; charset=utf-8", false)]
    [InlineData("fixture.previews/atlas/" + Hash + ".webp", "image/webp", true)]
    [InlineData("fixture.previews/a%20b.png", "image/png", false)]
    [InlineData("fixture.panel/style.css", "text/css; charset=utf-8", false)]
    public void ServesFilesOfRegisteredPackagesWithTheirTypeAndCaching(string path, string type, bool immutable)
    {
        var result = _files.Resolve("GET", Url(path));
        Assert.Equal(200, result.Status);
        Assert.True(File.Exists(result.Path));
        Assert.Equal(type, result.ContentType);
        Assert.Equal(immutable, result.Immutable);
        Assert.Contains(immutable ? "immutable" : "no-cache", result.Headers("https://overlay.fantasytale.local"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".json", "application/json; charset=utf-8")]
    [InlineData(".WEBP", "image/webp")]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".svg", "image/svg+xml")]
    [InlineData(".css", "text/css; charset=utf-8")]
    [InlineData(".js", "text/javascript; charset=utf-8")]
    [InlineData(".woff2", "font/woff2")]
    [InlineData(".otf", "font/otf")]
    [InlineData(".ttf", "font/ttf")]
    [InlineData(".bin", "application/octet-stream")]
    public void ContentTypesCoverTheFormatsPackagesShip(string extension, string type) =>
        Assert.Equal(type, OverlayPackageFiles.ContentTypeFor(extension));

    [Theory]
    [InlineData("fixture.previews/../secret.txt")]
    [InlineData("fixture.previews/%2e%2e/secret.txt")]
    [InlineData("fixture.previews/atlas/..%2f..%2fsecret.txt")]
    [InlineData("fixture.previews/atlas/%2e%2e%5c%2e%2e%5csecret.txt")]
    [InlineData("fixture.previews//pack.json")]
    [InlineData("fixture.previews/atlas//%2e%2e/pack.json")]
    [InlineData("fixture.previews/C%3A/Windows/win.ini")]
    [InlineData("fixture.previews/pack.json::$DATA")]
    [InlineData("fixture.previews/%5cWindows")]
    [InlineData("fixture.previews/pack.json%00.png")]
    [InlineData("Fixture.Previews/pack.json")]
    [InlineData("%2e%2e/pack.json")]
    public void RefusesTraversalAbsolutePathsAndMalformedNames(string path)
    {
        var result = _files.Resolve("GET", Url(path));
        Assert.NotEqual(200, result.Status);
        Assert.Null(result.Path);
    }

    [Theory]
    [InlineData("unknown.package/pack.json", 404)]
    [InlineData("fixture.previews", 404)]
    [InlineData("fixture.previews/", 404)]
    [InlineData("fixture.previews/atlas", 404)]
    [InlineData("fixture.previews/atlas/missing.webp", 404)]
    [InlineData("fixture.previews/secret.txt", 404)]
    public void AnswersNotFoundForUnknownIdsDirectoriesAndMissingFiles(string path, int status) =>
        Assert.Equal(status, _files.Resolve("GET", Url(path)).Status);

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    public void OnlyGetIsServed(string method) => Assert.Equal(405, _files.Resolve(method, Url("fixture.previews/pack.json")).Status);

    [Theory]
    [InlineData("http://packages.fantasytale.local/fixture.previews/pack.json")]
    [InlineData("https://packages.fantasytale.local:8443/fixture.previews/pack.json")]
    [InlineData("https://user@packages.fantasytale.local/fixture.previews/pack.json")]
    [InlineData("https://overlay.fantasytale.local/fixture.previews/pack.json")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("not a url")]
    public void OnlyThePackageHostOverHttpsIsServed(string uri) => Assert.Equal(400, _files.Resolve("GET", uri).Status);

    [Fact]
    public void QueryAndFragmentDoNotChangeTheFile()
    {
        var plain = _files.Resolve("GET", Url("fixture.previews/pack.json"));
        Assert.Equal(plain, _files.Resolve("GET", Url("fixture.previews/pack.json?v=2#frag")));
    }

    [Fact]
    public void LinksInsideAPackageAreRefused()
    {
        var target = Path.Combine(_root, "assets");
        var link = Path.Combine(_root, "assets", "data", "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        Assert.Equal(403, _files.Resolve("GET", Url("fixture.previews/linked/secret.txt")).Status);
        Assert.Equal(403, _files.Resolve("GET", Url("fixture.previews/linked")).Status);
    }

    [Fact]
    public void RegisteredFoldersMustBeAbsoluteExistingAndUnlinkedAndAFailedUpdateKeepsTheOldSet()
    {
        Assert.Throws<ArgumentException>(() => _files.SetRoots(new Dictionary<string, string> { ["fixture.previews"] = "relative" }));
        Assert.Throws<ArgumentException>(() => _files.SetRoots(new Dictionary<string, string> { ["fixture.previews"] = Path.Combine(_root, "nowhere") }));
        Assert.Throws<ArgumentException>(() => _files.SetRoots(new Dictionary<string, string> { ["Bad Id"] = _root }));
        Assert.Equal(200, _files.Resolve("GET", Url("fixture.previews/pack.json")).Status);
        _files.SetRoots(new Dictionary<string, string>());
        Assert.Equal(404, _files.Resolve("GET", Url("fixture.previews/pack.json")).Status);
    }

    [Fact]
    public void UrlsRoundTripAndValidateBothArguments()
    {
        var url = OverlayPackageFiles.Url("fixture.previews", "a b.png");
        Assert.Equal("https://packages.fantasytale.local/fixture.previews/a%20b.png", url);
        Assert.Equal(200, _files.Resolve("GET", url).Status);
        Assert.Throws<ArgumentException>(() => OverlayPackageFiles.Url("Fixture", "pack.json"));
        Assert.Throws<ArgumentException>(() => OverlayPackageFiles.Url("fixture.previews", "../pack.json"));
        Assert.Throws<ArgumentException>(() => OverlayPackageFiles.Url("fixture.previews", "/pack.json"));
        Assert.Throws<ArgumentException>(() => OverlayPackageFiles.Url("fixture.previews", ""));
    }

    [Fact]
    public void ThePageHelperAndTheLauncherAgreeOnTheContract()
    {
        var page = File.ReadAllText(Path.Combine(TestRepository.StartDirectory().FullName, "src", "FantasyTale.Launcher.Overlay", "overlay", "index.html"));
        Assert.Contains("packageUrl(packageId, path)", page, StringComparison.Ordinal);
        Assert.Contains("'https://packages.fantasytale.local/'", page, StringComparison.Ordinal);
        Assert.Contains("provide('fantasyTalePackages'", page, StringComparison.Ordinal);
        Assert.Contains(OverlayPackageFiles.Host, page, StringComparison.Ordinal);
    }
}
