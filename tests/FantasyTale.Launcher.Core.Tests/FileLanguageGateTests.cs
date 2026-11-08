using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class FileLanguageGateTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ft-language-files-").FullName;

    [Theory]
    [InlineData(null, "ko", "merged")]
    [InlineData("ko", "zh-CN", "merged")]
    [InlineData("ZH-cn", "ko", "original")]
    [InlineData("zh-TW", "ko", "original")]
    public void SelectedGameLanguageFiltersVariantsBeforeCollisionAndPayloadChecks(string? selected, string native, string expected)
    {
        var package = Package();
        var build = Build(native);
        var instance = new InstanceDefinition("sample", "Sample", "fixture", build.Id, null,
            [new(package.Id, package.Version, true, false, new Dictionary<string, object>())]) { Language = selected };
        var resolved = Assert.Single(FilePluginResolver.Resolve(instance, build, [package]));
        Assert.Equal(expected, File.ReadAllText(resolved.Source));
        var client = Directory.CreateDirectory(Path.Combine(_folder, "client")).FullName;
        File.WriteAllText(Path.Combine(client, "font.ttf"), "preceding");
        _ = RuntimeDeployment.Apply(client, "fixture", null, [resolved]);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(client, "font.ttf")));
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("preceding", File.ReadAllText(Path.Combine(client, "font.ttf")));
    }

    [Fact]
    public void ActiveLanguageVariantsStillRejectDestinationCollisions()
    {
        var package = Package();
        package = package with { Files = package.Files.Select(file => file with { ClientLanguages = [], ExcludeClientLanguages = [] }).ToArray() };
        var instance = new InstanceDefinition("sample", "Sample", "fixture", "fixture-1", null,
            [new(package.Id, package.Version, true, false, new Dictionary<string, object>())]);
        Assert.Throws<IOException>(() => FilePluginResolver.Resolve(instance, Build("ko"), [package]));
    }

    [Theory]
    [InlineData("client_languages=['zh-CN','ZH-cn']")]
    [InlineData("exclude_client_languages=['ko','KO']")]
    [InlineData("client_languages=['../bad']")]
    [InlineData("client_languages=['zh-CN']\nexclude_client_languages=['ZH-cn']")]
    public void InvalidOrContradictoryLanguageGatesFailAtLoad(string gates)
    {
        var package = Package();
        var path = Path.Combine(package.Folder, "plugin.toml");
        var text = File.ReadAllText(path);
        text = text[..text.IndexOf("[[files]]", StringComparison.Ordinal)] + FileRow("original") + gates;
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse(path, text));
    }

    private PluginDefinition Package()
    {
        File.WriteAllText(Path.Combine(_folder, "original"), "original");
        File.WriteAllText(Path.Combine(_folder, "merged"), "merged");
        var path = Path.Combine(_folder, "plugin.toml");
        File.WriteAllText(path, "schema=1\n[plugin]\nid='fixture.font'\nversion='1.0.0'\ngame='fixture'\nkind='files'\nname={en='Font'}\n"
            + FileRow("original") + "client_languages=['zh-CN','zh-TW']\n"
            + FileRow("merged") + "exclude_client_languages=['zh-CN','zh-TW']\n");
        return PluginCatalog.LoadFile(path);
    }

    private string FileRow(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(_folder, name));
        return $"\n[[files]]\nsource='{name}'\ntarget='font.ttf'\nsize={bytes.Length}\nsha256='{Convert.ToHexStringLower(SHA256.HashData(bytes))}'\n";
    }

    private static BuildDefinition Build(string native) => new("fixture-1", "fixture", 1,
        new(new Dictionary<string, string> { ["en"] = "Fixture" }), "game.exe", "standin", "", null, null, "nmco", [], []) { NativeLanguage = native };

    public void Dispose() => Directory.Delete(_folder, true);
}
