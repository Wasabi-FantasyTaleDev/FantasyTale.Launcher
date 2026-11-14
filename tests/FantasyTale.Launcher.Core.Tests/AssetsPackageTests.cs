using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AssetsPackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-assets-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static string Manifest(string id, string kind = "assets", string version = "1.0.0", string extra = "", string requires = "", string builds = "\"*\"") =>
        $$"""
        schema = 1
        [plugin]
        id = "{{id}}"
        version = "{{version}}"
        game = "fixture"
        kind = "{{kind}}"
        {{requires}}
        name = { en = "{{id}}" }
        [compatibility]
        builds = [{{builds}}]
        {{extra}}
        """;

    private const string AssetsTable = "[assets]\nroot = \"data\"";
    private const string Patch = "[[patches]]\nid = \"p\"\nlocate = { pattern = \"AA BB\" }\nexpect = \"AA\"\nwrite = \"90\"";

    /// <summary>Parses a manifest located in its own folder, optionally creating its data folder with one file.</summary>
    private PluginDefinition Package(string text, bool data = true)
    {
        var folder = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        if (data) { Directory.CreateDirectory(Path.Combine(folder, "data", "atlas")); File.WriteAllText(Path.Combine(folder, "data", "pack.json"), "{}"); }
        return PluginCatalog.Parse(Path.Combine(folder, "plugin.toml"), text);
    }

    private BuildDefinition Build()
    {
        var folder = Path.Combine(_root, "games-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Fixture", "builds"));
        File.WriteAllText(Path.Combine(folder, "Fixture", "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
        File.WriteAllText(Path.Combine(folder, "Fixture", "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
        return GameCatalog.Load(folder).Single().Builds.Single();
    }

    private static InstanceDefinition Instance(BuildDefinition build, params PluginDefinition[] enabled) =>
        new("fixture-client", "Fixture", "fixture", build.Id, null,
            enabled.Select(p => new InstancePlugin(p.Id, p.Version, true, false, new Dictionary<string, object>())).ToArray());

    [Fact]
    public void AssetsManifestDeclaresOnlyItsDataFolder()
    {
        var package = Package(Manifest("fixture.previews", extra: AssetsTable));
        Assert.Equal("assets", package.Kind);
        Assert.Equal("data", package.Assets!.Root);
        Assert.Empty(package.Requires);
        Assert.Equal(Path.Combine(package.Folder, "data"), PluginRequirements.AssetsFolder(package));
    }

    [Theory]
    [InlineData("[assets]\nroot = \"../data\"")]
    [InlineData("[assets]\nroot = \"/data\"")]
    [InlineData("[assets]\nroot = \"data\\\\x\"")]
    [InlineData("[assets]\nroot = \"data/./x\"")]
    [InlineData("[assets]\nroot = \"\"")]
    [InlineData("[assets]\nroot = \"data\"\nlink = true")]
    [InlineData("")]
    [InlineData("[assets]\nroot = \"data\"\n[[options]]\nkey = \"a\"\ntype = \"bool\"\ndefault = true\nlabel = { en = \"A\" }")]
    [InlineData("[assets]\nroot = \"data\"\n[native]\nlibrary = \"a.dll\"")]
    [InlineData("[assets]\nroot = \"data\"\n[overlay]\nentry = \"a.html\"")]
    [InlineData("[assets]\nroot = \"data\"\n[service]\nprovider = \"companion\"")]
    [InlineData("[assets]\nroot = \"data\"\n[companion.messages]\n\"a.b\" = 1")]
    public void AssetsManifestRejectsAnythingButADataFolder(string extra) =>
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml", Manifest("fixture.previews", extra: extra)));

    [Fact]
    public void AssetsTableBelongsToAssetsPackagesOnly() =>
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml", Manifest("fixture.other", kind: "overlay", extra: "[overlay]\nentry = \"a.html\"\n" + AssetsTable)));

    [Theory]
    [InlineData("requires = [\"fixture.previews\"]", "fixture.previews", "")]
    [InlineData("requires = [\"fixture.previews@>=1.2.0\"]", "fixture.previews", ">=1.2.0")]
    [InlineData("requires = [\"fixture.previews@>=1.2.0  <2.0.0\"]", "fixture.previews", ">=1.2.0 <2.0.0")]
    public void RequirementsAcceptAnIdOrAnIdWithAVersionConstraint(string line, string id, string range)
    {
        var requirement = Assert.Single(Package(Manifest("fixture.user", kind: "patch", requires: line, extra: Patch), false).Requires);
        Assert.Equal(new PluginRequirement(id, range), requirement);
    }

    [Theory]
    [InlineData("requires = [\"Fixture.Previews\"]")]
    [InlineData("requires = [\"fixture.previews@\"]")]
    [InlineData("requires = [\"fixture.previews@latest\"]")]
    [InlineData("requires = [\"fixture.previews@>=1.2\"]")]
    [InlineData("requires = [\"fixture.user\"]")]
    [InlineData("requires = [\"fixture.previews\", \"fixture.previews@>=1.0.0\"]")]
    [InlineData("requires = [\"../x\"]")]
    public void RequirementsRejectBadIdentitiesConstraintsSelvesAndDuplicates(string line) =>
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml", Manifest("fixture.user", kind: "patch", requires: line, extra: Patch)));

    [Fact]
    public void AssetsPackagesNeverReachTheNativeSession()
    {
        var build = Build();
        var previews = Package(Manifest("fixture.previews", extra: AssetsTable));
        Assert.Empty(PluginResolver.Resolve(Instance(build, previews), build, [previews]));
    }

    [Fact]
    public void StorePackageInThisRepositoryIsADataOnlyShellWithoutCommittedArt()
    {
        var folder = Path.Combine(TestRepository.StartDirectory().FullName, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.item-previews");
        var package = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        Assert.Equal("assets", package.Kind);
        Assert.Equal("data", package.Assets!.Root);
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        Assert.Equal(["en", "id", "ko", "vi", "zh-CN", "zh-TW"], package.Name.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(package.Name.Values.Keys.Order(StringComparer.Ordinal), package.Description!.Values.Keys.Order(StringComparer.Ordinal));
        var text = string.Concat(package.Name.Values.Values) + string.Concat(package.Description.Values.Values);
        Assert.DoesNotContain("overlay", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("atlas", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchAcceptsAnAssetsRequirementWithoutAnExplicitSelectionAndSharesOneCopy()
    {
        var build = Build();
        var previews = Package(Manifest("fixture.previews", extra: AssetsTable));
        var first = Package(Manifest("fixture.first", kind: "patch", requires: "requires = [\"fixture.previews@>=1.0.0\"]", extra: Patch), false);
        var second = Package(Manifest("fixture.second", kind: "patch", requires: "requires = [\"fixture.previews\"]", extra: Patch), false);
        var needed = PluginRequirements.RequireSatisfied(Instance(build, first, second), build, [previews, first, second]);
        Assert.Same(previews, Assert.Single(needed));
        Assert.Empty(PluginRequirements.RequireSatisfied(Instance(build), build, [previews, first, second]));
    }

    [Fact]
    public void LaunchFailsLoudlyForMissingIncompatibleOrUnavailableRequirements()
    {
        var build = Build();
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews@>=2.0.0\"]", extra: Patch), false);
        var missing = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user]));
        Assert.Contains("'fixture.user' requires 'fixture.previews@>=2.0.0', which is not installed", missing.Message, StringComparison.Ordinal);
        var old = Package(Manifest("fixture.previews", extra: AssetsTable));
        var tooOld = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, old]));
        Assert.Contains("only version 1.0.0 is installed", tooOld.Message, StringComparison.Ordinal);
        var other = Package(Manifest("fixture.previews", version: "2.0.0", extra: AssetsTable, builds: "\"elsewhere\""));
        var unavailable = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, other]));
        Assert.Contains("not available for test-1", unavailable.Message, StringComparison.Ordinal);
        var newer = Package(Manifest("fixture.previews", version: "2.1.0", extra: AssetsTable));
        Assert.Same(newer, Assert.Single(PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, old, newer])));
    }

    [Fact]
    public void LaunchFailsWhenTheDataFolderIsMissing()
    {
        var build = Build();
        var hollow = Package(Manifest("fixture.previews", extra: AssetsTable), data: false);
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews\"]", extra: Patch), false);
        var error = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, hollow]));
        Assert.Contains("data folder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequiredPackageOfAnotherKindMustBeEnabledItself()
    {
        var build = Build();
        var baseline = Package(Manifest("fixture.base", kind: "patch", extra: Patch), false);
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.base\"]", extra: Patch), false);
        var error = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, baseline]));
        Assert.Contains("which must also be enabled", error.Message, StringComparison.Ordinal);
        Assert.Empty(PluginRequirements.RequireSatisfied(Instance(build, user, baseline), build, [user, baseline]));
    }

    [Fact]
    public void RequirementsChainThroughAssetsPackagesAndRejectCycles()
    {
        var build = Build();
        var shared = Package(Manifest("fixture.shared", extra: AssetsTable));
        var previews = Package(Manifest("fixture.previews", requires: "requires = [\"fixture.shared\"]", extra: AssetsTable));
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews\"]", extra: Patch), false);
        Assert.Equal(["fixture.previews", "fixture.shared"], PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, previews, shared]).Select(p => p.Id).Order(StringComparer.Ordinal));
        var loop = Package(Manifest("fixture.shared", requires: "requires = [\"fixture.previews\"]", extra: AssetsTable));
        Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, user), build, [user, previews, loop]));
    }

    [Fact]
    public void OverlayPackagesServeTheirOwnFolderAndTheAssetsTheyRequire()
    {
        var build = Build();
        var previews = Package(Manifest("fixture.previews", extra: AssetsTable));
        var unused = Package(Manifest("fixture.unused", extra: AssetsTable));
        var overlay = Package(Manifest("fixture.panel", kind: "overlay", requires: "requires = [\"fixture.previews\"]", extra: "[overlay]\nentry = \"panel.html\""), false);
        var patch = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.unused\"]", extra: Patch), false);
        var folders = PluginRequirements.OverlayPackageFolders(Instance(build, overlay, patch), build, [previews, unused, overlay, patch]);
        Assert.Equal(overlay.Folder, folders["fixture.panel"]);
        Assert.Equal(Path.Combine(previews.Folder, "data"), folders["fixture.previews"]);
        Assert.Equal(2, folders.Count);
        Assert.Empty(PluginRequirements.OverlayPackageFolders(Instance(build), build, [previews, overlay]));
    }

    [Fact]
    public void DependentsAreFoundByIdentityAndFormerIdentity()
    {
        var previews = Package(Manifest("fixture.previews", extra: AssetsTable).Replace("kind = \"assets\"", "kind = \"assets\"\naliases = [\"fixture.old-previews\"]", StringComparison.Ordinal));
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.old-previews\"]", extra: Patch), false);
        var bystander = Package(Manifest("fixture.bystander", kind: "patch", extra: Patch), false);
        Assert.Same(user, Assert.Single(PluginRequirements.Dependents(previews, [previews, user, bystander])));
    }

    private static StorePackage Offer(PluginDefinition definition) =>
        new(definition.Id, "plugin", definition.Game, definition.Builds.Contains("*") ? [] : definition.Builds, definition.Version, definition.Name.Values,
            new Dictionary<string, string>(), "Fixture", 1, "m.toml", new string('0', 64)) { PluginDefinition = definition };

    [Fact]
    public void StoreInstallPlanListsUnmetRequirementsFromTheSameCatalogDependenciesFirst()
    {
        var shared = Package(Manifest("fixture.shared", extra: AssetsTable), false);
        var previews = Package(Manifest("fixture.previews", version: "1.2.0", requires: "requires = [\"fixture.shared\"]", extra: AssetsTable), false);
        var previewsOld = Package(Manifest("fixture.previews", version: "1.0.0", extra: AssetsTable), false);
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews@>=1.1.0\"]", extra: Patch), false);
        var catalog = new StoreCatalog("c", new Dictionary<string, string>(), [Offer(user), Offer(previewsOld), Offer(previews), Offer(shared)]);
        Assert.Equal(["fixture.shared@1.0.0", "fixture.previews@1.2.0"],
            PluginRequirements.PlanStoreInstall(catalog, Offer(user), []).Select(p => p.Id + "@" + p.Version));
        Assert.Equal(["fixture.previews@1.2.0"],
            PluginRequirements.PlanStoreInstall(catalog, Offer(user), [shared]).Select(p => p.Id + "@" + p.Version));
        Assert.Empty(PluginRequirements.PlanStoreInstall(catalog, Offer(user), [previews, shared]));
        Assert.Empty(PluginRequirements.PlanStoreInstall(catalog, Offer(shared), []));
    }

    [Fact]
    public void StoreInstallPlanReportsTheMissingRequirementAndCycles()
    {
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews@>=3.0.0\"]", extra: Patch), false);
        var previews = Package(Manifest("fixture.previews", version: "1.0.0", extra: AssetsTable), false);
        var catalog = new StoreCatalog("c", new Dictionary<string, string>(), [Offer(user), Offer(previews)]);
        var error = Assert.Throws<InvalidOperationException>(() => PluginRequirements.PlanStoreInstall(catalog, Offer(user), []));
        Assert.Contains("'fixture.user' requires 'fixture.previews@>=3.0.0'", error.Message, StringComparison.Ordinal);
        var a = Package(Manifest("fixture.a", requires: "requires = [\"fixture.b\"]", extra: AssetsTable), false);
        var b = Package(Manifest("fixture.b", requires: "requires = [\"fixture.a\"]", extra: AssetsTable), false);
        var loop = new StoreCatalog("c", new Dictionary<string, string>(), [Offer(a), Offer(b)]);
        Assert.Throws<InvalidOperationException>(() => PluginRequirements.PlanStoreInstall(loop, Offer(a), []));
    }

    [Fact]
    public void StoreInstallPlanIgnoresReleasesForOtherBuildsAndNewerLaunchers()
    {
        var user = Package(Manifest("fixture.user", kind: "patch", requires: "requires = [\"fixture.previews\"]", extra: Patch, builds: "\"test-1\""), false);
        var elsewhere = Offer(Package(Manifest("fixture.previews", extra: AssetsTable, builds: "\"other\""), false));
        var tooNew = Offer(Package(Manifest("fixture.previews", version: "1.1.0", extra: AssetsTable), false)) with { MinimumLauncherVersion = "999.0.0" };
        var catalog = new StoreCatalog("c", new Dictionary<string, string>(), [Offer(user), elsewhere, tooNew]);
        Assert.Throws<InvalidOperationException>(() => PluginRequirements.PlanStoreInstall(catalog, Offer(user), []));
    }
}
