using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreTests
{
    [Fact]
    public void ServerCatalogRequiresVersionAndSupportedBuildsWithoutChangingClientRules()
    {
        var server = Catalog.Replace("kind = \"client\"", "kind = \"server\"", StringComparison.Ordinal)
            .Replace("0.1.5.005", "2.25.0", StringComparison.Ordinal)
            .Replace("builds = [\"cn-279\"]", "builds = [\"cn-279\", \"cn-406\"]", StringComparison.Ordinal);
        var parsed = Assert.Single(StoreCatalogFile.Parse("server catalog", server).Packages);
        Assert.Equal("server", parsed.Kind);
        Assert.Equal(["cn-279", "cn-406"], parsed.Builds);
        foreach (var invalid in new[]
        {
            server.Replace("2.25.0", "", StringComparison.Ordinal),
            server.Replace("[\"cn-279\", \"cn-406\"]", "[]", StringComparison.Ordinal),
            server.Replace("[\"cn-279\", \"cn-406\"]", "[\"cn-279\", \"cn-279\"]", StringComparison.Ordinal),
            server + "\nupdate_mode = \"files\""
        }) Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("server catalog", invalid));
    }

    private const string ServerManifest = """
        schema = 1
        [release]
        id = "fixture.server"
        game = "bubblefighter"
        builds = ["cn-279", "cn-406"]
        version = "2.25.0"
        [[files]]
        path = "profiles/cn-406/profile.yaml"
        source_path = "objects/profile"
        size = 3
        sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        """;

    private static StorePackage ServerPackage(byte[] bytes) => new("fixture.server", "server", "bubblefighter",
        ["cn-279", "cn-406"], "2.25.0", new Dictionary<string, string> { ["en"] = "Fixture server" },
        new Dictionary<string, string>(), "Fixture", 3, "server.toml", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    [Fact]
    public void ServerManifestUsesSharedIntegrityAndPathChecksAndCannotBecomePluginArchive()
    {
        var bytes = Encoding.UTF8.GetBytes(ServerManifest);
        var package = ServerPackage(bytes);
        var file = Assert.Single(ClientRelease.Parse(package, bytes));
        Assert.Equal("profiles/cn-406/profile.yaml", file.Path);
        Assert.False(file.PluginArchive);
        Assert.Throws<InvalidDataException>(() => ClientRelease.Parse(package, Encoding.UTF8.GetBytes(ServerManifest + "\n")));
        foreach (var invalid in new[]
        {
            ServerManifest.Replace("version = \"2.25.0\"", "version = \"2.25.0\"\nformat = \"zip\"", StringComparison.Ordinal),
            ServerManifest.Replace("cn-406\"]", "kr-1256\"]", StringComparison.Ordinal),
            ServerManifest.Replace("profiles/cn-406/profile.yaml", "../profile.yaml", StringComparison.Ordinal)
        })
        {
            var changed = Encoding.UTF8.GetBytes(invalid);
            var error = Record.Exception(() => ClientRelease.Parse(ServerPackage(changed), changed));
            Assert.True(error is DefinitionException or ArgumentException or InvalidDataException, error?.ToString());
        }
        Assert.Throws<DefinitionException>(() => ClientRelease.Parse(package with { Builds = [] }, bytes));
    }
}
