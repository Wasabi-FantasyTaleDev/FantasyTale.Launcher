using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData("http", "valid")]
    [InlineData("alist", "valid")]
    [InlineData("github", "valid")]
    [InlineData("http", "missing")]
    [InlineData("alist", "missing")]
    [InlineData("github", "missing")]
    [InlineData("http", "corrupt")]
    [InlineData("alist", "corrupt")]
    [InlineData("github", "corrupt")]
    [InlineData("http", "changed-index")]
    [InlineData("alist", "changed-index")]
    [InlineData("github", "changed-index")]
    public async Task SignedSourceRequiresExactAuthenticatedIndexBeforeStaging(string kind, string scenario)
    {
        using var rsa = RSA.Create(2048);
        Policy(kind);
        File.AppendAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""

            signature_path = "launcher/stable.toml.sig"
            signature_public_key = "{Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}"
            """);
        var index = IndexBytes(Manifest());
        var signature = rsa.SignData(index, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (scenario == "corrupt") signature[0] ^= 0xff;
        if (scenario == "changed-index") index = IndexBytes(Manifest(3));
        using var handler = new SignedSourceHandler(index, scenario == "missing" ? null : signature);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var state = await service.CheckAsync();
        Assert.Equal(0, handler.PayloadRequests);
        if (scenario == "valid")
        {
            Assert.Equal("available", state.Status);
            Assert.Equal(2, state.Revision);
            await service.DownloadAsync(state.ManifestSha256!);
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
            Assert.Equal("ready", service.List()[0].Stage);
            Assert.Equal(2, handler.PayloadRequests);
        }
        else
        {
            Assert.Equal("failed", state.Status);
            Assert.Equal(scenario == "missing" ? "feedMissing" : "invalidInformation", state.FailureReason);
            Assert.Null(state.ManifestSha256);
            Assert.Empty(service.List());
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(Hash(index)));
            Assert.Equal(0, handler.PayloadRequests);
            Assert.False(File.Exists(Path.Combine(Data, "launcher-update", "release.toml")));
        }
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    private sealed class SignedSourceHandler(byte[] index, byte[]? signature) : HttpMessageHandler
    {
        internal int PayloadRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "raw.githubusercontent.com")
            {
                Assert.StartsWith("/team/repo/main/", path, StringComparison.Ordinal);
                Assert.Null(request.Headers.Authorization);
                path = path["/team/repo/main".Length..];
            }
            var resolving = path == "/api/fs/get";
            if (resolving)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                path = body.RootElement.GetProperty("path").GetString()!;
            }
            byte[]? content = path switch
            {
                "/launcher/stable.toml" => index,
                "/launcher/stable.toml.sig" => signature,
                "/objects/app" or "/objects/helper" => "new"u8.ToArray(),
                "/objects/index" => "web"u8.ToArray(),
                _ => null
            };
            if (content is null) return new(HttpStatusCode.NotFound);
            if (resolving)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { code = 200, data = new { raw_url = "https://storage.example" + path, is_dir = false, size = content.Length } })) };
            if (path.StartsWith("/objects/", StringComparison.Ordinal)) Interlocked.Increment(ref PayloadRequests);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }
    }
}
