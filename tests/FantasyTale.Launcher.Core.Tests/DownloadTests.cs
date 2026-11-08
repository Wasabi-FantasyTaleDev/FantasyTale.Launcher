using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DownloadTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-transfer-").FullName;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static ResolvedDownload Source(long size) => new(new Uri("https://storage.example/file"), new Dictionary<string, string>(), size);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorruptedResumePrefixIsCheckedUnlessStorageRestartsTheWholeTransfer(bool supportsRange)
    {
        var data = RandomNumberGenerator.GetBytes(400000);
        var destination = Path.Combine(_root, "download");
        var partial = destination + "." + Hash(data) + ".part";
        await File.WriteAllBytesAsync(partial, new byte[12345]);
        using var handler = new TransferHandler(data) { SupportsRange = supportsRange };
        using var http = new HttpClient(handler);
        var engine = new HttpDownloadEngine(http);
        if (supportsRange)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.DownloadAsync(new(Source(data.Length), destination, data.Length, Hash(data)), null, default));
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(partial));
        }
        else
        {
            await engine.DownloadAsync(new(Source(data.Length), destination, data.Length, Hash(data)), null, default);
            Assert.Equal(data, await File.ReadAllBytesAsync(destination));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RangeResumeAndRangeIgnoredBothVerifyTheWholeFile(bool supportsRange)
    {
        var data = RandomNumberGenerator.GetBytes(400000); var destination = Path.Combine(_root, "download");
        await File.WriteAllBytesAsync(destination + "." + Hash(data) + ".part", data[..12345]);
        using var handler = new TransferHandler(data) { SupportsRange = supportsRange }; using var http = new HttpClient(handler);
        await new HttpDownloadEngine(http).DownloadAsync(new(Source(data.Length), destination, data.Length, Hash(data)), null, default);
        Assert.Equal(12345, handler.LastOffset); Assert.Equal(data, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.part"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("overflow")]
    [InlineData("range")]
    public async Task InvalidStorageNeverReplacesExistingDestination(string failure)
    {
        var expected = Encoding.UTF8.GetBytes("expected payload"); var destination = Path.Combine(_root, "installed");
        await File.WriteAllTextAsync(destination, "original");
        var sent = failure == "hash" ? new byte[expected.Length] : failure == "overflow" ? new byte[expected.Length + 1] : expected;
        using var handler = new TransferHandler(sent) { InvalidRange = failure == "range" }; using var http = new HttpClient(handler);
        if (failure == "range") await File.WriteAllBytesAsync(destination + "." + Hash(expected) + ".part", expected[..3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpDownloadEngine(http).DownloadAsync(new(Source(expected.Length), destination, expected.Length, Hash(expected)), null, default));
        Assert.Equal("original", await File.ReadAllTextAsync(destination)); Assert.Empty(Directory.GetFiles(_root, "*.part"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Data/CON.txt")]
    [InlineData("Data/name.")]
    [InlineData("session.toml")]
    [InlineData(".fantasytale-runtime/anything")]
    [InlineData("Data/COM¹.dat")]
    public void ReleaseRejectsFilesystemAliasesAndLauncherMetadata(string path)
        => Assert.ThrowsAny<Exception>(() => ClientRelease.ValidatePath(path));

    [Fact]
    public void ManifestPinsExactBytesIdentityTotalsAndCaseInsensitivePaths()
    {
        var bytes = Manifest("client.exe"); var package = Package(bytes, 3);
        Assert.Single(ClientRelease.Parse(package, bytes));
        Assert.Throws<InvalidDataException>(() => ClientRelease.Parse(package, [.. bytes, 32]));
        Assert.ThrowsAny<Exception>(() => ClientRelease.Parse(package with { Version = "different" }, bytes));
        Assert.ThrowsAny<Exception>(() => ClientRelease.Parse(package with { Size = 4 }, bytes));
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + "\n[[files]]\npath = \"CLIENT.EXE\"\nsource_path = \"data/second\"\nsize = 3\nsha256 = \"" + Hash([1, 2, 3]) + "\"\n");
        Assert.ThrowsAny<Exception>(() => ClientRelease.Parse(Package(duplicate, 6), duplicate));
    }

    [Fact]
    public void ArchiveManifestUsesOneTransportModeAndKeepsPasswordOptional()
    {
        const string text = """
            schema = 1
            [release]
            id = "fixture.client"
            game = "fixture"
            build = "test-1"
            version = "1"
            format = "archive"
            archive_format = "zip"
            password = "fixture-password"
            [[files]]
            path = "client.part1.zip"
            source_path = "client.part1.zip"
            size = 3
            sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            volume = 1
            [[files]]
            path = "client.part2.zip"
            source_path = "client.part2.zip"
            size = 4
            sha256 = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
            volume = 2
            """;
        var bytes = Encoding.UTF8.GetBytes(text);
        var package = new StorePackage("fixture.client", "client", "fixture", ["test-1"], "1",
            new Dictionary<string, string> { ["en"] = "Fixture" }, new Dictionary<string, string>(), "Fixture", 7,
            "manifest.toml", Hash(bytes));
        var files = ClientRelease.Parse(package, bytes);
        Assert.Equal([1, 2], files.Select(file => file.ArchiveVolume));
        Assert.All(files, file => Assert.Equal("zip", file.ArchiveFormat));
        Assert.All(files, file => Assert.Equal("fixture-password", file.ArchivePassword));
    }

    [Fact]
    public async Task ZipArchiveExtractsToVerifiedContentObjectsBeforeInstall()
    {
        var client = Path.Combine(_root, "archive-client"); Directory.CreateDirectory(client);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("BubbleFighter.exe").Open()) entry.Write("client"u8);
            using (var data = archive.CreateEntry("Data/settings.ini").Open()) data.Write("settings"u8);
        }
        var archiveBytes = buffer.ToArray(); var archiveHash = Hash(archiveBytes);
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(client));
        await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(client, archiveHash), archiveBytes);
        var volume = new ClientReleaseFile("client.zip", "client.zip", archiveBytes.Length, archiveHash, false, "zip", null, 1);
        var expanded = await ClientArchiveExtractor.ExtractAsync(client, [volume], null, _ => { }, default);
        Assert.Equal(["BubbleFighter.exe", "Data/settings.ini"], expanded.Select(file => file.Path));
        Assert.Equal("client", await File.ReadAllTextAsync(ClientInstallTransaction.Staged(client, expanded[0].Sha256)));
        Assert.Equal("settings", await File.ReadAllTextAsync(ClientInstallTransaction.Staged(client, expanded[1].Sha256)));
    }

    [Fact]
    public async Task SevenZipArchiveUsesTheSameExtractionPath()
    {
        var client = Path.Combine(_root, "sevenzip-client"); Directory.CreateDirectory(client);
        // Independently generated 7-Zip fixture containing only a synthetic text file.
        var archiveBytes = Convert.FromBase64String("N3q8ryccAASynZm3DQAAAAAAAABqAAAAAAAAAAgbZ20BAAhjbGllbnQtN3oAAQQGAAEJDQAHCwEAASEhAQAMCQAICgGmmKmQAAAFARkMAAAAAAAAAAAAAAAAESUAQgB1AGIAYgBsAGUARgBpAGcAaAB0AGUAcgAuAGUAeABlAAAAFAoBAOzW5l1WYd0BFQYBACAAAAAAAA=="); var archiveHash = Hash(archiveBytes);
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(client));
        await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(client, archiveHash), archiveBytes);
        var volume = new ClientReleaseFile("client.7z", "client.7z", archiveBytes.Length, archiveHash, false, "7z", null, 1);
        var expanded = await ClientArchiveExtractor.ExtractAsync(client, [volume], null, _ => { }, default);
        Assert.Equal("BubbleFighter.exe", Assert.Single(expanded).Path);
        Assert.Equal("client-7z", await File.ReadAllTextAsync(ClientInstallTransaction.Staged(client, expanded[0].Sha256)));
    }

    [Fact]
    public async Task InstallationRollsBackAllPreviousReplacementsOnFailure()
    {
        var files = await StageAsync();
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(_root, files, _ => throw new IOException("fixture interruption"), default));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "first.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "second.bin")));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(_root), "journal.json")));
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(_root, "first.bin")));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(_root, "second.bin")));
    }

    [Fact]
    public async Task RecoveryKeepsExternalChangesAndOriginalBackups()
    {
        var files = await StageAsync();
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(_root, files, _ =>
        { File.WriteAllText(Path.Combine(_root, "first.bin"), "external edit"); throw new IOException("fixture interruption"); }, default));
        Assert.Equal("external edit", await File.ReadAllTextAsync(Path.Combine(_root, "first.bin")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(ClientInstallTransaction.Workspace(_root), "original-0")));
        Assert.True(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(_root), "journal.json")));
    }

    [Fact]
    public async Task IdleMaintenanceExcludesNewWorkersAndSkipsActiveTransfers()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        var payload = new byte[256000];
        using var http = new HttpClient(new TransferHandler(payload) { Slow = true });
        await using var service = Service(store, http, new CountingSource(payload.Length));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var enqueueEntered = new ManualResetEventSlim();
        var maintenance = Task.Run(() => service.TryRunWhileIdle(() =>
        {
            using var lease = store.Acquire("fixture");
            entered.Set();
            return release.Wait(TimeSpan.FromSeconds(5));
        }));
        Task<ClientDownloadTask>? enqueue = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var manifest = Manifest("client.exe", payload);
            enqueue = Task.Run(() =>
            {
                enqueueEntered.Set();
                return service.Start(Definition, Package(manifest, payload.Length), manifest, "fixture");
            });
            Assert.True(enqueueEntered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(enqueue, await Task.WhenAny(enqueue, Task.Delay(100)));
        }
        finally { release.Set(); }
        Assert.True(await maintenance);
        Assert.NotNull(enqueue);
        await enqueue;
        var invoked = false;
        Assert.False(service.TryRunWhileIdle(() => invoked = true));
        Assert.False(invoked);
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage);
        await WaitAsync(() => service.TryRunWhileIdle(() => true));
    }

    [Fact]
    public async Task RealTasksPauseResumeResolveAgainAndInstallUnderTheClientLease()
    {
        var data = RandomNumberGenerator.GetBytes(256 * 1024); var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var handler = new TransferHandler(data) { Slow = true }; using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var source = new CountingSource(data.Length);
        await using var service = Service(store, http, source);
        var manifest = Manifest("client.exe", data);
        var package = Package(manifest, data.Length);
        var job = service.Start(Definition, package, manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage == "downloading" && service.List()[0].Files[0].Received > 0);
        Assert.Throws<InstanceBusyException>(() => store.Acquire("fixture"));
        await service.ActionAsync(job.Id, "pause"); Assert.Equal("paused", service.List()[0].Stage);
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
        await service.ActionAsync(job.Id, "resume");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage); Assert.Equal(2, source.Resolutions); Assert.True(handler.LastOffset > 0);
        using (store.Acquire("fixture")) { }
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
        Assert.All(service.List()[0].Files, file => Assert.Equal("verified", file.Stage));
        var receipt = ClientInstallTransaction.ReadReceipt(store.ClientFolder("fixture"));
        Assert.Equal("fixture", receipt!.SourceId); Assert.Equal("fixture.client", receipt.PackageId); Assert.Equal("1", receipt.Version);
        await service.DisposeAsync();
        await File.WriteAllTextAsync(Path.Combine(ClientInstallTransaction.Workspace(store.ClientFolder("fixture")), "partial-fixture"), "not an installed game file");
        await store.CloneAsync("fixture", "copy", "Copy");
        Assert.Equal(receipt, ClientInstallTransaction.ReadReceipt(store.ClientFolder("copy")));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(store.ClientFolder("copy")), "partial-fixture")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartResumesInterruptedTransfersButPreservesExplicitPause(bool pause)
    {
        var data = RandomNumberGenerator.GetBytes(300000); var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var handler = new TransferHandler(data) { Slow = true }; using var http = new HttpClient(handler);
        var source = new CountingSource(data.Length); var manifest = Manifest("client.exe", data); string id;
        await using (var first = Service(store, http, source))
        {
            id = first.Start(Definition, Package(manifest, data.Length), manifest, "fixture").Id;
            await WaitAsync(() => first.List()[0].Files[0].Received > 0);
            if (pause) await first.ActionAsync(id, "pause");
        }
        var records = Directory.GetFiles(Path.Combine(_root, "tasks"), "*.json");
        Assert.Single(records); var saved = await File.ReadAllTextAsync(records[0]);
        Assert.DoesNotContain("https://", saved); Assert.DoesNotContain("raw_url", saved);
        await using (var reopened = Service(store, http, source))
        {
            Assert.Equal(id, reopened.List()[0].Id);
            if (pause)
            {
                await Task.Delay(100); Assert.Equal("paused", reopened.List()[0].Stage); Assert.Equal(1, source.Resolutions);
                await reopened.ActionAsync(id, "resume");
            }
            await WaitAsync(() => reopened.List()[0].Stage is "completed" or "failed");
            Assert.Equal("completed", reopened.List()[0].Stage); Assert.True(handler.LastOffset > 0);
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
        }
        await using (var history = Service(store, http, source))
        {
            Assert.Equal("completed", history.List()[0].Stage); Assert.Equal(2, source.Resolutions);
            history.ClearFinished(); Assert.Empty(history.List());
        }
        await using var empty = Service(store, http, source); Assert.Empty(empty.List());
    }

    [Fact]
    public async Task ThirdClientQueuesAndStartsWhenOneWorkerStops()
    {
        var data = RandomNumberGenerator.GetBytes(700000); var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var id in new[] { "first", "second", "third" }) await store.CreateAsync(new(id, id, "fixture", "test-1", null, []));
        using var http = new HttpClient(new TransferHandler(data) { Slow = true });
        await using var service = Service(store, http, new CountingSource(data.Length)); var manifest = Manifest("client.exe", data);
        var first = service.Start(Definition, Package(manifest, data.Length), manifest, "first");
        service.Start(Definition, Package(manifest, data.Length), manifest, "second");
        var third = service.Start(Definition, Package(manifest, data.Length), manifest, "third");
        Assert.Equal("queued", third.Stage);
        await service.ActionAsync(third.Id, "pause"); Assert.Equal("paused", service.List().Single(t => t.Id == third.Id).Stage);
        await service.ActionAsync(third.Id, "resume"); Assert.Equal("queued", service.List().Single(t => t.Id == third.Id).Stage);
        await service.ActionAsync(first.Id, "cancel");
        await WaitAsync(() => service.List().Single(t => t.Id == third.Id).Stage != "queued");
        Assert.Equal("canceled", service.List().Single(t => t.Id == first.Id).Stage);
        await WaitAsync(() => service.List().Single(t => t.Id == third.Id).Stage is "completed" or "failed");
        Assert.Equal("completed", service.List().Single(t => t.Id == third.Id).Stage);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("changed")]
    public async Task RestartRefusesRemovedDisabledOrRetargetedSources(string change)
    {
        var data = RandomNumberGenerator.GetBytes(300000); var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var http = new HttpClient(new TransferHandler(data) { Slow = true }); var source = new CountingSource(data.Length);
        var manifest = Manifest("client.exe", data);
        await using (var first = Service(store, http, source))
        {
            first.Start(Definition, Package(manifest, data.Length), manifest, "fixture");
            await WaitAsync(() => first.List()[0].Files[0].Received > 0);
        }
        StoreSource? changed = change == "removed" ? null : change == "disabled" ? Definition with { Enabled = false } : Definition with { BaseUrl = "https://other.example/" };
        await using var reopened = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => changed, _ => source);
        await WaitAsync(() => reopened.List()[0].Stage == "failed");
        Assert.Equal("source", reopened.List()[0].Failure); Assert.Equal(1, source.Resolutions);
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
        changed = Definition; await reopened.ActionAsync(reopened.List()[0].Id, "retry");
        await WaitAsync(() => reopened.List()[0].Stage is "completed" or "failed"); Assert.Equal("completed", reopened.List()[0].Stage);
    }

    [Fact]
    public async Task CorruptHistoryIsRetainedAndBlocksNewTransfers()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances")); var tasks = Path.Combine(_root, "tasks"); Directory.CreateDirectory(tasks);
        var path = Path.Combine(tasks, Guid.NewGuid().ToString("N") + ".json"); await File.WriteAllTextAsync(path, "{broken}");
        using var http = new HttpClient(new TransferHandler([1, 2, 3]));
        await using var service = Service(store, http, new CountingSource(3));
        Assert.Throws<IOException>(() => service.List()); Assert.Throws<IOException>(() => service.ClearFinished());
        Assert.Equal("{broken}", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RestartRejectsChangedDestinationInsteadOfWritingToTheNewFolder()
    {
        var data = RandomNumberGenerator.GetBytes(300000); var store = new InstanceStore(Path.Combine(_root, "instances"));
        var instance = new InstanceDefinition("fixture", "Fixture", "fixture", "test-1", null, []); await store.CreateAsync(instance);
        using var http = new HttpClient(new TransferHandler(data) { Slow = true }); var source = new CountingSource(data.Length); var manifest = Manifest("client.exe", data);
        await using (var first = Service(store, http, source))
        {
            first.Start(Definition, Package(manifest, data.Length), manifest, "fixture"); await WaitAsync(() => first.List()[0].Files[0].Received > 0);
        }
        var other = Path.Combine(_root, "other"); Directory.CreateDirectory(other); await File.WriteAllTextAsync(Path.Combine(_root, "instances", "fixture", "instance.toml"), FantasyTale.Launcher.Core.Definitions.InstanceFile.Render(instance with { InstallationPath = other }));
        await using var reopened = Service(store, http, source);
        await WaitAsync(() => reopened.List()[0].Stage == "failed"); Assert.Equal("disk", reopened.List()[0].Failure);
        Assert.Empty(Directory.GetFiles(other)); Assert.Equal(1, source.Resolutions);
    }

    [Fact]
    public async Task CachedFileChecksSkipUnchangedBytesAndRejectChangedFiles()
    {
        var path = Path.Combine(_root, "data.pack"); byte[] data = [1, 2, 3]; await File.WriteAllBytesAsync(path, data);
        var file = new ClientReleaseFile("data.pack", "data.pack", data.Length, Hash(data));
        var first = new ClientFileCache(_root); Assert.True(await first.MatchesAsync(file, default)); first.RecordInstalled([file]);
        var reopened = new ClientFileCache(_root);
        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(await reopened.MatchesAsync(file, default));
        await File.WriteAllBytesAsync(path, [3, 2, 1]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
        Assert.False(await reopened.MatchesAsync(file, default));
    }

    [Fact]
    public async Task CompleteFolderSnapshotCountsAndDownloadsOnlyChangedFiles()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances")); await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        var client = store.ClientFolder("fixture"); byte[] changed = [4, 5, 6];
        await File.WriteAllBytesAsync(Path.Combine(client, "client.exe"), [1, 2, 3]);
        var text = Encoding.UTF8.GetString(Manifest("client.exe")).Replace("version = \"1\"\n", "", StringComparison.Ordinal) +
            $"\n[[files]]\npath = \"data.bin\"\nsource_path = \"data.bin\"\nsize = 3\nsha256 = \"{Hash(changed)}\"\n";
        var manifest = Encoding.UTF8.GetBytes(text); var package = Package(manifest, 6) with { Version = "", UpdateMode = "files" };
        var source = new CountingSource(3); using var http = new HttpClient(new TransferHandler(changed));
        await using var service = Service(store, http, source);
        service.Start(Definition, package, manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage); Assert.Equal(1, source.Resolutions);
        var progress = Assert.Single(service.List()[0].Files); Assert.Equal("data.bin", progress.Path); Assert.Equal(3, progress.Size);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(client, "client.exe")));
        Assert.Equal(changed, await File.ReadAllBytesAsync(Path.Combine(client, "data.bin")));
    }

    private async Task<ClientReleaseFile[]> StageAsync()
    {
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(_root));
        await File.WriteAllTextAsync(Path.Combine(_root, "first.bin"), "original");
        byte[] data = [1, 2, 3]; await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(_root, Hash(data)), data);
        return [new("first.bin", "first", data.Length, Hash(data)), new("second.bin", "second", data.Length, Hash(data))];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedContentIsRehashedBeforeSkippingSourceResolution(bool corrupted)
    {
        byte[] data = [1, 2, 3];
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        var client = store.ClientFolder("fixture");
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(client));
        await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(client, Hash(data)), corrupted ? [9, 9, 9] : data);
        using var http = new HttpClient(new TransferHandler(data));
        var source = new CountingSource(data.Length);
        await using var service = Service(store, http, source);
        var manifest = Manifest("client.exe", data);
        service.Start(Definition, Package(manifest, data.Length), manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage);
        Assert.Equal(corrupted ? 1 : 0, source.Resolutions);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(client, "client.exe")));
        Assert.All(service.List()[0].Files, file => Assert.Equal("verified", file.Stage));
    }

    [Fact]
    public async Task IdenticalReleaseFilesShareOneVerifiedTransfer()
    {
        byte[] data = [1, 2, 3];
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        var text = Encoding.UTF8.GetString(Manifest("client.exe", data)) +
            $"\n[[files]]\npath = \"copy.bin\"\nsource_path = \"data/copy\"\nsize = 3\nsha256 = \"{Hash(data)}\"\n";
        var manifest = Encoding.UTF8.GetBytes(text);
        using var http = new HttpClient(new TransferHandler(data));
        var source = new CountingSource(data.Length);
        await using var service = Service(store, http, source);
        service.Start(Definition, Package(manifest, 6), manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage);
        Assert.Equal(1, source.Resolutions);
        Assert.Equal(2, service.List()[0].Files.Count);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(store.ClientFolder("fixture"), "copy.bin")));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("changed")]
    public async Task VerifiedStagingDoesNotBypassSourcePolicy(string change)
    {
        byte[] data = [1, 2, 3];
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        var client = store.ClientFolder("fixture");
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(client));
        await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(client, Hash(data)), data);
        using var http = new HttpClient(new TransferHandler(data));
        var source = new CountingSource(data.Length);
        StoreSource? current = change == "removed" ? null : change == "disabled"
            ? Definition with { Enabled = false } : Definition with { BaseUrl = "https://other.example/" };
        var lookups = 0;
        await using var service = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"),
            _ => Interlocked.Increment(ref lookups) == 1 ? Definition : current, _ => source);
        var manifest = Manifest("client.exe", data);
        service.Start(Definition, Package(manifest, data.Length), manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("failed", service.List()[0].Stage);
        Assert.Equal("source", service.List()[0].Failure);
        Assert.Equal(0, source.Resolutions);
        Assert.False(File.Exists(Path.Combine(client, "client.exe")));
    }

    [Fact]
    public async Task FailedTaskReportsIntegrityAndNeverInstallsCorruptContent()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var http = new HttpClient(new TransferHandler([0, 0, 0]));
        await using var service = Service(store, http, new CountingSource(3));
        var manifest = Manifest("client.exe");
        service.Start(Definition, Package(manifest, 3), manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage == "failed");
        Assert.Equal("integrity", service.List()[0].Failure);
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
        using var lease = store.Acquire("fixture");
    }

    [Fact]
    public async Task RecoveryAfterProcessLossIsRepeatable()
    {
        var files = await StageAsync(); var workspace = ClientInstallTransaction.Workspace(_root);
        File.Move(Path.Combine(_root, "first.bin"), Path.Combine(workspace, "original-0"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "first.bin"), [1, 2, 3]);
        var journal = new { Schema = 1, Committed = false, Files = new[] { new { Path = "first.bin", Size = 3, Hash = files[0].Sha256, OriginalSize = 8, OriginalHash = Hash(Encoding.UTF8.GetBytes("original")) } } };
        await File.WriteAllTextAsync(Path.Combine(workspace, "journal.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        await ClientInstallTransaction.RecoverAsync(_root); await ClientInstallTransaction.RecoverAsync(_root);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "first.bin")));
        Assert.False(File.Exists(Path.Combine(workspace, "journal.json")));
    }
    private static readonly StoreSource Definition = new("fixture", "Fixture", "http", "https://storage.example", "catalog.toml", true, false);

    [Fact]
    public async Task LooseAssetArchiveCompletesCleanupAndRestoresCompletedTask()
    {
        const int count = 8200;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            for (var index = 0; index < count; index++)
            {
                using var entry = zip.CreateEntry($"Images/#Screen/{index}.bin").Open();
                entry.Write(BitConverter.GetBytes(index));
            }
        var transport = new Dictionary<string, byte[]> { ["client.zip"] = buffer.ToArray() };
        var manifest = ArchiveManifest(transport, "");
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var handler = new ArchiveHandler(transport);
        using var http = new HttpClient(handler);
        var source = new ArchiveSource(transport);
        await using (var service = Service(store, http, source))
        {
            service.Start(Definition, Package(manifest, transport["client.zip"].Length), manifest, "fixture");
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            while (service.List()[0].Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
            Assert.Equal("completed", service.List()[0].Stage);
        }
        var client = store.ClientFolder("fixture");
        Assert.Equal(count, Directory.GetFiles(Path.Combine(client, "Images/#Screen")).Length);
        Assert.Empty(Directory.GetFiles(ClientInstallTransaction.Workspace(client), "*.verified"));
        Assert.NotNull(ClientInstallTransaction.ReadReceipt(client));
        await using var reopened = Service(store, http, source);
        Assert.Equal("completed", Assert.Single(reopened.List()).Stage);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "Game/")]
    [InlineData(false, "Outer/Changed name/")]
    public async Task SplitZipDownloadsEveryVolumeAndDiscoversExecutableRoot(bool split, string prefix)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var file = zip.CreateEntry(prefix + "client.exe").Open()) file.Write("client data"u8);
            using (var file = zip.CreateEntry(prefix + "config.ini").Open()) file.Write("settings"u8);
        }
        var data = buffer.ToArray();
        var payloads = split ? new[] { data[..80], data[80..] } : [data];
        var transport = payloads.Select((value, index) => (Name: $"client.zip.{index + 1:000}", Data: value)).ToDictionary(v => v.Name, v => v.Data);
        var manifest = ArchiveManifest(transport, "");
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var handler = new ArchiveHandler(transport); using var http = new HttpClient(handler);
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
        var build = new BuildDefinition("test-1", "test", 1, name, "client.exe", "", "", null, null, "test", [], []);
        var game = new GameDefinition("fixture", GameStatus.Enabled, name, "test.dll", "external", "", null, "", [build]);
        await using var service = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => Definition,
            _ => new ArchiveSource(transport), game: _ => game);
        service.Start(Definition, Package(manifest, data.Length), manifest, "fixture");
        await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
        Assert.Equal("completed", service.List()[0].Stage);
        Assert.Equal(transport.Count, handler.Requests);
        var client = store.ClientFolder("fixture");
        Assert.Equal("client data", await File.ReadAllTextAsync(Path.Combine(client, "client.exe")));
        Assert.Equal("settings", await File.ReadAllTextAsync(Path.Combine(client, "config.ini")));
        Assert.DoesNotContain(Directory.GetFiles(client), path => path.Contains(".zip", StringComparison.Ordinal));
        Assert.NotNull(ClientInstallTransaction.ReadReceipt(client));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EncryptedArchiveUsesMetadataOrRetriesAfterRestartWithoutDownloadingAgain(bool supplied)
    {
        // Locally generated stored ZIP: one text file, no game code or third-party data.
        var bytes = Convert.FromBase64String("UEsDBBQAAQAAAEuBUV0HoT7jHwAAABMAAAAKAAAAY2xpZW50LmV4ZXlsEwE+O+FIjOGvJIRDvZprJb5Up/2YXz7LouUa595QSwECPwAUAAEAAABLgVFdB6E+4x8AAAATAAAACgAkAAAAAAAAACAAAAAAAAAAY2xpZW50LmV4ZQoAIAAAAAAAAQAYAOx6yfMOXt0BAAAAAAAAAAAAAAAAAAAAAFBLBQYAAAAAAQABAFwAAABHAAAAAAA=");
        var transport = new Dictionary<string, byte[]> { ["client.zip"] = bytes };
        var manifest = ArchiveManifest(transport, supplied ? "password = \"test-only-password\"\n" : "");
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
        using var handler = new ArchiveHandler(transport); using var http = new HttpClient(handler);
        var source = new ArchiveSource(transport); string id;
        await using (var service = Service(store, http, source))
        {
            id = service.Start(Definition, Package(manifest, bytes.Length), manifest, "fixture").Id;
            await WaitAsync(() => service.List()[0].Stage is "completed" or "failed");
            Assert.Equal(supplied ? "completed" : "failed", service.List()[0].Stage);
            if (!supplied) Assert.Equal("archive-password", service.List()[0].Failure);
        }
        if (!supplied)
        {
            await using var reopened = Service(store, http, source);
            Assert.Equal("archive-password", reopened.List()[0].Failure);
            await reopened.ActionAsync(id, "retry", "incorrect");
            await WaitAsync(() => reopened.List()[0].Stage == "failed");
            Assert.Equal("archive-password", reopened.List()[0].Failure);
            await reopened.ActionAsync(id, "retry", "test-only-password");
            await WaitAsync(() => reopened.List()[0].Stage is "completed" or "failed");
            Assert.Equal("completed", reopened.List()[0].Stage);
            Assert.DoesNotContain("test-only-password", await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(_root, "tasks"), "*.json").Single()));
        }
        Assert.Equal(1, handler.Requests);
        Assert.Equal("fixture client data", await File.ReadAllTextAsync(Path.Combine(store.ClientFolder("fixture"), "client.exe")));
    }

    private static byte[] ArchiveManifest(Dictionary<string, byte[]> volumes, string options)
        => Encoding.UTF8.GetBytes("schema = 1\n[release]\nid = \"fixture.client\"\ngame = \"fixture\"\nbuild = \"test-1\"\nversion = \"1\"\nformat = \"archive\"\narchive_format = \"zip\"\n" + options +
            string.Concat(volumes.Select((row, index) => $"\n[[files]]\npath = \"{row.Key}\"\nsource_path = \"{row.Key}\"\nsize = {row.Value.Length}\nsha256 = \"{Hash(row.Value)}\"\nvolume = {index + 1}\n")));

    private sealed class ArchiveSource(Dictionary<string, byte[]> volumes) : IDownloadSource
    {
        public string Kind => "http";
        public Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(new ResolvedDownload(new Uri("https://storage.example/" + path), new Dictionary<string, string>(), volumes[path].Length));
    }

    private sealed class ArchiveHandler(Dictionary<string, byte[]> volumes) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(volumes[request.RequestUri!.AbsolutePath.TrimStart('/')]) });
        }
    }

    private ClientDownloadService Service(InstanceStore store, HttpClient http, IDownloadSource source) =>
        new(store, http, Path.Combine(_root, "tasks"), _ => Definition, _ => source);
    private static byte[] Manifest(string path, byte[]? payload = null) => Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"fixture.client\"\ngame = \"fixture\"\nbuild = \"test-1\"\nversion = \"1\"\n[[files]]\npath = \"{path}\"\nsource_path = \"data/client\"\nsize = {payload?.Length ?? 3}\nsha256 = \"{Hash(payload ?? [1, 2, 3])}\"\n");
    private static StorePackage Package(byte[] manifest, long size) => new("fixture.client", "client", "fixture", ["test-1"], "1", new Dictionary<string, string> { ["en"] = "Fixture" }, new Dictionary<string, string>(), "Fixture", size, "manifest.toml", Hash(manifest));
    private static async Task WaitAsync(Func<bool> condition)
    { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!condition()) await Task.Delay(20, deadline.Token); }
    private sealed class CountingSource(long size) : IDownloadSource
    {
        public string Kind => "http";
        internal int Resolutions;
        public Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken) { Resolutions++; return Task.FromResult(Source(size)); }
    }
    private sealed class TransferHandler(byte[] data) : HttpMessageHandler
    {
        internal bool SupportsRange = true, InvalidRange, Slow;
        internal long LastOffset;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastOffset = request.Headers.Range?.Ranges.Single().From ?? 0;
            var offset = SupportsRange ? (int)LastOffset : 0;
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
            response.Content = Slow ? new StreamContent(new SlowStream(data[offset..])) : new ByteArrayContent(data[offset..]);
            response.Content.Headers.ContentLength = data.Length - offset;
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(InvalidRange ? 0 : offset, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }
    private sealed class SlowStream(byte[] data) : MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(15, cancellationToken); return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], cancellationToken); }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
