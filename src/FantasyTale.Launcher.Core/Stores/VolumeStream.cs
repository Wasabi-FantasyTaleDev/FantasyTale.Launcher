namespace FantasyTale.Launcher.Core.Stores;

// Presents pinned byte-split ZIP/7z volumes as one seekable input without copying their contents.
internal sealed class VolumeStream : Stream
{
    private readonly IReadOnlyList<Stream> _streams;
    private readonly long[] _starts;
    private long _position;
    internal VolumeStream(IReadOnlyList<Stream> streams)
    {
        _streams = streams; _starts = new long[streams.Count + 1];
        for (var i = 0; i < streams.Count; i++) _starts[i + 1] = checked(_starts[i] + streams[i].Length);
    }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _starts[^1];
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("Invalid input buffer range.");
        if (_position >= Length || count == 0) return 0;
        var volume = 0;
        while (_starts[volume + 1] <= _position) volume++;
        var stream = _streams[volume];
        stream.Position = _position - _starts[volume];
        var read = stream.Read(buffer, offset, (int)Math.Min(count, _starts[volume + 1] - _position));
        if (read == 0) throw new EndOfStreamException("Archive volume ended before its pinned length.");
        _position += read; return read;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0) throw new IOException("Cannot seek before archive start.");
        return _position = position;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
