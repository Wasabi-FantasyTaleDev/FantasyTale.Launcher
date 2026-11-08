using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FantasyTale.Launcher.Games.BubbleFighter.Archives;

/// <summary>
/// Reads the early PKN layout consumed by cn-279 0x61D8B0. Unlike modern PKN, the
/// case-sensitive URI keys a header of four u32s and a root path; index entries have
/// no checksum word and store paths relative to that root. Members retain the same
/// SNOW, compression flags and MD5 verification (0x61D590, 0x61E0B0).
/// </summary>
public sealed class LegacyPknArchive : IArchiveReader
{
    private readonly Stream _stream;
    private readonly int _maxMemberBytes;
    private readonly Dictionary<string, PknEntry> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ArchiveEntry> _entries = [];

    private LegacyPknArchive(Stream stream, string uri, int maxMemberBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMemberBytes);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("A readable, seekable stream is required.", nameof(stream));
        _stream = stream;
        _maxMemberBytes = maxMemberBytes;
        Uri = uri;
        // cn-279 0x61D390: the sum of the original URI's UTF-16 code units modulo 312, plus 30.
        var headerOffset = PknFormat.HeaderOffset(uri);
        var header = new Cursor(stream, headerOffset, PknFormat.HeaderKey(uri));
        Unknown0 = header.ReadUInt32(); // cn-279: stored at pack +24
        Unknown1 = header.ReadUInt32(); // cn-279: stored at pack +28
        var indexGap = header.ReadUInt32();
        var count = header.ReadUInt32();
        var root = header.ReadPath();
        Root = root;
        IndexGap = indexGap;
        var indexOffset = checked(header.Position + indexGap);
        if (indexOffset > stream.Length)
            throw new InvalidDataException($"'{uri}' legacy index lies outside the archive.");
        // Every early index entry needs a length, four storage words and a 16-byte digest.
        if (count > (stream.Length - indexOffset) / 36)
            throw new InvalidDataException($"'{uri}' legacy entry count cannot fit in the archive.");
        var index = new Cursor(stream, indexOffset, PknFormat.IndexKey(uri));
        for (uint i = 0; i < count; i++)
        {
            var relative = index.ReadPath();
            var flags = index.ReadUInt32();
            var block = index.ReadUInt32();
            var size = index.ReadUInt32();
            var stored = index.ReadUInt32();
            var md5 = index.Read(16);
            if ((flags & ~7u) != 0 || size > maxMemberBytes || stored > maxMemberBytes)
                throw new InvalidDataException($"'{uri}' legacy entry {i} has unsupported flags or exceeds the member byte limit.");
            // cn-279 0x61D600 joins the root and relative name before deriving member keys.
            var path = root.Length == 0 ? relative : root + "/" + relative;
            if (path.Length == 0 || !_members.TryAdd(Archive.NormalizeMemberPath(path),
                    new PknEntry(path, flags, block, size, stored, md5)))
                throw new InvalidDataException($"'{uri}' contains an empty or duplicate legacy member path.");
            _entries.Add(new ArchiveEntry(Archive.NormalizeMemberPath(path), size));
        }
        DataOffset = checked((index.Position + 1023) / 1024 * 1024);
        foreach (var member in _members.Values)
        {
            var end = checked(DataOffset + (long)member.Block * 1024 + (((long)member.StoredSize + 3) & ~3L));
            if (end > stream.Length || ((member.Flags & PknFormat.FlagZlib) == 0 && member.Size > member.StoredSize))
                throw new InvalidDataException($"'{uri}' member '{member.Path}' extends outside its stored data.");
        }
    }

    /// <summary>Preserved first header word whose purpose is not established.</summary>
    public uint Unknown0 { get; }

    /// <summary>Preserved second header word whose purpose is not established.</summary>
    public uint Unknown1 { get; }

    /// <summary>Original root used to resolve the index's relative member names.</summary>
    public string Root { get; }

    /// <summary>Padding between the decoded header and the encrypted index.</summary>
    public uint IndexGap { get; }

    /// <summary>Gets the original, case-sensitive URI used for header and index keys.</summary>
    public string Uri { get; }

    /// <summary>Gets the aligned member-data offset, measured from the beginning of the file.</summary>
    public long DataOffset { get; }

    /// <inheritdoc />
    public IReadOnlyList<ArchiveEntry> Entries => _entries;

    /// <summary>Opens an early pack, defaulting to the original file name under <c>Pack/</c>.</summary>
    /// <param name="filePath">The archive file.</param>
    /// <param name="clientRelativePath">The case-sensitive URI, such as <c>Pack/Config0000.pkn</c>.</param>
    /// <param name="maxMemberBytes">Maximum stored and unpacked byte length of a member.</param>
    public static LegacyPknArchive Open(string filePath, string? clientRelativePath = null, int maxMemberBytes = int.MaxValue)
    {
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Open(stream, (clientRelativePath ?? "Pack/" + Path.GetFileName(filePath)).Replace('\\', '/'), maxMemberBytes);
    }

    /// <summary>Opens an early pack and owns the stream, including on validation failure.</summary>
    /// <param name="stream">A readable, seekable stream.</param>
    /// <param name="uri">The case-sensitive, slash-separated URI used by the client.</param>
    /// <param name="maxMemberBytes">Maximum stored and unpacked byte length of a member.</param>
    public static LegacyPknArchive Open(Stream stream, string uri, int maxMemberBytes = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try { return new LegacyPknArchive(stream, uri, maxMemberBytes); }
        catch { stream.Dispose(); throw; }
    }

    /// <inheritdoc />
    public bool Contains(string path) => _members.ContainsKey(Archive.NormalizeMemberPath(path));

    internal StoredPknMember ReadStored(string path)
    {
        var member = _members[Archive.NormalizeMemberPath(path)];
        var bytes = new byte[checked((int)(((long)member.StoredSize + 3) & ~3L))];
        lock (_stream)
        {
            _stream.Position = checked(DataOffset + (long)member.Block * 1024);
            _stream.ReadExactly(bytes);
        }
        return new(member.Path, member.Flags, member.Size, member.StoredSize, member.Md5, bytes);
    }

    /// <inheritdoc />
    public byte[] Read(string path)
    {
        if (!_members.TryGetValue(Archive.NormalizeMemberPath(path), out var member))
            throw new FileNotFoundException($"'{Uri}' has no member '{path}'.", path);
        var padded = checked((int)(((long)member.StoredSize + 3) & ~3L));
        if (padded > _maxMemberBytes + 3L)
            throw new InvalidDataException("Legacy member exceeds the configured byte limit.");
        var bytes = new byte[padded];
        lock (_stream)
        {
            _stream.Position = DataOffset + (long)member.Block * 1024;
            _stream.ReadExactly(bytes);
        }
        if ((member.Flags & PknFormat.FlagHeadEncrypted) != 0)
            new Snow2Cipher(PknFormat.MemberKey(member.Path, member.Md5)).Decrypt(bytes.AsSpan(0, Math.Min(1024, padded)));
        if ((member.Flags & PknFormat.FlagEncrypted) != 0)
            new Snow2Cipher(PknFormat.MemberKey(member.Path, member.Md5)).Decrypt(bytes);
        byte[] result;
        if ((member.Flags & PknFormat.FlagZlib) != 0)
        {
            using var zlib = new ZLibStream(new MemoryStream(bytes, 0, checked((int)member.StoredSize)), CompressionMode.Decompress);
            result = new byte[member.Size];
            zlib.ReadExactly(result);
            if (zlib.ReadByte() != -1)
                throw new InvalidDataException($"'{Uri}' member '{member.Path}' expands beyond its declared size.");
        }
        else result = bytes.AsSpan(0, checked((int)member.Size)).ToArray();
        if (!PknFormat.MemberDigest(result).AsSpan().SequenceEqual(member.Md5))
            throw new InvalidDataException($"'{Uri}' member '{member.Path}' fails its MD5 check.");
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();

    private sealed class Cursor(Stream stream, long offset, byte[] key)
    {
        private readonly Snow2Cipher _cipher = new(key);
        private readonly byte[] _word = new byte[4];
        private int _available;
        private long _next = offset;
        internal long Position { get; private set; } = offset;

        internal byte[] Read(int count)
        {
            if (count < 0 || Position > stream.Length - count)
                throw new InvalidDataException("Legacy PKN index extends beyond the archive.");
            var result = new byte[count];
            for (var i = 0; i < count; i++)
            {
                if (_available == 0)
                {
                    stream.Position = _next;
                    stream.ReadExactly(_word);
                    _next += 4;
                    _cipher.Decrypt(_word);
                    _available = 4;
                }
                result[i] = _word[4 - _available--];
            }
            Position += count;
            return result;
        }

        internal uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Read(4));

        internal string ReadPath()
        {
            var length = ReadUInt32();
            if (length > int.MaxValue / 2 || (long)length * 2 > stream.Length - Position)
                throw new InvalidDataException("Legacy PKN path extends beyond the archive.");
            return Encoding.Unicode.GetString(Read(checked((int)length * 2)));
        }
    }
}
