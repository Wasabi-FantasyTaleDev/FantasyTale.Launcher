using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DuplicateInstallationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-duplicate-installation-").FullName;

    private InstanceStore Seed()
    {
        var root = Path.Combine(_root, "instances");
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        File.WriteAllText(Path.Combine(client, "game.bin"), "unchanged game files");
        foreach (var id in new[] { "first", "selected" })
        {
            var metadata = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
            var definition = new InstanceDefinition(id, id, "fixture", "test-1", null, []) { InstallationPath = client };
            File.WriteAllText(Path.Combine(metadata, "instance.toml"), InstanceFile.Render(definition));
            File.WriteAllText(Path.Combine(metadata, "history.txt"), id);
        }
        return new InstanceStore(root);
    }

    [Fact]
    public void ConsolidationPreservesSelectedSettingsAndArchivesMetadataWithoutTouchingGameFiles()
    {
        var store = Seed();
        store.ConsolidateRegistrations("selected");
        Assert.Equal("selected", Assert.Single(store.List()).Id);
        var archive = Assert.Single(Directory.GetFiles(Path.Combine(_root, "instances", ".duplicates"), "history.txt", SearchOption.AllDirectories));
        Assert.Equal("first", File.ReadAllText(archive));
        Assert.Equal("unchanged game files", File.ReadAllText(Path.Combine(store.ClientFolder("selected"), "game.bin")));
        store.ConsolidateRegistrations();
        Assert.Single(store.List());
    }

    [Fact]
    public void DuplicateIdsShareOnePhysicalLeaseAndBusyGroupsAreNotMoved()
    {
        var store = Seed();
        using (store.Acquire("first"))
        {
            Assert.Throws<InstanceBusyException>(() => store.Acquire("selected"));
            store.ConsolidateRegistrations("selected");
            Assert.Equal(2, store.List().Count);
        }
        store.ConsolidateRegistrations("selected", id => id == "first");
        Assert.Equal(2, store.List().Count);
        store.ConsolidateRegistrations("selected");
        Assert.Single(store.List());
    }

    [Fact]
    public async Task SeparatePhysicalCopiesRemainSeparateClients()
    {
        var store = Seed();
        await store.CreateAtAsync(new("copy", "Copy", "fixture", "test-1", null, []), Path.Combine(_root, "independent"));
        store.ConsolidateRegistrations("selected");
        Assert.Equal(2, store.List().Count);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
