using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientLanguageTests
{
    private static BuildDefinition Build(string languageDefinition = "native_language = \"id\"\nadditional_languages = { \"zh-CN\" = \"test.zh\", ko = \"test.ko\" }") =>
        GameCatalog.ParseBuild("id-400.toml", $$"""
        schema = 1
        [build]
        id = "id-400"
        game = "test"
        branch = "id"

        protocol_version = 400
        name = { en = "Fixture" }
        executable = "game.exe"
        [launch]
        login = "nmco"
        {{languageDefinition}}
        """, "test");

    private static GameDefinition Game(BuildDefinition build) =>
        new("test", GameStatus.Enabled, build.Name, "fixture.dll", "", "", null, "", [build]);

    [Theory]
    [InlineData("native_language = \"unknown\"")]
    [InlineData("planned_languages = [\"zh-CN\"]")]
    [InlineData("client_languages = [\"zh-CN\"]")]
    [InlineData("additional_languages = { ko = \"test.ko\" }")]
    [InlineData("native_language = \"id\"\nadditional_languages = { id = \"test.id\" }")]
    [InlineData("native_language = \"id\"\nadditional_languages = { ko = \"test.same\", en = \"test.same\" }")]
    public void ProfilesRejectAmbiguousOrRemovedLanguageFields(string definition) =>
        Assert.Throws<DefinitionException>(() => Build(definition));

    [Fact]
    public void NativeAndAdditionalLanguagesRemainDistinctButHaveOneSupportedList()
    {
        var build = Build();
        Assert.Equal("id", ClientLanguagePolicy.Native(Game(build), build));
        Assert.Equal(["id", "zh-CN", "ko"], ClientLanguagePolicy.Supported(Game(build), build));
        var instance = new InstanceDefinition("fixture", "Fixture", "test", build.Id, null, []);
        ClientLanguagePolicy.RequireAvailable(Game(build), build, instance, []);
        Assert.Throws<InvalidOperationException>(() => ClientLanguagePolicy.RequireAvailable(Game(build), build, instance with { Language = "zh-CN" }, []));
        Assert.Throws<InvalidOperationException>(() => ClientLanguagePolicy.RequireAvailable(Game(build), build, instance with { Language = "en" }, []));
    }

    [Fact]
    public void OnlySelectedTranslationIsDeployedAndExitRestoresTheNativeFiles()
    {
        var root = Directory.CreateTempSubdirectory("ft-languages-");
        try
        {
            var build = Build();
            var game = Game(build);
            PluginDefinition Package(string id, string text)
            {
                var folder = Directory.CreateDirectory(Path.Combine(root.FullName, id)).FullName;
                var source = Path.Combine(folder, "text.txt");
                File.WriteAllText(source, text);
                var bytes = File.ReadAllBytes(source);
                return new(id, "1.0.0", "test", "files", "", folder, build.Name, null, [], "", "", [build.Id], [], [], [], null, null)
                {
                    Files = [new("text.txt", "text.txt", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), [])],
                };
            }
            var chinese = Package("test.zh", "Chinese");
            var korean = Package("test.ko", "Korean");
            PluginDefinition[] packages = [chinese, korean];
            var instance = new InstanceDefinition("fixture", "Fixture", "test", build.Id, null,
                packages.Select(package => new InstancePlugin(package.Id, package.Version, true, false, new Dictionary<string, object>())).ToArray()) { Language = "zh-CN" };
            ClientLanguagePolicy.RequireAvailable(game, build, instance, packages);
            var active = ClientLanguagePolicy.ForLaunch(game, build, instance);
            Assert.Equal("test.zh", Assert.Single(active.Plugins).Id);
            Assert.Equal(2, instance.Plugins.Count);
            var client = Directory.CreateDirectory(Path.Combine(root.FullName, "client")).FullName;
            File.WriteAllText(Path.Combine(client, "text.txt"), "Native");
            var deployment = RuntimeDeployment.Apply(client, game.Id, null, FilePluginResolver.Resolve(active, build, packages));
            Assert.Equal("Chinese", File.ReadAllText(Path.Combine(client, "text.txt")));
            deployment.Restore();
            Assert.Equal("Native", File.ReadAllText(Path.Combine(client, "text.txt")));
            Assert.Empty(FilePluginResolver.Resolve(ClientLanguagePolicy.ForLaunch(game, build, instance with { Language = "id" }), build, packages));
            Assert.Equal("id", ClientLanguagePolicy.Native(game, build));
            Assert.Throws<InvalidOperationException>(() => ClientLanguagePolicy.RequireAvailable(game, build, instance,
                [chinese with { RestoreFiles = false }, korean]));
            Assert.Throws<InvalidOperationException>(() => ClientLanguagePolicy.RequireAvailable(game, build,
                instance with { Plugins = instance.Plugins.Select(selection => selection with { Enabled = false }).ToArray() }, packages));
            File.WriteAllText(Path.Combine(chinese.Folder, "text.txt"), "Tampered");
            Assert.Throws<IOException>(() => FilePluginResolver.Resolve(active, build, packages));
            Assert.Equal("Native", File.ReadAllText(Path.Combine(client, "text.txt")));
        }
        finally { root.Delete(true); }
    }
}
