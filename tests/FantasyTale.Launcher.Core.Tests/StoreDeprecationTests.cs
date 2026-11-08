using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreTests
{
    private const string Retirement = """
        [[deprecated_plugins]]
        id = "fixture.retired"
        game = "fixture"
        name = { en = "Retired plugin", zh-CN = "已弃用插件" }
        reason = { en = "No longer needed" }
        """;

    [Fact]
    public void RetirementTombstonesRemainAvailableWithoutPackageRows()
    {
        var catalog = StoreCatalogFile.Parse("store", "schema=2\n[catalog]\nid=\"fixture\"\nname={en=\"Fixture\"}\n" + Retirement);
        Assert.Empty(catalog.Packages);
        Assert.Equal("fixture.retired", Assert.Single(catalog.DeprecatedPlugins).Id);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("store", Catalog + "\n" + Retirement));
    }

    [Theory]
    [InlineData("id = \"fixture.retired\"", "id = \"../invalid\"")]
    [InlineData("game = \"fixture\"", "game = \"../invalid\"")]
    [InlineData("name = { en = \"Retired plugin\", zh-CN = \"已弃用插件\" }", "name = { en = \"\" }")]
    public void RetirementMetadataIsValidatedBeforeAnyRemoval(string before, string after)
    {
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("store", "schema=2\n[catalog]\nid=\"fixture\"\nname={en=\"Fixture\"}\n"
            + Retirement.Replace(before, after, StringComparison.Ordinal)));
    }
}
