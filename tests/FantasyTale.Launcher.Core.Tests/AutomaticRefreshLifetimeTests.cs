using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AutomaticRefreshLifetimeTests
{
    [Fact]
    public async Task CandidateRejectionConsumesRevisionAndNextSnapshotUsesANewerRevision()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var two = Signal(); var count = 0;
        await using var pipe = new ControlStream(revision =>
        {
            if (Interlocked.Increment(ref count) == 2) two.TrySetResult();
            return Ack(revision, revision == 1 ? "rejected" : "accepted", revision == 1 ? "candidates" : "updated");
        });
        using var writes = new SemaphoreSlim(1, 1);
        var pending = GatewayBootstrap.RefreshAutomaticAsync(new Selection(), new string('a', 32), pipe, Signal().Task,
            writes, null, cancellation.Token, TimeSpan.FromMilliseconds(1));
        await two.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([1L, 2L], pipe.Revisions.Take(2));
        Assert.Contains("stop", pipe.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishedAcknowledgementStopsRefreshesWithoutInterruptingNativeRedirect(bool admissionExpires)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var replied = Signal(); var exited = Signal();
        await using var pipe = new ControlStream(revision => { replied.TrySetResult(); return Ack(revision, "finished", "retired"); });
        using var writes = new SemaphoreSlim(1, 1);
        var pending = GatewayBootstrap.RefreshAutomaticAsync(new Selection(), new string('a', 32), pipe, exited.Task,
            writes, null, cancellation.Token, TimeSpan.FromMilliseconds(1));
        await replied.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(30, cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.Single(pipe.Revisions);
        Assert.DoesNotContain("stop", pipe.Commands);
        if (admissionExpires) await cancellation.CancelAsync();
        else exited.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain("stop", pipe.Commands);
    }

    [Fact]
    public async Task CancellationDuringPendingAckFinishesReaderBeforeSendingStop()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var sent = Signal();
        await using var pipe = new ControlStream(_ => { sent.TrySetResult(); return null; });
        using var writes = new SemaphoreSlim(1, 1);
        var pending = GatewayBootstrap.RefreshAutomaticAsync(new Selection(), new string('a', 32), pipe, Signal().Task,
            writes, null, cancellation.Token, TimeSpan.FromMilliseconds(1));
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancellation.CancelAsync();
        await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["refresh-automatic", "stop"], pipe.Commands);
        Assert.Equal(0, pipe.ConcurrentReads);
    }

    [Theory]
    [InlineData("accepted", "updated")]
    [InlineData("rejected", "grant-expired")]
    [InlineData("finished", "retired")]
    public async Task ClosedAdmissionWithdrawsCandidatesWithoutStoppingNativeCleanup(string status, string reason)
    {
        await using var pipe = new ControlStream(revision => Ack(revision, status, reason));
        using var writes = new SemaphoreSlim(1, 1);
        await GatewayBootstrap.RefreshAutomaticAsync(new Selection { Closed = true }, new string('a', 32), pipe, Signal().Task,
            writes, null, CancellationToken.None, TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["refresh-automatic"], pipe.Commands);
        Assert.Equal([1L], pipe.Revisions);
        Assert.Equal([0], pipe.CandidateCounts);
        Assert.DoesNotContain("stop", pipe.Commands);
    }

    [Fact]
    public async Task ExpiredAcknowledgementEndsHealthRefreshWithoutStoppingManualSessionCleanup()
    {
        await using var pipe = new ControlStream(revision => Ack(revision, "rejected", "grant-expired"));
        using var writes = new SemaphoreSlim(1, 1);
        await GatewayBootstrap.RefreshAutomaticAsync(new Selection(), new string('a', 32), pipe, Signal().Task,
            writes, null, CancellationToken.None, TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["refresh-automatic"], pipe.Commands);
        Assert.Equal([1L], pipe.Revisions);
    }

    [Fact]
    public async Task RedeemedTicketAfterHealthyRefreshWithdrawsWithoutStoppingAuxiliaryConnection()
    {
        var selection = new Selection();
        await using var pipe = new ControlStream(revision =>
        {
            if (revision == 1) selection.Closed = true;
            return Ack(revision, "accepted", "updated");
        });
        using var writes = new SemaphoreSlim(1, 1);
        await GatewayBootstrap.RefreshAutomaticAsync(selection, new string('a', 32), pipe, Signal().Task,
            writes, null, CancellationToken.None, TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal([1L, 2L], pipe.Revisions);
        Assert.Equal(["refresh-automatic", "refresh-automatic"], pipe.Commands);
        Assert.DoesNotContain("stop", pipe.Commands);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static byte[] Ack(long revision, string status, string reason) => JsonSerializer.SerializeToUtf8Bytes(new
    { type = "automatic-refresh-result", row_id = "selector", revision, status, reason });

    private sealed class Selection : IAutomaticServerSelection
    {
        public LaunchServerSelection Grant { get; } = AutomaticServerSelectionTests.Grant(DateTimeOffset.UtcNow);
        public IReadOnlyList<GameServerTarget> Targets => [];
        public ServerSelector Automatic => Grant.Servers[0];
        public IReadOnlyList<string> AdmittedCenterIds => ["2", "3"];
        internal bool Closed { get; set; }
        public Task<IReadOnlyList<AutomaticServerCandidate>?> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomaticServerCandidate>?>(Closed ? null : []);
    }

    private sealed class ControlStream(Func<long, byte[]?> response) : Stream
    {
        private readonly Channel<byte> _incoming = Channel.CreateBounded<byte>(65536);
        private readonly MemoryStream _written = new();
        private int _reads;
        internal ConcurrentQueue<string> Commands { get; } = new();
        internal ConcurrentQueue<long> Revisions { get; } = new();
        internal ConcurrentQueue<int> CandidateCounts { get; } = new();
        internal int ConcurrentReads { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) > 1) ConcurrentReads++;
            try
            {
                var first = await _incoming.Reader.ReadAsync(cancellationToken);
                buffer.Span[0] = first;
                var count = 1;
                while (count < buffer.Length && _incoming.Reader.TryRead(out var value)) buffer.Span[count++] = value;
                return count;
            }
            finally { Interlocked.Decrement(ref _reads); }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _written.Write(buffer.Span);
            var bytes = _written.GetBuffer().AsSpan(0, checked((int)_written.Length));
            if (bytes.Length < 4 || bytes.Length < 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes)) return ValueTask.CompletedTask;
            using var command = JsonDocument.Parse(bytes[4..].ToArray());
            var type = command.RootElement.GetProperty("type").GetString()!;
            Commands.Enqueue(type);
            if (type == "refresh-automatic")
            {
                var revision = command.RootElement.GetProperty("revision").GetInt64(); Revisions.Enqueue(revision);
                CandidateCounts.Enqueue(command.RootElement.GetProperty("candidates").GetArrayLength());
                if (response(revision) is { } body)
                {
                    var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
                    foreach (var value in header.Concat(body)) Assert.True(_incoming.Writer.TryWrite(value));
                }
            }
            _written.SetLength(0); _written.Position = 0;
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _written.Dispose(); _incoming.Writer.TryComplete(); }
            base.Dispose(disposing);
        }
    }
}
