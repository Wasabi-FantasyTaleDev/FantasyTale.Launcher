using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientCorpusTests
{
    private sealed class CorpusTheoryAttribute : TheoryAttribute
    {
        public CorpusTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_CORPUS")))
                Skip = "Set FT_TEST_CLIENT_CORPUS to verify installed client identity without executing or modifying clients.";
        }
    }

    [CorpusTheory]
    [InlineData("BFLegacy_CN2013_0.2.35.001", "cn-406", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFLegacy_CN2014_0.2.46.001/BubbleFighter", "cn-404", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFLegacy_CN2014_0.2.51.002", "cn-706", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFLegacy_CN2015/BubbleFighter", "cn-717", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFLegacy_ID2014/CrazyShooter", "id-400", "CrazyShooter.exe", "CrazyShooterU.exe")]
    [InlineData("BFLegacy_ID2015/CrazyShooter", "id-1809", "CrazyShooter.exe", "CrazyShooterU.exe")]
    [InlineData("BFLegacy_KR2015", "kr-812", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFKorea/BubbleFighter", "kr-1256", "BubbleFighter.exe", "unpacked_BubbleFighter.exe")]
    [InlineData("BFLegacy_CN2019_0.2.98.3", "cn-774", "BubbleFighter.exe", "BubbleFighterU.exe")]
    [InlineData("BFLegacy_TW2019/BubbleFighter", "tw-454", "BubbleFighter.exe", "BubbleFighterU.exe")]
    public async Task InstalledRetailAndUnpackedCandidatesResolveThroughTheHostBridge(string relative, string expected, string retail, string unpacked)
    {
        var folder = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_CORPUS")!, relative));
        var repo = TestRepository.StartDirectory();
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        var games = Path.Combine(repo!.FullName, "games");
        var builds = GameCatalog.Load(games).Single(game => game.Id == "bubblefighter").Builds;
        var originalHashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in new[] { retail, unpacked })
        {
            var path = Path.Combine(folder, name);
            Assert.True(File.Exists(path), "Missing corpus executable: " + path);
            var identity = ClientExecutable.ReadIdentity(path);
            Assert.Equal([expected], builds.Where(build => ClientExecutable.Matches(identity, build)).Select(build => build.Id));
            await using var file = File.OpenRead(path);
            originalHashes.Add(name, await SHA256.HashDataAsync(file));
        }
        var data = Directory.CreateTempSubdirectory("ft-corpus-inspection-").FullName;
        try
        {
            await using var backend = new LauncherBackend(games, data);
            var replies = new List<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 1, method = "instances.inspectInstallation", @params = new { id = "corpus-probe", game = "bubblefighter", installationPath = folder } }), default);
            var reply = replies.Single(item => item["id"]?.GetValue<int>() == 1);
            Assert.Null(reply["error"]);
            Assert.Equal([expected], reply["result"]!["detectedBuildIds"]!.AsArray().Select(item => item!.GetValue<string>()));
            async Task<JsonNode> Create(int sequence, string id)
            {
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id = sequence, method = "instances.create", @params = new
                {
                    id, name = expected, game = "bubblefighter", build = expected, installationPath = folder, overwrite = false, existingClient = true,
                } }), default);
                return replies.Single(item => item["id"]?.GetValue<int>() == sequence);
            }
            var created = await Create(2, "corpus-client");
            Assert.Null(created["error"]);
            var repeated = await Create(3, "corpus-retry");
            Assert.Null(repeated["error"]);
            Assert.Equal(created["result"]!["id"]!.GetValue<string>(), repeated["result"]!["id"]!.GetValue<string>());
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 4, method = "instances.list" }), default);
            var listed = Assert.Single(replies.Single(item => item["id"]?.GetValue<int>() == 4)["result"]!.AsArray())!;
            Assert.Equal("corpus-client", listed["id"]!.GetValue<string>());
            Assert.Equal(expected, listed["build"]!.GetValue<string>());
            Assert.Equal(folder, listed["installationPath"]!.GetValue<string>());
            Assert.True(listed["clientInstalled"]!.GetValue<bool>());
            Assert.True(listed["externalInstallation"]!.GetValue<bool>());
            foreach (var name in new[] { retail, unpacked })
            {
                await using var file = File.OpenRead(Path.Combine(folder, name));
                Assert.Equal(originalHashes[name], await SHA256.HashDataAsync(file));
            }
        }
        finally { Directory.Delete(data, recursive: true); }
    }
}
