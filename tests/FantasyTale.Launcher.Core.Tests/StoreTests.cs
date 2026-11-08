using System.Net;
using System.Net.Http.Json;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreTests
{
    private const string Catalog = """
        schema = 1
        [catalog]
        id = "test-catalog"
        name = { en = "Test catalog", zh-CN = "测试资源" }
        [[packages]]
        id = "test.client"
        kind = "client"
        game = "bubblefighter"
        builds = ["cn-279"]
        version = "0.1.5.005"
        name = { en = "China 2011" }
        description = { en = "A client release." }
        publisher = "Test publisher"
        size = 1024
        manifest = "releases/client/0.1.5.005/manifest.toml"
        manifest_sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        """;

    [Fact]
    public void OptionalPluginArchiveValidatesKindPathSizeAndDigest()
    {
        const string offer = "\nplugin_archive = { source_path = \"plugins/fixture.zip\", size = 42, sha256 = \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\" }\n";
        var plugin = Catalog.Replace("kind = \"client\"", "kind = \"plugin\"", StringComparison.Ordinal)
            .Replace("0.1.5.005", "1.0.0", StringComparison.Ordinal);
        var package = Assert.Single(StoreCatalogFile.Parse("catalog", plugin + offer).Packages);
        Assert.Equal("plugins/fixture.zip", package.PluginArchive!.SourcePath);
        Assert.Equal(42, package.PluginArchive.Size);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog + offer));
        foreach (var invalid in new[] { offer.Replace("plugins/fixture.zip", "../fixture.zip", StringComparison.Ordinal),
            offer.Replace("fixture.zip", "fixture.exe", StringComparison.Ordinal),
            offer.Replace("size = 42", "size = 0", StringComparison.Ordinal),
            offer.Replace(new string('a', 64), "invalid", StringComparison.Ordinal) })
            Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", plugin + invalid));
    }

    [Fact]
    public void CatalogSeparatesAuthorsAndOptionalUploaderFromPublisher()
    {
        var original = Assert.Single(StoreCatalogFile.Parse("catalog", Catalog).Packages);
        Assert.Empty(original.Authors);
        Assert.Equal("", original.Uploader);
        var metadata = Catalog + "\nauthors = [\"Fantasy Tale Studio\"]\nuploader = \"Contributor\"\n";
        var package = Assert.Single(StoreCatalogFile.Parse("catalog", metadata).Packages);
        Assert.Equal("Fantasy Tale Studio", Assert.Single(package.Authors));
        Assert.Equal("Contributor", package.Uploader);
        Assert.Equal("Test publisher", package.Publisher);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog + "\nuploader = \"  \""));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog + "\nauthors = [\"\"]"));
    }

    [Fact]
    public async Task AlistAndHttpReadTheSameCatalogWithoutHeadOrForwardedApiTokens()
    {
        using var handler = new SourceHandler(Catalog); using var http = new HttpClient(handler);
        var alist = new AListSource(http, new Uri("https://files.example/"), "test-api-token");
        var plain = new HttpSource(new Uri("https://static.example/"));
        var a = StoreCatalogFile.Parse("alist catalog", await SourceDocumentReader.ReadAsync(http, alist, "FantasyTale/source.toml", default));
        var b = StoreCatalogFile.Parse("http catalog", await SourceDocumentReader.ReadAsync(http, plain, "FantasyTale/source.toml", default));
        Assert.Equal(a.Id, b.Id); Assert.Equal(a.Packages[0].Manifest, b.Packages[0].Manifest);
        Assert.Equal("0.1.5.005", a.Packages[0].Version); Assert.Equal("测试资源", a.Name["zh-CN"]);
        Assert.Equal(["POST files.example", "GET storage.example", "GET static.example"], handler.Requests);
    }

    [Theory]
    [InlineData("../source.toml")]
    [InlineData("//outside.example/file")]
    [InlineData("https://outside.example/file")]
    [InlineData("folder/%2e%2e/file")]
    [InlineData("folder\\file")]
    [InlineData("folder//file")]
    public async Task BothTransportsRejectAmbiguousPathsBeforeNetworkAccess(string path)
    {
        using var http = new HttpClient(new SourceHandler(Catalog));
        await Assert.ThrowsAsync<ArgumentException>(() => new AListSource(http, new Uri("https://files.example/")).ResolveAsync(path, default));
        await Assert.ThrowsAsync<ArgumentException>(() => new HttpSource(new Uri("https://files.example/")).ResolveAsync(path, default));
    }

    [Fact]
    public void CatalogRejectsInvalidReleasesAndFutureSchemas()
    {
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog.Replace("schema = 1", "schema = 3", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog.Replace("releases/client/", "../client/", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog.Replace("builds = [\"cn-279\"]", "builds = []", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog + "\nunknown = true"));
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("catalog", Catalog + "\n" + Catalog[Catalog.IndexOf("[[packages]]", StringComparison.Ordinal)..]));
    }

    [Fact]
    public async Task ChunkedMetadataReadsBeyondFormerDefaultWithoutAByteCeiling()
    {
        var text = new string('a', 5 * 1024 * 1024);
        using var http = new HttpClient(new SourceHandler(text));
        var source = new HttpSource(new Uri("https://static.example/"));
        Assert.Equal(text, await SourceDocumentReader.ReadAsync(http, source, "FantasyTale/source.toml", default));
        Assert.Equal(Encoding.UTF8.GetBytes(text), await SourceDocumentReader.ReadBytesAsync(http, source, "FantasyTale/source.toml", default));
    }

    [Fact]
    public async Task SourceRootAndUnicodeFileNamesRemainWithinTheirDirectory()
    {
        var source = new HttpSource(new Uri("https://static.example/store/"));
        var result = await source.ResolveAsync("客户端/one two.toml", default);
        Assert.Equal("https://static.example/store/%E5%AE%A2%E6%88%B7%E7%AB%AF/one%20two.toml", result.Url.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => SourceAddress.BaseUrl("https://user:password@static.example/"));
    }

    [Fact]
    public void PreviousBundledEndpointMigratesWithoutChangingItsDisabledChoice()
    {
        var root = Directory.CreateTempSubdirectory("ft-source-migration-").FullName;
        try
        {
            var store = new StoreSourceStore(root);
            var source = Assert.Single(store.List());
            store.Save(source with { BaseUrl = "http://wanwanlocksmith.pp.ua:5244/", Enabled = false, PreferIp = false });
            var migrated = Assert.Single(new StoreSourceStore(root).List());
            Assert.Equal("http://archive-cn.600819.xyz:5244/", migrated.BaseUrl);
            Assert.True(migrated.PreferIp);
            Assert.False(migrated.Enabled);
            store.Save(migrated with { PreferIp = false });
            Assert.False(Assert.Single(new StoreSourceStore(root).List()).PreferIp);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SourceSettingsPersistAndDoNotGrantUpdaterAuthority()
    {
        var root = Path.Combine(Path.GetTempPath(), "ft-store-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StoreSourceStore(root); var bundled = Assert.Single(store.List());
            Assert.Equal("http://archive-cn.600819.xyz:5244/", bundled.BaseUrl); Assert.True(bundled.BuiltIn); Assert.True(bundled.PreferIp);
            store.Save(bundled with { BaseUrl = null, Enabled = false });
            var migrated = Assert.Single(store.List()); Assert.Equal(bundled.BaseUrl, migrated.BaseUrl); Assert.False(migrated.Enabled);
            var custom = new StoreSource("custom", "Custom", "alist", "https://files.example", "FantasyTale/source.toml", true, false);
            store.Save(custom with { PreferIp = true });
            var loaded = new StoreSourceStore(root).List().Single(s => s.Id == "custom");
            Assert.Equal("https://files.example/", loaded.BaseUrl); Assert.True(loaded.PreferIp);
            store.Save(loaded with { Enabled = false }); Assert.False(store.List().Single(s => s.Id == "custom").Enabled);
            Assert.Throws<InvalidOperationException>(() => store.Delete(bundled.Id));
            Assert.Throws<ArgumentException>(() => store.Save(custom with { BuiltIn = true }));
            var text = File.ReadAllText(Path.Combine(root, "custom.toml")); Assert.DoesNotContain("token", text); Assert.DoesNotContain("updater", text);
            store.Delete(custom.Id); Assert.Single(store.List());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class SourceHandler(string text) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.Host);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("test-api-token", request.Headers.GetValues("Authorization").Single());
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("/FantasyTale/source.toml", body);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { code = 200, data = new { size = Encoding.UTF8.GetByteCount(text), is_dir = false, raw_url = "https://storage.example/catalog?sign=temporary" } }) };
            }
            Assert.Equal(HttpMethod.Get, request.Method); Assert.False(request.Headers.Contains("Authorization"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text))) };
        }
    }
}
