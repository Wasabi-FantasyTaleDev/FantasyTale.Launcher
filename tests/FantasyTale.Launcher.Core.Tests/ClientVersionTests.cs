using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientVersionTests
{
    [Theory]
    [InlineData("cn-324", "0.2.25.004", "")]
    [InlineData("cn-717", "0.2.61.001", "")]
    [InlineData("kr-812", "", "458")]
    [InlineData("kr-1256", "", "1304")]
    [InlineData("id-400", "", "113")]
    [InlineData("id-1809", "", "133")]
    [InlineData("tw-454", "", "369")]
    public void BundledProfilesCarryVerifiedDistributionLabels(string id, string release, string patch)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "games", "BubbleFighter", "builds"))) root = root.Parent;
        Assert.NotNull(root);
        var build = GameCatalog.LoadBuild(Path.Combine(root.FullName, "games", "BubbleFighter", "builds", id + ".toml"), "bubblefighter");
        Assert.Equal(release, build.ReleaseVersion);
        Assert.Equal(patch, build.PatchVersion);
    }

    internal static BuildDefinition Build() => GameCatalog.ParseBuild("kr-1256-2.toml", """
        schema = 1
        [build]
        id = "kr-1256-2"
        game = "bubblefighter"
        branch = "kr"

        protocol_version = 1256
        executable = "BubbleFighter.exe"
        release_version = "profile-release"
        patch_version = "profile-patch"
        name = { en = "Fixture" }
        [launch]
        login = "hook"
        arguments = []
        """, "bubblefighter");

    [Theory]
    [InlineData("[PATCH] \r\nVERSION=458 \r\n", "profile-release", "458")]
    [InlineData("[Common]\r\nVersion=0.2.61.001\r\n[DownOnlyFlag]\r\nPatchFlag=0\r\nComplete=0", "0.2.61.001", "0.2.61.001")]
    [InlineData("[Common]\nVersion=1.2.3\n[PATCH]\nVERSION=1304", "1.2.3", "1304")]
    [InlineData("[OTHER]\nVERSION=99\n[PATCH]\nVERSION=bad", "profile-release", "profile-patch")]
    [InlineData("", "profile-release", "profile-patch")]
    public void OptionalVersionFileUsesSectionSpecificLabels(string content, string release, string patch)
    {
        var directory = Directory.CreateTempSubdirectory("ft-client-version-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "Version.dat"), content);
            var metadata = new BubbleFighterModule().ReadVersionMetadata(Build(), directory.FullName);
            Assert.Equal(new ClientVersionMetadata(release, patch), metadata);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void MissingOversizedAndLockedFilesDoNotPreventLaunchMetadataResolution()
    {
        var directory = Directory.CreateTempSubdirectory("ft-client-version-");
        try
        {
            var module = new BubbleFighterModule();
            var build = Build();
            var expected = new ClientVersionMetadata("profile-release", "profile-patch");
            Assert.Equal(expected, module.ReadVersionMetadata(build, directory.FullName));
            Assert.Equal(expected, module.ReadVersionMetadata(build, Path.Combine(directory.FullName, "missing")));
            var file = Path.Combine(directory.FullName, "version.dat");
            File.WriteAllText(file, "[PATCH]\nVERSION=1304\n" + new string(' ', 16384));
            Assert.Equal(expected, module.ReadVersionMetadata(build, directory.FullName));
            using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(expected, module.ReadVersionMetadata(build, directory.FullName));
        }
        finally { directory.Delete(true); }
    }
}
