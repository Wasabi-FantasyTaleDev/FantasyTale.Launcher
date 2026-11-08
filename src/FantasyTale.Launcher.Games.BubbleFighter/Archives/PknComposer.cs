using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FantasyTale.Launcher.Games.BubbleFighter.Archives;

/// <summary>Rebuilds either PKN index layout with one member in memory and a disk-backed payload spool.</summary>
public static class PknComposer
{
    /// <summary>Writes verified decoded members, preserving the early format's root and unknown header words.</summary>
    public static void Write(Stream output, string uri, IEnumerable<(string Path, byte[] Data)> members,
        LegacyPknArchive? legacy = null) => WriteMembers(output, uri, members.Select(m => new PknWriteMember(m.Path, m.Data, null)), legacy);

    internal static void WriteMembers(Stream output, string uri, IEnumerable<PknWriteMember> members, LegacyPknArchive? legacy = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(uri);
        if (legacy is null) uri = uri.Replace('\\', '/').ToLowerInvariant();
        if (!output.CanSeek || output.Position != 0) throw new ArgumentException("An empty seekable destination is required.", nameof(output));
        var temporary = Path.Combine(Path.GetTempPath(), "fantasytale-pack-" + Guid.NewGuid().ToString("N"));
        using var spool = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            65536, FileOptions.DeleteOnClose);
        using var index = new MemoryStream();
        using var writer = new BinaryWriter(index, Encoding.Unicode, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint count = 0;
        foreach (var member in members)
        {
            var path = member.Path;
            if (!seen.Add(path) || string.IsNullOrEmpty(path)) throw new InvalidDataException("Duplicate or empty PKN member: " + path);
            var relative = path;
            if (legacy is { Root.Length: > 0 })
            {
                var prefix = legacy.Root + "/";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Member lies outside the original early-PKN root: " + path);
                relative = path[prefix.Length..];
            }
            byte[] payload, md5;
            uint flags, size, stored;
            if (member.Stored is { } raw)
            {
                if (raw.Path != path) throw new InvalidDataException("An encrypted member cannot be renamed without re-encoding it.");
                payload = raw.Payload; md5 = raw.Md5; flags = raw.Flags; size = raw.Size; stored = raw.StoredSize;
            }
            else
            {
                var bytes = member.Data ?? throw new InvalidDataException("Missing PKN member contents.");
                md5 = PknFormat.MemberDigest(bytes);
                using var compressed = new MemoryStream();
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes);
                payload = compressed.ToArray();
                Array.Resize(ref payload, checked((payload.Length + 3) & ~3));
                new Snow2Cipher(PknFormat.MemberKey(path, md5)).Encrypt(payload);
                flags = 3; size = checked((uint)bytes.Length); stored = checked((uint)payload.Length);
            }
            var block = checked((uint)(spool.Position / 1024));
            WritePath(writer, relative);
            if (legacy is null) writer.Write(unchecked(flags + block + size + stored + (uint)md5.Sum(b => b)));
            writer.Write(flags); writer.Write(block); writer.Write(size); writer.Write(stored); writer.Write(md5);
            spool.Write(payload);
            var aligned = checked((spool.Position + 1023) / 1024 * 1024);
            spool.SetLength(aligned); spool.Position = aligned;
            count = checked(count + 1);
        }
        var headerOffset = PknFormat.HeaderOffset(uri);
        byte[] header;
        long indexOffset;
        if (legacy is null)
        {
            header = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(header, unchecked(count + 2));
            header[4] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), count);
            indexOffset = PknFormat.IndexOffset(uri);
        }
        else
        {
            using var raw = new MemoryStream();
            using var h = new BinaryWriter(raw, Encoding.Unicode, leaveOpen: true);
            h.Write(legacy.Unknown0); h.Write(legacy.Unknown1); h.Write(legacy.IndexGap); h.Write(count);
            WritePath(h, legacy.Root);
            indexOffset = checked(headerOffset + raw.Length + legacy.IndexGap);
            header = raw.ToArray();
            Array.Resize(ref header, checked((header.Length + 3) & ~3));
            if (headerOffset + header.Length > indexOffset) throw new InvalidDataException("Early-PKN header overlaps its index.");
        }
        var rawIndexLength = index.Length;
        var indexBytes = index.ToArray();
        Array.Resize(ref indexBytes, checked((indexBytes.Length + 3) & ~3));
        // cn-279 and kr-1256 align the decoded index end, before cipher word padding.
        var dataOffset = checked((indexOffset + rawIndexLength + 1023) / 1024 * 1024);
        if (indexOffset + indexBytes.Length > dataOffset) throw new InvalidDataException("PKN index overlaps data.");
        output.SetLength(checked(dataOffset + spool.Length));
        new Snow2Cipher(PknFormat.HeaderKey(uri)).Encrypt(header);
        output.Position = headerOffset; output.Write(header);
        new Snow2Cipher(PknFormat.IndexKey(uri)).Encrypt(indexBytes);
        output.Position = indexOffset; output.Write(indexBytes);
        output.Position = dataOffset; spool.Position = 0; spool.CopyTo(output);
    }

    private static void WritePath(BinaryWriter writer, string path)
    {
        writer.Write(checked((uint)path.Length));
        writer.Write(Encoding.Unicode.GetBytes(path));
    }
}
