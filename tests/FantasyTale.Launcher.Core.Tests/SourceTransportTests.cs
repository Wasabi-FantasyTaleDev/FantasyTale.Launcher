using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class SourceTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IpPreferenceControlsBothAListMetadataAndResolvedDownloads(bool preferIp)
    {
        using var handler = new IpSourceHandler(preferIp);
        using var http = new HttpClient(handler);
        var source = SourceTransport.Open(http, "alist", new Uri("http://localhost:5244/"), preferIp);
        Assert.Equal("model", await SourceDocumentReader.ReadAsync(http, source, "model", default));
        var payload = await source.ResolveAsync("model", default);
        Assert.Equal(preferIp, IPAddress.TryParse(payload.Url.Host, out _));
        Assert.Empty(payload.Headers);
        var staticSource = SourceTransport.Open(http, "http", new Uri("http://localhost:5244/"), preferIp);
        var direct = await staticSource.ResolveAsync("folder/model", default);
        Assert.Equal(preferIp, IPAddress.TryParse(direct.Url.Host, out _));
        Assert.Equal("/folder/model", direct.Url.AbsolutePath);
        var secure = SourceTransport.Open(http, "http", new Uri("https://localhost:5244/"), preferIp);
        Assert.Equal("localhost", (await secure.ResolveAsync("model", default)).Url.Host);
    }

    private sealed class IpSourceHandler(bool preferIp) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(preferIp, IPAddress.TryParse(request.RequestUri!.Host, out _));
            Assert.Null(request.Headers.Authorization);
            var content = request.Method == HttpMethod.Post
                ? "{\"code\":200,\"data\":{\"size\":5,\"is_dir\":false,\"raw_url\":\"http://localhost:5244/model\"}}"
                : "model";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }

    [Theory]
    [InlineData("https://github.com/team/releases", "https://raw.githubusercontent.com/team/releases/HEAD/source.toml")]
    [InlineData("https://github.com/team/releases.git", "https://raw.githubusercontent.com/team/releases/HEAD/source.toml")]
    [InlineData("https://github.com/team/releases/tree/main/catalog", "https://raw.githubusercontent.com/team/releases/main/catalog/source.toml")]
    [InlineData("https://github.com/team/releases/releases/download/v1", "https://github.com/team/releases/releases/download/v1/source.toml")]
    [InlineData("https://raw.githubusercontent.com/team/releases/main", "https://raw.githubusercontent.com/team/releases/main/source.toml")]
    [InlineData("https://team.github.io/releases", "https://team.github.io/releases/source.toml")]
    public async Task GitHubRootsResolveThroughTheCommonPathContract(string root, string expected)
    {
        using var http = new HttpClient();
        var source = SourceTransport.Open(http, "github", new Uri(root));
        Assert.Equal("github", source.Kind);
        Assert.Equal(expected, (await source.ResolveAsync("source.toml", default)).Url.AbsoluteUri);
        await Assert.ThrowsAsync<ArgumentException>(() => source.ResolveAsync("../secret", default));
    }

    [Theory]
    [InlineData("https://github.com.evil.invalid/team/repo/")]
    [InlineData("https://github.com/team/")]
    [InlineData("https://github.com/team/repo/issues/")]
    [InlineData("http://github.com/team/repo/")]
    public void InvalidGitHubRootsAreRejectedBeforeSaving(string root) => Assert.Throws<ArgumentException>(() => SourceTransport.Validate("github", root));

    [Theory]
    [InlineData("http")]
    [InlineData("alist")]
    [InlineData("github")]
    public async Task EveryTransportReadsListingsAndManifestsAndDownloadsVerifiedFiles(string kind)
    {
        var root = Directory.CreateTempSubdirectory("ft-source-transport-").FullName;
        try
        {
            var payload = Encoding.UTF8.GetBytes("verified content"); var digest = Convert.ToHexStringLower(SHA256.HashData(payload));
            var catalog = Encoding.UTF8.GetBytes("schema=2\n[catalog]\nid=\"fixture\"\nname={en=\"Fixture\"}\n[[servers]]\nid=\"one\"\ngame=\"fixture\"\nname=\"One\"\napi_url=\"https://game.example/\"\nbuilds=[\"kr-1\"]\n");
            var documents = new Dictionary<string, byte[]> { ["source.toml"] = catalog, ["manifest.toml"] = Encoding.UTF8.GetBytes("a release manifest"), ["asset.bin"] = payload };
            using var mirrors = new GitHubMirrors([new("fixture", "Fixture", new Uri("https://mirror.invalid/"))], new Uri("https://raw.githubusercontent.com/team/repo/main/probe"), [1], 1, TimeSpan.FromSeconds(1), 1);
            using var handler = new Handler(documents); using var http = new HttpClient(new GitHubRoutingHandler(() => true, () => "fixture", mirrors, handler));
            var address = kind == "github" ? "https://github.com/team/repo/tree/main/" : "https://files.invalid/";
            var source = SourceTransport.Open(http, kind, new Uri(address));
            Assert.Single(StoreCatalogFile.Parse("source.toml", await SourceDocumentReader.ReadAsync(http, source, "source.toml", default)).Servers);
            Assert.Equal(documents["manifest.toml"], await SourceDocumentReader.ReadBytesAsync(http, source, "manifest.toml", default));
            var resolved = await source.ResolveAsync("asset.bin", default);
            var destination = Path.Combine(root, "asset.bin");
            await new HttpDownloadEngine(http).DownloadAsync(new(resolved, destination, payload.Length, digest), null, default);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.Equal(kind == "alist" ? 6 : 3, handler.Requests);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MirrorPrefixesPublicUrlsAndRejectsLookalikeHosts()
    {
        var uri = new Uri("https://raw.githubusercontent.com/team/repo/main/source.toml");
        Assert.Equal("https://mirror.invalid/" + uri.AbsoluteUri, GitHubRequestRouting.Route(uri, new Uri("https://mirror.invalid/")).AbsoluteUri);
        Assert.True(GitHubRequestRouting.IsGitHub(new Uri("https://api.github.com/repos/team/repo")));
        Assert.False(GitHubRequestRouting.IsGitHub(new Uri("https://github.com.attacker.invalid/")));
    }

    private sealed class Handler(IReadOnlyDictionary<string, byte[]> documents) : HttpMessageHandler
    {
        internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
                var path = body.GetProperty("path").GetString()!.TrimStart('/');
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { code = 200, data = new { is_dir = false, size = documents[path].Length, raw_url = "https://payload.invalid/" + path } })) };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(documents[request.RequestUri!.Segments[^1]]) };
        }
    }
}
