using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientRemovalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-removal-tests-").FullName;
    private static readonly LocalizedText Name = new(new Dictionary<string, string> { ["en"] = "Test" });
    private static readonly BuildDefinition Build = new("test-1", "test", 1, Name, "game.exe", "", "", null, null, "test", [], []);
    private static readonly GameDefinition Game = new("test", GameStatus.Enabled, Name, "test.dll", "external", "", null, "", [Build]);
    private static InstanceDefinition Definition(string id = "sample") => new(id, "Sample", "test", "test-1", null, []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateCleanupDeletesOnlyUnchangedListedFiles(bool external)
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        await store.CreateAtAsync(Definition(), external ? Path.Combine(_root, "game") : null);
        var client = store.ClientFolder("sample");
        await InstallAsync(client, ("game.exe", "game"), ("data/keep.bin", "original"), ("data/remove.bin", "data"));
        File.WriteAllText(Path.Combine(client, "data", "keep.bin"), "user modification");
        File.WriteAllText(Path.Combine(client, "personal.txt"), "personal");
        Directory.CreateDirectory(Path.Combine(client, "screenshots"));
        File.WriteAllText(Path.Combine(client, "screenshots", "photo.txt"), "photo");
        var sibling = Path.Combine(_root, "outside.txt"); File.WriteAllText(sibling, "outside");

        var result = await store.RemoveAsync("sample", true, Game);

        Assert.Equal(new ClientRemovalResult(2, 1), result);
        Assert.Empty(store.List());
        Assert.False(File.Exists(Path.Combine(client, "game.exe")));
        Assert.False(File.Exists(Path.Combine(client, "data", "remove.bin")));
        Assert.Equal("user modification", File.ReadAllText(Path.Combine(client, "data", "keep.bin")));
        Assert.Equal("personal", File.ReadAllText(Path.Combine(client, "personal.txt")));
        Assert.Equal("photo", File.ReadAllText(Path.Combine(client, "screenshots", "photo.txt")));
        Assert.Equal("outside", File.ReadAllText(sibling));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordOnlyPreservesEveryFileAndCanBeAddedAgain(bool external)
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        await store.CreateAtAsync(Definition(), external ? Path.Combine(_root, "game") : null);
        var client = store.ClientFolder("sample");
        File.WriteAllText(Path.Combine(client, "game.exe"), "existing");
        File.WriteAllText(Path.Combine(client, "personal.txt"), "personal");

        Assert.Equal(new ClientRemovalResult(0, 0), await store.RemoveAsync("sample", false, Game));
        Assert.Empty(store.List());
        Assert.True(store.InspectInstallationPath("new", client).ContainsFiles);
        var restored = await store.RegisterDetectedAsync(Definition("new"), client, Build);
        Assert.Equal(external ? "new" : "sample", restored.Id);
        Assert.Equal(client, store.ClientFolder(restored.Id));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(client, "game.exe")));
        Assert.Equal("personal", File.ReadAllText(Path.Combine(client, "personal.txt")));
        Assert.Single(store.List());
        await store.RemoveAsync(restored.Id, false, Game);
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task MissingInventoryRefusesFileCleanupButStillAllowsRecordOnly()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        await store.CreateAtAsync(Definition(), Path.Combine(_root, "game"));
        var path = Path.Combine(store.ClientFolder("sample"), "game.exe"); File.WriteAllText(path, "local import");
        var error = await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", true, Game));
        Assert.Equal("client_remove_inventory", error.Message);
        Assert.Single(store.List()); Assert.Equal("local import", File.ReadAllText(path));
        await store.RemoveAsync("sample", false, Game);
        Assert.Equal("local import", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("drive")]
    [InlineData("metadata")]
    [InlineData("ancestor")]
    [InlineData("profile")]
    [InlineData("launcher")]
    public async Task DangerousPathsAreRejectedBeforeAnyFileMutation(string location)
    {
        var metadata = Path.Combine(_root, "metadata"); var store = new InstanceStore(metadata);
        await store.CreateAsync(Definition());
        var sentinel = Path.Combine(store.ClientFolder("sample"), "keep.txt"); File.WriteAllText(sentinel, "keep");
        var path = location switch
        {
            "drive" => Path.GetPathRoot(_root)!, "metadata" => metadata, "ancestor" => _root,
            "profile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), _ => AppContext.BaseDirectory,
        };
        File.WriteAllText(Path.Combine(metadata, "sample", "instance.toml"), InstanceFile.Render(Definition() with { InstallationPath = path }));
        await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", true, Game));
        Assert.Single(store.List()); Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    [Fact]
    public async Task AllInventoryPathsAreValidatedBeforeTheFirstDeletion()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata")); await store.CreateAsync(Definition());
        var client = store.ClientFolder("sample"); await InstallAsync(client, ("game.exe", "game"));
        var cache = Path.Combine(client, ".fantasytale-install", "file-cache.json");
        var content = File.ReadAllText(cache);
        File.WriteAllText(cache, content[..^1] + ",{" + "\"Path\":\"../outside.txt\",\"Size\":1,\"Sha256\":\"" + new string('a', 64) + "\",\"Written\":0,\"Created\":0}]");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RemoveAsync("sample", true, Game));
        Assert.True(File.Exists(Path.Combine(client, "game.exe"))); Assert.Single(store.List());
    }

    [Fact]
    public async Task LinkedInventoryFileIsRejectedWithoutTouchingItsTarget()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata")); await store.CreateAsync(Definition());
        var client = store.ClientFolder("sample"); await InstallAsync(client, ("game.exe", "game"), ("data/file.bin", "data"));
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "file.bin"), "data");
        Directory.Delete(Path.Combine(client, "data"), true);
        // NTFS junctions work without the symbolic-link privilege required for file symlinks.
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { ArgumentList = { "/c", "mklink", "/J", Path.Combine(client, "data"), outside }, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }))
        { Assert.NotNull(process); await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
        try
        {
            await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", true, Game));
            Assert.True(File.Exists(Path.Combine(client, "game.exe")));
            Assert.Equal("data", File.ReadAllText(Path.Combine(outside, "file.bin"))); Assert.Single(store.List());
        }
        finally { Directory.Delete(Path.Combine(client, "data")); }
    }

    [Fact]
    public async Task LeaseAndLockedFilePreventRemovalAndRetainRegistration()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata")); await store.CreateAsync(Definition());
        var client = store.ClientFolder("sample"); await InstallAsync(client, ("game.exe", "game"));
        using (store.Acquire("sample"))
        {
            await Assert.ThrowsAsync<InstanceBusyException>(() => store.RemoveAsync("sample", true, Game));
            await Assert.ThrowsAsync<InstanceBusyException>(() => store.RemoveAsync("sample", false, Game));
        }
        using (File.Open(Path.Combine(client, "game.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", true, Game));
        Assert.Single(store.List()); Assert.True(File.Exists(Path.Combine(client, "game.exe")));
    }

    [Theory]
    [InlineData("session.toml")]
    [InlineData(".fantasytale-runtime")]
    [InlineData(".fantasytale-install/journal.json")]
    public async Task InterruptedWorkRetainsRecoveryEvidence(string relative)
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata")); await store.CreateAsync(Definition());
        var client = store.ClientFolder("sample"); await InstallAsync(client, ("game.exe", "game"));
        var evidence = Path.Combine(client, relative); Directory.CreateDirectory(Path.GetDirectoryName(evidence)!); File.WriteAllText(evidence, "pending");
        foreach (var deleteFiles in new[] { false, true })
        {
            var error = await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", deleteFiles, Game));
            Assert.Equal("client_remove_pending", error.Message);
        }
        Assert.Equal("pending", File.ReadAllText(evidence)); Assert.True(File.Exists(Path.Combine(client, "game.exe")));
    }

    [Fact]
    public async Task RunningGameStartedOutsideLauncherBlocksBothActions()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var client = store.ClientFolder("sample");
        var build = Build with { Executable = "FantasyTale.Launcher.TestClient.exe" };
        var game = Game with { Builds = [build] };
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(client, build.Executable))
        { WorkingDirectory = client, UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            foreach (var deleteFiles in new[] { false, true })
            {
                var error = await Assert.ThrowsAsync<IOException>(() => store.RemoveAsync("sample", deleteFiles, game));
                Assert.Equal("client_remove_running", error.Message);
            }
            Assert.Single(store.List()); Assert.False(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); }
    }

    private static async Task InstallAsync(string client, params (string Path, string Content)[] files)
    {
        var manifest = new List<ClientReleaseFile>();
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(client, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
            var bytes = File.ReadAllBytes(path); manifest.Add(new(relative, relative, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        await ClientInstallTransaction.ApplyAsync(client, manifest, _ => { }, CancellationToken.None, new("test", "test-client", "1.0.0", new string('a', 64)));
        new ClientFileCache(client).RecordInstalled(manifest);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
