using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace FantasyTale.Launcher.Core.Stores;

internal sealed record NativeArchiveEntry(uint Index, string Path, long Size, bool Directory, bool Encrypted);

// Uses the 7-Zip 26.04 IArchive/IStream ABI; callbacks expose only manifest-pinned volumes.
[SupportedOSPlatform("windows")]
internal sealed class NativeArchiveDecoder : IDisposable
{
    private static readonly Lazy<nint> Library = new(() => NativeLibrary.Load(Path.Combine(
        AppContext.BaseDirectory, "archive", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "7z.dll")));
    private readonly IArchive _archive;
    private readonly Callback _callback;
    private readonly CancellationToken _ct;
    private bool _disposed;
    internal IReadOnlyList<NativeArchiveEntry> Entries { get; }

    internal NativeArchiveDecoder(string format, IReadOnlyList<string> names, IReadOnlyList<Stream> streams,
        string? password, CancellationToken ct)
    {
        _ct = ct;
        var create = Marshal.GetDelegateForFunctionPointer<CreateArchive>(NativeLibrary.GetExport(Library.Value, "CreateObject"));
        var handler = format switch
        {
            "zip" => "01", "7z" => "07",
            "rar" => IsRar5(streams[0]) ? "CC" : "03",
            _ => throw new InvalidDataException("Unsupported archive format.")
        };
        var cls = new Guid($"23170F69-40C1-278A-1000-000110{handler}0000");
        var iid = typeof(IArchive).GUID;
        Marshal.ThrowExceptionForHR(create(in cls, in iid, out _archive));
        _callback = new Callback(names, streams, password, ct);
        try
        {
            Stream input = format is "zip" or "7z" && streams.Count > 1 ? new VolumeStream(streams) : streams[0];
            input.Position = 0;
            ulong checkPosition = 0;
            var opened = _archive.Open(new Input(input, ct), ref checkPosition, _callback);
            ct.ThrowIfCancellationRequested();
            if (_callback.Error is { } error) throw error;
            if (opened != 0) throw _callback.PasswordRequested ? new ArchivePasswordRequiredException() : new InvalidDataException("Archive format, headers or volumes are invalid.");
            Marshal.ThrowExceptionForHR(_archive.Count(out var count));
            if (count == 0) throw new InvalidDataException("Archive contains no entries.");
            var entries = new List<NativeArchiveEntry>(checked((int)count));
            var paths = new Dictionary<string, (string Path, bool Directory)>(StringComparer.OrdinalIgnoreCase);
            var explicitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; index < count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var directory = Number(_archive, index, 6) != 0;
                var name = Text(_archive, index, 3).Replace('\\', '/');
                name = ClientRelease.ValidatePath(directory ? name.TrimEnd('/') : name);
                var size = Number(_archive, index, 7);
                if (size > long.MaxValue || directory && size != 0) throw new InvalidDataException("Invalid archive entry size.");
                var attributes = Number(_archive, index, 9);
                var unixType = ((attributes >> 16) | Number(_archive, index, 53)) & 0xf000;
                if ((attributes & (ulong)FileAttributes.ReparsePoint) != 0 ||
                    unixType != 0 && unixType != (directory ? 0x4000UL : 0x8000UL) ||
                    Text(_archive, index, 54).Length != 0 || Text(_archive, index, 90).Length != 0 ||
                    Number(_archive, index, 21) != 0 || Number(_archive, index, 63) != 0 || Number(_archive, index, 64) != 0 ||
                    Number(_archive, index, 65) != 0)
                    throw new InvalidDataException("Archive links, alternate streams and special entries are not supported.");
                var encrypted = Number(_archive, index, 15) != 0;
                if (encrypted && password is null) throw new ArchivePasswordRequiredException();
                if (!explicitPaths.Add(name)) throw new InvalidDataException("Duplicate archive path.");
                AddPath(name, directory);
                for (var at = name.IndexOf('/'); at >= 0; at = name.IndexOf('/', at + 1)) AddPath(name[..at], true);
                entries.Add(new(index, name, (long)size, directory, encrypted));
            }
            void AddPath(string path, bool directory)
            {
                if (paths.TryGetValue(path, out var previous) && (previous.Path != path || previous.Directory != directory))
                    throw new InvalidDataException("Archive paths conflict or alias another entry.");
                paths[path] = (path, directory);
            }
            Entries = entries;
        }
        catch { Dispose(); throw; }
    }

    private static bool IsRar5(Stream input)
    {
        input.Position = 6;
        var result = input.ReadByte() == 1;
        input.Position = 0;
        return result;
    }

    internal void Extract(Func<NativeArchiveEntry, string> destination, Action<NativeArchiveEntry, string> completed, Action<int> progress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _callback.Configure(Entries, destination, completed, progress);
        var result = _archive.Extract(0, uint.MaxValue, 0, _callback);
        _ct.ThrowIfCancellationRequested();
        if ((_callback.Error ?? _callback.OutputError) is { } error) throw error;
        if (result != 0) throw new InvalidDataException("Archive extraction failed.");
        if (_callback.Completed != Entries.Count(entry => !entry.Directory)) throw new InvalidDataException("Archive extraction was incomplete.");
        progress(100);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _callback.Dispose(); }
        finally
        {
            try { _ = _archive.Close(); }
            finally
            {
                Marshal.FinalReleaseComObject(_archive);
                GC.KeepAlive(_callback);
            }
        }
    }

    private static string Text(IArchive archive, uint index, uint property)
    {
        Marshal.ThrowExceptionForHR(archive.Property(index, property, out var value));
        if (value.Type == 0) return "";
        if (value.Type != 8) throw new InvalidDataException("Unexpected archive string property.");
        try { return Marshal.PtrToStringBSTR(value.Pointer); }
        finally { Marshal.FreeBSTR(value.Pointer); }
    }

    private static ulong Number(IArchive archive, uint index, uint property)
    {
        Marshal.ThrowExceptionForHR(archive.Property(index, property, out var value));
        return value.Type switch
        {
            0 => 0, 11 => value.Number != 0 ? 1UL : 0UL,
            19 => value.Number & uint.MaxValue, 21 => value.Number,
            _ => throw new InvalidDataException("Unexpected archive numeric property.")
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateArchive(in Guid cls, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IArchive archive);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct Variant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal ulong Number;
        [FieldOffset(8)] internal nint Pointer;
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000600600000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IArchive
    {
        [PreserveSig] int Open(IInput input, ref ulong searchLimit, IOpen callback);
        [PreserveSig] int Close();
        [PreserveSig] int Count(out uint count);
        [PreserveSig] int Property(uint index, uint property, out Variant value);
        [PreserveSig] int Extract(nint indices, uint count, int test, IExtract callback);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000300030000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInput
    {
        [PreserveSig] int Read(nint data, uint size, out uint processed);
        [PreserveSig] int Seek(long offset, uint origin, nint position);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000300020000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOutput
    {
        [PreserveSig] int Write(nint data, uint size, out uint processed);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000600100000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOpen
    {
        [PreserveSig] int SetTotal(nint files, nint bytes);
        [PreserveSig] int SetCompleted(nint files, nint bytes);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000600300000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IVolumes
    {
        [PreserveSig] int GetProperty(uint property, out Variant value);
        [PreserveSig] int GetStream([MarshalAs(UnmanagedType.LPWStr)] string name, out IInput? input);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000500100000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPassword
    {
        [PreserveSig] int CryptoGetTextPassword([MarshalAs(UnmanagedType.BStr)] out string password);
    }

    [ComImport, Guid("23170F69-40C1-278A-0000-000600200000"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExtract
    {
        [PreserveSig] int SetTotal(ulong total);
        [PreserveSig] int SetCompleted(nint completed);
        [PreserveSig] int GetStream(uint index, out IOutput? output, int mode);
        [PreserveSig] int PrepareOperation(int mode);
        [PreserveSig] int SetOperationResult(int result);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Input(Stream stream, CancellationToken ct) : IInput
    {
        private readonly byte[] _buffer = new byte[128 * 1024];
        public int Read(nint data, uint size, out uint processed)
        {
            processed = 0;
            if (ct.IsCancellationRequested) return unchecked((int)0x80004004);
            try
            {
                var read = stream.Read(_buffer, 0, (int)Math.Min(size, (uint)_buffer.Length));
                Marshal.Copy(_buffer, 0, data, read); processed = (uint)read; return 0;
            }
            catch (IOException) { return unchecked((int)0x80004005); }
        }
        public int Seek(long offset, uint origin, nint position)
        {
            if (origin > 2 || ct.IsCancellationRequested) return unchecked((int)0x80004004);
            try { var next = stream.Seek(offset, (SeekOrigin)origin); if (position != 0) Marshal.WriteInt64(position, next); return 0; }
            catch (IOException) { return unchecked((int)0x80004005); }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Callback(IReadOnlyList<string> names, IReadOnlyList<Stream> streams,
        string? password, CancellationToken ct) : IOpen, IVolumes, IPassword, IExtract, IDisposable
    {
        internal Exception? Error;
        internal Exception? OutputError => _output?.Error;
        internal bool PasswordRequested;
        internal int Completed;
        private Dictionary<uint, NativeArchiveEntry> _files = [];
        private readonly HashSet<uint> _seen = [];
        private Func<NativeArchiveEntry, string> _destination = null!;
        private Action<NativeArchiveEntry, string> _completed = null!;
        private Action<int> _progress = null!;
        private Output? _output;
        private NativeArchiveEntry? _current;
        private long _complete, _total;
        internal void Configure(IReadOnlyList<NativeArchiveEntry> entries, Func<NativeArchiveEntry, string> destination, Action<NativeArchiveEntry, string> completed, Action<int> progress)
        {
            _files = entries.Where(entry => !entry.Directory).ToDictionary(entry => entry.Index);
            _total = _files.Values.Sum(entry => entry.Size);
            _destination = destination; _completed = completed; _progress = progress;
        }
        private int Status => ct.IsCancellationRequested || Error is not null ? unchecked((int)0x80004004) : 0;
        public int SetTotal(nint files, nint bytes) => Status;
        public int SetCompleted(nint files, nint bytes) => Status;
        public int SetTotal(ulong total) => Status;
        public int SetCompleted(nint completed) => Status;
        public int PrepareOperation(int mode) => Status;
        public int CryptoGetTextPassword(out string text)
        {
            PasswordRequested = true; text = password ?? "";
            if (password is null) Error = new ArchivePasswordRequiredException();
            return Status;
        }
        public int GetProperty(uint property, out Variant value)
        {
            value = default;
            if (property == 4) value = new() { Type = 8, Pointer = Marshal.StringToBSTR(names[0]) };
            return Status;
        }
        public int GetStream(string name, out IInput? input)
        {
            input = null;
            for (var i = 0; i < names.Count; i++)
                if (string.Equals(name, names[i], StringComparison.OrdinalIgnoreCase))
                { streams[i].Position = 0; input = new Input(streams[i], ct); return Status; }
            return 1;
        }
        public int GetStream(uint index, out IOutput? output, int mode)
        {
            output = null;
            if (_output is null) _current = null;
            if (Status != 0 || mode != 0 || !_files.TryGetValue(index, out var entry)) return Status;
            try
            {
                if (_output is not null || !_seen.Add(index)) throw new InvalidDataException("Unexpected duplicate or concurrent archive output.");
                _current = entry;
                _output = new Output(_destination(entry), entry.Size,
                    value => _progress(_total == 0 ? 0 : (int)Math.Min(99, (double)(_complete + value) * 100 / _total)), ct);
                output = _output; return 0;
            }
            // Managed consumer failures must return through Extract, not cross the COM boundary.
            catch (Exception error)
            { Error = error; return Status; }
        }
        public int SetOperationResult(int result)
        {
            try
            {
                if (_output?.Error is { } writeError) throw writeError;
                if (result != 0) throw (_current?.Encrypted == true || PasswordRequested) && result is 2 or 3 or 9
                    ? new ArchivePasswordRequiredException() : new InvalidDataException("Archive decoder rejected an entry or its checksum.");
                if (_output is null) return Status;
                var hash = _output.Finish();
                _output.Dispose(); _output = null;
                _completed(_current!, hash); _complete += _current!.Size; Completed++;
                return Status;
            }
            catch (Exception error)
            { Error = error; return Status; }
        }
        public void Dispose()
        {
            if (_output is null) return;
            var pending = _output;
            _output = null;
            try { pending.Dispose(); }
            finally { File.Delete(pending.Path); }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Output : IOutput, IDisposable
    {
        private readonly FileStream _stream;
        private readonly IncrementalHash _hash;
        private readonly byte[] _buffer = new byte[128 * 1024];
        private readonly long _size;
        private readonly Action<long> _progress;
        private readonly CancellationToken _ct;
        internal string Path { get; }
        internal Exception? Error;
        internal Output(string path, long size, Action<long> progress, CancellationToken ct)
        {
            Path = path; _size = size; _progress = progress; _ct = ct;
            _stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try { _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); }
            catch { _stream.Dispose(); throw; }
        }
        public int Write(nint data, uint size, out uint processed)
        {
            processed = 0;
            if (_ct.IsCancellationRequested) return unchecked((int)0x80004004);
            try
            {
                var count = (int)Math.Min(size, (uint)_buffer.Length);
                if (_stream.Length + count > _size) throw new InvalidDataException("Archive output exceeds its declared size.");
                Marshal.Copy(data, _buffer, 0, count); _hash.AppendData(_buffer, 0, count); _stream.Write(_buffer, 0, count);
                processed = (uint)count; _progress(_stream.Length); return 0;
            }
            catch (Exception error) { Error = error; return unchecked((int)0x80004005); }
        }
        internal string Finish()
        {
            if (_stream.Length != _size) throw new InvalidDataException("Archive output is incomplete.");
            _stream.Flush(true); return Convert.ToHexStringLower(_hash.GetHashAndReset());
        }
        public void Dispose()
        {
            try { _stream.Dispose(); }
            finally { _hash.Dispose(); }
        }
    }
}
