using System.Runtime.InteropServices;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LauncherUpdatePublisherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-update-publisher-").FullName;

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("excluded")]
    public async Task MissingBundledNativePayloadBlocksPublicationUntilStaged(string failure)
    {
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(Path.Combine(input, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(input, "updater"));
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.Executable), "launcher");
        await File.WriteAllTextAsync(Path.Combine(input, "wwwroot/index.html"), "frontend");
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.UpdaterExecutable), "updater");
        var folder = Directory.CreateDirectory(Path.Combine(input, "games/Fixture/plugins/fixture.controls")).FullName;
        var library = failure == "excluded" ? "controls.pdb" : "controls.dll";
        var manifest = Path.Combine(folder, "plugin.toml");
        await File.WriteAllTextAsync(manifest,
            $"schema = 1\n[plugin]\nid = 'fixture.controls'\nversion = '1.0.0'\ngame = 'fixture'\nkind = 'native'\nruntime = '>=3.0.0 <4.0.0'\nname = {{ en = 'Controls' }}\n[compatibility]\nbuilds = ['*']\n[native]\nlibrary = '{library}'\n");
        if (failure != "missing") await File.WriteAllTextAsync(Path.Combine(folder, library), failure == "empty" ? "" : "excluded");
        var configuration = Path.Combine(_root, "publish.toml");
        await File.WriteAllTextAsync(configuration,
            "input = 'input'\noutput = 'public'\nplatform = 'win-x64'\nrevision = 1\nversion = '0.1.53'\n");

        var error = await Assert.ThrowsAsync<DefinitionException>(() => LauncherReleasePublisher.PublishAsync(configuration));
        Assert.Contains("native.library", error.Message, StringComparison.Ordinal);
        Assert.Contains("fixture.controls", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "public")));

        if (failure == "excluded") return;
        await File.WriteAllTextAsync(Path.Combine(folder, library), "staged native payload");
        var feed = await LauncherReleasePublisher.PublishAsync(configuration);
        var text = await File.ReadAllTextAsync(feed);
        Assert.Contains("games/Fixture/plugins/fixture.controls/controls.dll", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentDistributionSizedReadableIndexRetainsEveryFile()
    {
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(Path.Combine(input, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(input, "updater"));
        Directory.CreateDirectory(Path.Combine(input, "games/Fixture/assets/ranks"));
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.Executable), "launcher");
        await File.WriteAllTextAsync(Path.Combine(input, "wwwroot/index.html"), "frontend");
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.UpdaterExecutable), "updater");
        for (var index = 0; index < 997; index++)
            await File.WriteAllTextAsync(Path.Combine(input, $"games/Fixture/assets/ranks/rank-{index:D4}.png"), "rank");

        var configuration = Path.Combine(_root, "publish.toml");
        var platform = "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        await File.WriteAllTextAsync(configuration,
            $"input = 'input'\noutput = 'public'\nplatform = '{platform}'\nsource_prefix = 'FantasyTale/launcher/{platform}'\nrevision = 2\nversion = '0.1.22'\n");
        var indexPath = await LauncherReleasePublisher.PublishAsync(configuration);
        var manifest = await File.ReadAllBytesAsync(indexPath);
        Assert.True(manifest.Length > 256 * 1024);
        var text = System.Text.Encoding.UTF8.GetString(manifest);
        Assert.Contains("\n\n[[files]]\npath = \"", text, StringComparison.Ordinal);
        Assert.Contains("\nsource_path = \"", text, StringComparison.Ordinal);

        await File.WriteAllTextAsync(Path.Combine(input, LauncherUpdatePolicy.FileName),
            "kind = 'http'\nbase_url = 'https://updates.example/'\n");
        var policy = LauncherUpdatePolicy.Load(input)!;
        var release = LauncherRelease.Parse(policy, manifest);
        Assert.Equal(1000, release.Files.Count);
        Assert.Equal(997L * 4 + 23, release.Files.Sum(file => file.Size));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeprecationIsPublishedInTheIndexWithoutASeparatePolicy(bool retiredInput)
    {
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(Path.Combine(input, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(input, "updater"));
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.Executable), "launcher");
        await File.WriteAllTextAsync(Path.Combine(input, "wwwroot/index.html"), "frontend");
        await File.WriteAllTextAsync(Path.Combine(input, LauncherRelease.UpdaterExecutable), "updater");
        if (retiredInput) await File.WriteAllTextAsync(Path.Combine(input, "launcher-release-policy.toml"), "invalid");
        var configuration = Path.Combine(_root, "publish.toml");
        var platform = "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        await File.WriteAllTextAsync(configuration,
            $"input = 'input'\noutput = 'public'\nplatform = '{platform}'\nrevision = 2\nversion = '0.1.45'\ndeprecated_before_version = '0.1.39'\n");
        if (retiredInput)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(configuration));
            Assert.False(Directory.Exists(Path.Combine(_root, "public")));
            return;
        }
        var index = await LauncherReleasePublisher.PublishAsync(configuration);
        await File.WriteAllTextAsync(Path.Combine(input, LauncherUpdatePolicy.FileName),
            "kind = 'http'\nbase_url = 'https://updates.example/'\n");
        var release = LauncherRelease.Parse(LauncherUpdatePolicy.Load(input)!, await File.ReadAllBytesAsync(index));
        Assert.Equal("0.1.39", release.DeprecatedBeforeVersion);
        Assert.True(release.RequiresUpdate("0.1.38"));
        Assert.True(release.RequiresUpdate("0.1.39-rc.1"));
        Assert.False(release.RequiresUpdate("0.1.39"));
        // Launchers before 0.2.0 read the inclusive key, set to the lowest pre-release of the boundary.
        Assert.Contains("deprecated_max_version = \"0.1.39-0\"", await File.ReadAllTextAsync(index), StringComparison.Ordinal);
        Assert.Equal(3, release.Files.Count);
        Assert.DoesNotContain(release.Files, file => file.Path == "launcher-release-policy.toml");
    }

    [Fact]
    public async Task RetiredDeprecationTransportIsRejectedBeforePublication()
    {
        var configuration = Path.Combine(_root, "publish.toml");
        await File.WriteAllTextAsync(configuration,
            "input = 'input'\noutput = 'public'\nversion = '0.1.45'\ndeprecated_before_version = '0.1.38'\ndeprecation_transport = 'compatibility-file'\n");
        await Assert.ThrowsAsync<DefinitionException>(() => LauncherReleasePublisher.PublishAsync(configuration));
        Assert.False(Directory.Exists(Path.Combine(_root, "public")));
    }

    [Theory]
    [InlineData("signature_public_key_path = 'public.txt'")]
    [InlineData("signature_path = 'signed.sig'\nsignature_private_key_env = 'FT_UNUSED'")]
    [InlineData("signature_path = 'current.toml'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'public.txt'")]
    [InlineData("signature_path = 'CURRENT.TOML.tmp'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'public.txt'")]
    [InlineData("signature_path = 'signed.sig'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'SIGNED.sig'")]
    [InlineData("signature_path = 'signed.sig'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'signed.sig.tmp'")]
    [InlineData("signature_path = 'launcher/objects/content'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'public.txt'")]
    [InlineData("signature_path = 'signed.sig'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = '.fantasytale-signature.json'")]
    [InlineData("signature_path = 'signed.sig'\nsignature_private_key_env = 'FT_UNUSED'\nsignature_public_key_path = 'launcher'")]
    public async Task PartialSigningOrCollidingPublicationPathsFailBeforeCreatingOutput(string signing)
    {
        var configuration = Path.Combine(_root, "invalid.toml");
        await File.WriteAllTextAsync(configuration,
            "input = 'input'\noutput = 'public'\nfeed_path = 'current.toml'\nsource_prefix = 'launcher'\nrevision = 2\n" + signing);
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherReleasePublisher.PublishAsync(configuration));
        Assert.False(Directory.Exists(Path.Combine(_root, "public")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
