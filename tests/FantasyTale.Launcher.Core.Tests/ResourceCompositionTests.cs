using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceCompositionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-resource-test-").FullName;

    [Theory]
    [InlineData("Models/Characters/Dao/Default/Default.bfz")]
    [InlineData("Packs/ModelsChrPlDao_00000.pkn")]
    public void ComposesCombinedEditsPreservesOthersAndRestoresExactOriginal(string uri)
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var input = Path.Combine(client, uri); Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        (string Path, byte[] Data)[] members = [("Models/Characters/Dao/Default/Body.msh", "original body"u8.ToArray()),
            ("Models/Characters/Dizni/Default/Body.msh", "original second"u8.ToArray()),
            ("Models/Characters/Other/keep.txt", "unrelated"u8.ToArray())];
        using (var stream = File.Create(input))
            if (uri.EndsWith(".bfz", StringComparison.Ordinal)) BfzArchive.Write(stream, uri, members);
            else PknComposer.Write(stream, uri.ToLowerInvariant(), members);
        var original = File.ReadAllBytes(input);
        var first = Payload("body", "new body"u8.ToArray()); var second = Payload("face", "new face"u8.ToArray());
        var output = Path.Combine(_root, "output" + Path.GetExtension(uri));
        new ArchiveResourceComposer().Compose(input, uri, output,
            [new(members[0].Path, first, Hash(File.ReadAllBytes(first)), Hash(members[0].Data)),
             new("Models/Characters/Dao/Default/f00.png", second, Hash(File.ReadAllBytes(second)), null)]);
        using (var result = Archive.Open(output, uri))
        {
            Assert.Equal("new body", Encoding.UTF8.GetString(result.Read(members[0].Path)));
            Assert.Equal(members[1].Data, result.Read(members[1].Path));
            Assert.Equal(members[2].Data, result.Read(members[2].Path));
            Assert.Equal(4, result.Entries.Count);
        }
        _ = RuntimeDeployment.Apply(client, "test", null,
            [new(output, uri, new FileInfo(output).Length, Hash(File.ReadAllBytes(output))) { OriginalSha256 = Hash(original) }]);
        // A fresh recovery object has no access to the applying process's in-memory state.
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Fact]
    public void EarlyPknPreservesCaseSensitiveUriRootUnknownHeaderAndOriginal()
    {
        const string uri = "Pack/Models0000.pkn";
        const string member = "Models/Body.msh";
        var input = Path.Combine(_root, "early.pkn");
        var original = "original body"u8.ToArray();
        using (var file = File.Create(input))
        {
            using var index = new MemoryStream();
            using (var writer = new BinaryWriter(index, Encoding.Unicode, leaveOpen: true))
            {
                writer.Write(8u); writer.Write(Encoding.Unicode.GetBytes("Body.msh"));
                writer.Write(0u); writer.Write(0u); writer.Write((uint)original.Length); writer.Write((uint)original.Length);
                writer.Write(Convert.FromHexString("ca60085960998bb15ba4807ce60659cc"));
            }
            using var header = new MemoryStream();
            using (var writer = new BinaryWriter(header, Encoding.Unicode, leaveOpen: true))
            {
                writer.Write(0u); writer.Write(2268132480u); writer.Write(196u); writer.Write(1u);
                writer.Write(6u); writer.Write(Encoding.Unicode.GetBytes("Models"));
            }
            var headerBytes = header.ToArray();
            var indexBytes = index.ToArray(); Array.Resize(ref indexBytes, (indexBytes.Length + 3) & ~3);
            new Snow2Cipher(PknFormat.HeaderKey(uri)).Encrypt(headerBytes);
            new Snow2Cipher(PknFormat.IndexKey(uri)).Encrypt(indexBytes);
            file.SetLength(2048);
            file.Position = PknFormat.HeaderOffset(uri); file.Write(headerBytes);
            file.Position = PknFormat.HeaderOffset(uri) + header.Length + 196; file.Write(indexBytes);
            file.Position = 1024; file.Write(original);
        }
        var payload = Payload("replacement", "new body"u8.ToArray());
        var output = Path.Combine(_root, "early-result.pkn");
        new ArchiveResourceComposer().Compose(input, uri, output,
            [new(member, payload, Hash("new body"u8.ToArray()), Hash(original))]);
        using var result = LegacyPknArchive.Open(output, uri);
        Assert.Equal("Models", result.Root);
        Assert.Equal(2268132480u, result.Unknown1);
        Assert.Equal(196u, result.IndexGap);
        Assert.Equal(0u, result.Unknown0);
        Assert.Equal(uri, result.Uri);
        Assert.Equal("new body", Encoding.UTF8.GetString(result.Read(member)));
        using var unchanged = LegacyPknArchive.Open(input, uri);
        Assert.Equal(original, unchanged.Read(member));
    }

    [Fact]
    public void ConflictsAndStaleInputsNeverProduceAnArchive()
    {
        var uri = "Packs/test.pkn"; var input = Path.Combine(_root, "input.pkn");
        using (var stream = File.Create(input)) PknComposer.Write(stream, uri.ToLowerInvariant(), [("a.txt", "before"u8.ToArray())]);
        var payload = Payload("payload", "after"u8.ToArray());
        var output = Path.Combine(_root, "output.pkn");
        var edit = new ArchiveResourceEdit("a.txt", payload, Hash("after"u8.ToArray()), Hash("before"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => new ArchiveResourceComposer().Compose(input, uri, output, [edit, edit with { Source = null }]));
        Assert.Throws<InvalidDataException>(() => new ArchiveResourceComposer().Compose(input, uri, output, [edit with { ExpectedOriginal = Hash("different"u8.ToArray()) }]));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void ExistingOutputIsPreservedWhenCreationIsRejected()
    {
        const string uri = "Packs/test.pkn";
        var input = Path.Combine(_root, "input.pkn");
        using (var stream = File.Create(input)) PknComposer.Write(stream, uri, [("a.txt", "before"u8.ToArray())]);
        var output = Payload("existing.pkn", "existing user file"u8.ToArray());
        Assert.Throws<IOException>(() => new ArchiveResourceComposer().Compose(input, uri, output, []));
        Assert.Equal("existing user file", File.ReadAllText(output));
        Assert.Throws<IOException>(() => new ArchiveResourceComposer().Compose(input, uri, input, []));
        using var original = Archive.Open(input, uri);
        Assert.Equal("before"u8.ToArray(), original.Read("a.txt"));
    }

    [Fact]
    public void InterruptedPreparationLeavesOriginalUntouchedAndIsRecoverable()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        File.WriteAllText(Path.Combine(client, "archive.pkn"), "original");
        var staging = Directory.CreateDirectory(Path.Combine(client, ".fantasytale-runtime")).FullName;
        File.WriteAllText(Path.Combine(staging, "preparing"), "");
        File.WriteAllText(Path.Combine(staging, "old-0"), "original");
        File.WriteAllText(Path.Combine(staging, "new-0"), "prepared");
        File.WriteAllText(Path.Combine(staging, "journal.pending"), "truncated");
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "archive.pkn")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void CorruptBackupRetainsRecoveryEvidence()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var source = Payload("patched", "patched"u8.ToArray());
        File.WriteAllText(Path.Combine(client, "model.bfz"), "original");
        _ = RuntimeDeployment.Apply(client, "test", null, [new(source, "model.bfz", 7, Hash("patched"u8.ToArray()))]);
        var backup = Path.Combine(client, ".fantasytale-runtime", "old-0");
        File.WriteAllText(backup, "corrupt");
        Assert.Throws<IOException>(() => RuntimeDeployment.RecoverStoppedClient(client));
        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(Path.Combine(client, ".fantasytale-runtime", "journal.json")));
        Assert.Equal("patched", File.ReadAllText(Path.Combine(client, "model.bfz")));
    }

    [Fact]
    public void RecoveryCanResumeAfterOriginalWasRestoredAndBackupCleanupWasInterrupted()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var source = Payload("patched", "patched"u8.ToArray());
        var target = Path.Combine(client, "model.bfz");
        File.WriteAllText(target, "original");
        _ = RuntimeDeployment.Apply(client, "test", null, [new(source, "model.bfz", 7, Hash("patched"u8.ToArray()))]);
        File.WriteAllText(target, "original");
        File.Delete(Path.Combine(client, ".fantasytale-runtime", "old-0"));
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    private string Payload(string name, byte[] bytes) { var path = Path.Combine(_root, name); File.WriteAllBytes(path, bytes); return path; }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
