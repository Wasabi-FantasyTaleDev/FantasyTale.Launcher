using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class StorageFailureTests
{
    [Theory]
    [InlineData(112, "space")]
    [InlineData(39, "space")]
    [InlineData(32, "busy")]
    [InlineData(33, "busy")]
    [InlineData(5, "access")]
    [InlineData(3, "disk")]
    public void LocalFailuresDoNotClaimFullDiskWithoutAnOperatingSystemFullDiskCode(int code, string expected)
    {
        var error = new IOException("private path", unchecked((int)0x80070000) | code);
        Assert.Equal(expected, StorageFailure.Code(error));
        Assert.Equal(expected, StorageFailure.Code(new IOException("wrapped", error)));
    }

    [Fact]
    public async Task CurrentComponentSelectionsRemainReadableWhileAnotherOperationOwnsTheInstance()
    {
        var repo = TestRepository.StartDirectory();
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        var game = GameCatalog.Load(Path.Combine(repo!.FullName, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "cn-324") with { PeTimestamp = null, SignerCertificateSha256 = null };
        game = game with { Builds = [build] };
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"))
            .Concat(PluginCatalog.Load(Path.Combine(repo.FullName, "store-plugins", "BubbleFighter"))).ToArray();
        var root = Directory.CreateTempSubdirectory("ft-storage-failure-");
        try
        {
            var store = new InstanceStore(root.FullName);
            var instance = await store.CreateAsync(new("probe", "Probe", game.Id, build.Id, null, PluginResolver.Defaults(game, build, packages)));
            Directory.CreateDirectory(store.ClientFolder(instance.Id));
            File.WriteAllBytes(Path.Combine(store.ClientFolder(instance.Id), build.Executable), [0]);
            _ = ClientPolicyActivation.Claim(store, instance.Id, game, packages);
            using var lease = store.Acquire(instance.Id);
            Assert.Empty(ClientPolicyActivation.Claim(store, instance.Id, game, packages));
            Assert.Equal(InstanceFile.Render(instance), InstanceFile.Render(FilePluginResolver.EnsureInstalled(store, instance.Id, game, build, packages)));
            Assert.Equal(InstanceFile.Render(instance), InstanceFile.Render(FilePluginResolver.RecoverInstalled(store, instance.Id, game, build, packages, false, out var recoveries)));
            Assert.Empty(recoveries);
            var error = Assert.Throws<InstanceBusyException>(() => store.Save(instance));
            Assert.Equal("instance_in_use", error.Message);
            Assert.Equal(instance.Id, error.InstanceId);
            Assert.Equal(store.ClientFolder(instance.Id), error.ClientPath);
            Assert.EndsWith(".lock", error.LockPath, StringComparison.Ordinal);
            Assert.Equal("busy", StorageFailure.Code(error));
            Assert.Equal("access", StorageFailure.Code(new UnauthorizedAccessException("private path")));
        }
        finally { root.Delete(true); }
    }
}
