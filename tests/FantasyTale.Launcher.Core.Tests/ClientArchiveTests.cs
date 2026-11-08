using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Stores;
using Xunit.Abstractions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientArchiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-client-archives-").FullName;

    [Theory]
    [InlineData("")]
    [InlineData("Wrapper/")]
    [InlineData("Changed folder/Another folder/")]
    public async Task ExecutableDiscoveryInstallsNestedArchiveAtItsActualRoot(string prefix)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry(prefix + "Bin/client.exe").Open()) entry.Write("executable"u8);
            using (var entry = zip.CreateEntry(prefix + "Data/assets.bin").Open()) entry.Write("assets"u8);
            using (var entry = zip.CreateEntry(prefix + "Tools/unrelated.exe").Open()) entry.Write("tool"u8);
        }
        var volume = await StageAsync("client.zip", buffer.ToArray());
        var expanded = await ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default);
        var files = ArchiveInstallationRoot.Find(expanded, ["Bin/client.exe", "Bin/alias.exe"]);
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default);
        Assert.Equal("executable", await File.ReadAllTextAsync(Path.Combine(_root, "Bin", "client.exe")));
        Assert.Equal("assets", await File.ReadAllTextAsync(Path.Combine(_root, "Data", "assets.bin")));
        Assert.Equal(3, files.Count);
    }

    [Theory]
    [InlineData("First/client.exe", "Second/client.exe")]
    [InlineData("client.exe", "Backup/client.exe")]
    [InlineData("tool.exe", "other.exe")]
    public void MissingOrAmbiguousExecutableCannotChooseAnInstallationRoot(string first, string second)
    {
        ClientReleaseFile File(string path) => new(path, path, 1, new string('a', 64));
        Assert.Throws<InvalidDataException>(() => ArchiveInstallationRoot.Find([File(first), File(second)], ["client.exe"]));
    }

    [Fact]
    public void AliasesInOneRootAreUnambiguousAndFilesOutsideThatRootAreNotInstalled()
    {
        ClientReleaseFile File(string path) => new(path, path, 1, new string('a', 64));
        var files = ArchiveInstallationRoot.Find([File("Bundle/client.exe"), File("Bundle/alias.exe"), File("Bundle/Data/a"), File("README.txt")],
            ["client.exe", "alias.exe"]);
        Assert.Equal(["client.exe", "alias.exe", "Data/a"], files.Select(file => file.Path));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("session.toml")]
    [InlineData(".fantasytale-install/data")]
    [InlineData("Data/CON.txt")]
    [InlineData("Images/../escape")]
    [InlineData("Images/name?")]
    [InlineData("Images/C:/escape")]
    public async Task InvalidEntryCannotModifyTheInstallation(string name)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var output = zip.CreateEntry(name).Open()) output.Write("invalid"u8);
        var volume = await StageAsync("invalid.zip", buffer.ToArray());
        await File.WriteAllTextAsync(Path.Combine(_root, "original.txt"), "retained");
        await Assert.ThrowsAnyAsync<Exception>(() => ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default));
        Assert.Equal("retained", await File.ReadAllTextAsync(Path.Combine(_root, "original.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "invalid.zip")));
    }

    [Theory]
    [InlineData("Images/#Screen/#Screen.bfz")]
    [InlineData("Images/100%/literal%2e%2e.bfz")]
    public async Task LiteralAssetNamesArePreservedThroughInstallationAndRecovery(string name)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var entry = zip.CreateEntry(name).Open()) entry.WriteByte(42);
        var volume = await StageAsync("assets.zip", buffer.ToArray());
        var files = await ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default);
        Assert.Equal(name, Assert.Single(files).Path);
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default, deferCommit: true);
        Assert.Equal(new byte[] { 42 }, await File.ReadAllBytesAsync(Path.Combine(_root, name)));
        await ClientInstallTransaction.RecoverAsync(_root);
        Assert.False(File.Exists(Path.Combine(_root, name)));
    }

    [Theory]
    [InlineData("file", "file/child")]
    [InlineData("Data/one", "data/two")]
    [InlineData("same", "SAME")]
    public async Task ConflictingEntriesAreRejectedBeforeStaging(string first, string second)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var name in new[] { first, second })
            { using var output = zip.CreateEntry(name).Open(); output.Write("data"u8); }
        var volume = await StageAsync("invalid.zip", buffer.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default));
        Assert.Single(Directory.GetFiles(ClientInstallTransaction.Workspace(_root), "*.verified"));
    }

    [Fact]
    public async Task LooseAssetArchiveSupportsLargeInstallationRecoveryAndCacheRestart()
    {
        const int count = 4200;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            for (var index = 0; index < count; index++)
            {
                using var entry = zip.CreateEntry("Data/" + new string('a', 100) + index + ".bin").Open();
                entry.WriteByte(42);
            }
        var volume = await StageAsync("loose-assets.zip", buffer.ToArray());
        var expanded = await ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default);
        Assert.Equal(count, expanded.Count);
        await ClientInstallTransaction.ApplyAsync(_root, expanded, _ => { }, default, deferCommit: true);
        await ClientInstallTransaction.RecoverAsync(_root);
        Assert.All(expanded, file => Assert.False(File.Exists(ClientInstallTransaction.Safe(_root, file.Path))));
        await ClientInstallTransaction.ApplyAsync(_root, expanded, _ => { }, default);
        var cache = new ClientFileCache(_root);
        cache.RecordInstalled(expanded);
        Assert.True(new FileInfo(Path.Combine(ClientInstallTransaction.Workspace(_root), "file-cache.json")).Length > 1024 * 1024);
        var restarted = new ClientFileCache(_root);
        Assert.True(await restarted.MatchesAsync(expanded[0], default));
        Assert.True(await restarted.MatchesAsync(expanded[^1], default));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(_root), "journal.json")));
    }

    [Fact]
    public async Task ArchiveRootIsExplicitAndTransactionalFailureRestoresOriginalFiles()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var output = zip.CreateEntry("Wrapper/Game/client.exe").Open()) output.Write("new executable"u8);
            using (var output = zip.CreateEntry("Wrapper/Game/data.ini").Open()) output.Write("new settings"u8);
        }
        var volume = await StageAsync("wrapped.zip", buffer.ToArray());
        volume = volume with { ArchiveRoot = "Wrapper/Game" };
        var files = await ClientArchiveExtractor.ExtractAsync(_root, [volume], null, _ => { }, default);
        await File.WriteAllTextAsync(Path.Combine(_root, "client.exe"), "old executable");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(_root, files, _ => throw new IOException("interruption"), default));
        Assert.Equal("old executable", await File.ReadAllTextAsync(Path.Combine(_root, "client.exe")));
        Assert.False(File.Exists(Path.Combine(_root, "data.ini")));
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default);
        Assert.Equal("new executable", await File.ReadAllTextAsync(Path.Combine(_root, "client.exe")));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("hash")]
    [InlineData("checksum")]
    public async Task InvalidFormatHashOrChecksumCannotProduceVerifiedOutput(string failure)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var content = zip.CreateEntry("client.exe", CompressionLevel.NoCompression).Open())
            content.Write("synthetic executable"u8);
        var bytes = buffer.ToArray();
        if (failure == "checksum") bytes[40] ^= 1;
        var volume = await StageAsync("client.zip", bytes);
        if (failure == "format") volume = volume with { ArchiveFormat = "7z" };
        var expected = new ClientReleaseFile("client.exe", "client.exe", 20, new string('a', 64));
        await Assert.ThrowsAsync<InvalidDataException>(() => ClientArchiveExtractor.ExtractAsync(
            _root, [volume], null, _ => { }, default, failure == "hash" ? [expected] : null));
        Assert.Single(Directory.GetFiles(ClientInstallTransaction.Workspace(_root), "*.verified"));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(_root), "archive-entry.part")));
    }

    [Fact]
    public async Task CancellationDuringNativeOutputRemovesPartialFileAndNeverReportsCompletion()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var content = zip.CreateEntry("client.exe").Open()) content.Write(new byte[1024 * 1024]);
        var volume = await StageAsync("client.zip", buffer.ToArray());
        using var cancellation = new CancellationTokenSource();
        var progress = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientArchiveExtractor.ExtractAsync(_root, [volume], null,
            value => { progress.Add(value); cancellation.Cancel(); }, cancellation.Token));
        Assert.DoesNotContain(100, progress);
        Assert.Single(Directory.GetFiles(ClientInstallTransaction.Workspace(_root), "*.verified"));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(_root), "archive-entry.part")));
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("progress")]
    [InlineData("completed")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void NativeConsumerFailureRetainsOriginalExceptionAndReleasesOutput(string callback)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var content = zip.CreateEntry("client.exe").Open()) content.Write(new byte[1024]);
        var target = Path.Combine(_root, "client.exe");
        var expected = new InvalidOperationException("Synthetic consumer callback failure.");
        using (var decoder = new NativeArchiveDecoder("zip", ["client.zip"], [buffer], null, default))
        {
            var actual = Assert.Throws<InvalidOperationException>(() => decoder.Extract(
                _ => callback == "destination" ? throw expected : target,
                (_, _) => { if (callback == "completed") throw expected; },
                _ => { if (callback == "progress") throw expected; }));
            Assert.Same(expected, actual);
        }
        if (File.Exists(target)) File.Delete(target);
        File.WriteAllText(target, "retry");
        Assert.Equal("retry", File.ReadAllText(target));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void NativeDecoderCanBeDisposedTwiceAndRejectsExtractionAfterDisposal()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var content = zip.CreateEntry("client.exe").Open()) content.Write("client"u8);
        using var decoder = new NativeArchiveDecoder("zip", ["client.zip"], [buffer], null, default);
        decoder.Dispose();
        decoder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => decoder.Extract(
            _ => throw new InvalidOperationException("Disposed decoder invoked a consumer."), (_, _) => { }, _ => { }));
        Assert.True(buffer.CanRead);
    }

    private sealed class ArchiveFixturesFactAttribute : FactAttribute
    {
        public ArchiveFixturesFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_ARCHIVE_FIXTURES")))
                Skip = "Set FT_TEST_ARCHIVE_FIXTURES to an independently extracted archive fixture matrix.";
        }
    }

    [ArchiveFixturesFact]
    public async Task OptionalRealArchiveMatrixMatchesIndependentExtraction()
    {
        var fixtureRoot = Environment.GetEnvironmentVariable("FT_TEST_ARCHIVE_FIXTURES");
        Assert.False(string.IsNullOrEmpty(fixtureRoot));
        var cases = JsonSerializer.Deserialize<ArchiveCase[]>(await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "cases.json")))!;
        Assert.NotEmpty(cases);
        foreach (var item in cases)
        {
            output.WriteLine("Archive fixture: " + item.Name + " / " + item.Format);
            var client = Path.Combine(_root, item.Name); Directory.CreateDirectory(client);
            var volumes = new List<ClientReleaseFile>();
            foreach (var path in item.Volumes)
            {
                var data = await File.ReadAllBytesAsync(Path.Combine(fixtureRoot, path));
                var hash = Convert.ToHexStringLower(SHA256.HashData(data));
                Directory.CreateDirectory(ClientInstallTransaction.Workspace(client));
                await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(client, hash), data);
                volumes.Add(new(path, path, data.Length, hash, ArchiveFormat: item.Format, ArchiveVolume: volumes.Count + 1));
            }
            if (item.Password is not null)
            {
                await Assert.ThrowsAsync<ArchivePasswordRequiredException>(() => ClientArchiveExtractor.ExtractAsync(client, volumes, null, _ => { }, default));
                await Assert.ThrowsAnyAsync<Exception>(() => ClientArchiveExtractor.ExtractAsync(client, volumes, "incorrect", _ => { }, default));
            }
            if (volumes.Count > 1)
                await Assert.ThrowsAnyAsync<Exception>(() => ClientArchiveExtractor.ExtractAsync(client, volumes[..^1], item.Password, _ => { }, default));
            var files = await ClientArchiveExtractor.ExtractAsync(client, volumes, item.Password, _ => { }, default);
            Assert.Equal(item.Files.Count, files.Count);
            foreach (var file in files) Assert.Equal(item.Files[file.Path], file.Sha256);
        }
    }

    private async Task<ClientReleaseFile> StageAsync(string name, byte[] data)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(_root));
        await File.WriteAllBytesAsync(ClientInstallTransaction.Staged(_root, hash), data);
        return new(name, name, data.Length, hash, ArchiveFormat: "zip", ArchiveVolume: 1);
    }

    private sealed record ArchiveCase(string Name, string Format, string[] Volumes, string? Password, Dictionary<string, string> Files);

    public void Dispose() => Directory.Delete(_root, true);
}
