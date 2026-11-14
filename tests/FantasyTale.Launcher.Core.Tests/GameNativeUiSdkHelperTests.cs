using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GameNativeUiSdkHelperTests
{
    private const string Resource = "Ui/DlgCreateRoom.ui";
    private static readonly GameNativeUiWaitOptions Wait = new(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
    private static GameNativeUiRequest Inspect(string id = "op1") => new(id, "control.inspect", "");
    private static GameNativeUiForm Form(long sequence, int x = 10, int? selection = 3, long clicks = 0, bool visible = true) =>
        new(Resource, sequence, visible, x, 20, 434, 648, 800, 600, selection, clicks);

    [Fact]
    public async Task InvokeRetriesAnUnsubmittedRequestAndReadsTheResultUntilItLeavesTheQueue()
    {
        var native = new Scripted
        {
            Invokes = { new("op1", "busy", Reason: "channelBusy"), new("op1", "queued") },
            Results = { null, new("op1", "queued"), new("op1", "completed") },
        };
        var result = await native.InvokeUntilDoneAsync(Inspect(), Wait);
        Assert.Equal("completed", result.Status);
        Assert.Equal(2, native.InvokeCount);
        Assert.Equal(3, native.ResultCount);
    }

    [Fact]
    public async Task InvokeNeverSubmitsAnAdmittedOperationAgainWhenItsReplyIsLate()
    {
        var native = new Scripted { Invokes = { new("op1", "queued") }, QueuedForever = true };
        var result = await native.InvokeUntilDoneAsync(Inspect(), new(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(5)));
        Assert.Equal(GameNativeUiInvocation.OutcomeUnknown, result.Status);
        Assert.Equal("nativeReplyUnavailable", result.Reason);
        Assert.Equal(1, native.InvokeCount);
    }

    [Fact]
    public async Task InvokeReturnsAStillBusyChannelOnceTheBudgetRunsOut()
    {
        var native = new Scripted { BusyForever = true };
        var result = await native.InvokeUntilDoneAsync(Inspect(), new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5)));
        Assert.Equal("busy", result.Status);
        Assert.True(native.InvokeCount > 1);
    }

    [Fact]
    public async Task InvokeRejectsInvalidRequestsAndBudgetsBeforeSubmitting()
    {
        var native = new Scripted();
        await Assert.ThrowsAsync<ArgumentException>(() => native.InvokeUntilDoneAsync(new("bad id", "control.inspect", ""), Wait));
        await Assert.ThrowsAsync<ArgumentException>(() => native.InvokeUntilDoneAsync(Inspect(), new(TimeSpan.Zero, TimeSpan.FromMilliseconds(1))));
        await Assert.ThrowsAsync<ArgumentException>(() => native.InvokeUntilDoneAsync(Inspect(), new(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1))));
        Assert.Equal(0, native.InvokeCount);
    }

    [Fact]
    public async Task WatcherReportsOpenMoveSelectionClickAndCloseWithOneSessionPerForm()
    {
        var native = new Scripted();
        var watcher = new GameNativeUiFormWatcher(native, Resource);
        native.Forms = [Form(1)];
        var opened = await watcher.ObserveAsync();
        Assert.Equal(GameNativeUiFormWatchStatus.Open, opened.Status);
        Assert.Equal(GameNativeUiFormChanges.Opened, opened.Changes);
        Assert.NotNull(opened.SessionId);

        Assert.Equal(GameNativeUiFormChanges.None, (await watcher.ObserveAsync()).Changes);
        native.Forms = [Form(1, x: 50, selection: 7, clicks: 2)];
        var changed = await watcher.ObserveAsync();
        Assert.Equal(GameNativeUiFormChanges.Moved | GameNativeUiFormChanges.SelectionChanged | GameNativeUiFormChanges.Clicked, changed.Changes);
        Assert.Equal(opened.SessionId, changed.SessionId);

        // The game loading the dialog again is a new form, so actions from the old view no longer match its session.
        native.Forms = [Form(2, x: 50, selection: 7, clicks: 2)];
        var reopened = await watcher.ObserveAsync();
        Assert.Equal(GameNativeUiFormChanges.Opened | GameNativeUiFormChanges.Closed, reopened.Changes);
        Assert.NotEqual(opened.SessionId, reopened.SessionId);

        native.Forms = [Form(2, visible: false)];
        var closed = await watcher.ObserveAsync();
        Assert.Equal(GameNativeUiFormWatchStatus.NotOpen, closed.Status);
        Assert.Equal(GameNativeUiFormChanges.Closed, closed.Changes);
        Assert.Null(closed.SessionId);
        Assert.Null(watcher.Form);
    }

    [Fact]
    public async Task WatcherFollowsTheNewestVisibleFormAndKeepsItsStateOverTransientFailures()
    {
        var native = new Scripted { Forms = [Form(4), Form(9, x: 70), Form(12, visible: false)] };
        var watcher = new GameNativeUiFormWatcher(native, Resource);
        var first = await watcher.ObserveAsync();
        Assert.Equal(9, first.Form!.Sequence);

        native.FormsFailure = new GameNativeUiBusyException();
        var busy = await watcher.ObserveAsync();
        Assert.Equal((GameNativeUiFormWatchStatus.Busy, GameNativeUiFormChanges.None, first.SessionId), (busy.Status, busy.Changes, busy.SessionId));
        native.FormsFailure = new IOException("pipe");
        Assert.Equal(GameNativeUiFormWatchStatus.ReadFailed, (await watcher.ObserveAsync()).Status);
        native.FormsFailure = null;
        Assert.Equal(GameNativeUiFormChanges.None, (await watcher.ObserveAsync()).Changes);
        Assert.Equal(first.SessionId, watcher.SessionId);
    }

    [Fact]
    public async Task WatcherPausesWithoutReadingAndStopsForGoodWhenFormsAreUnsupported()
    {
        var native = new Scripted { Forms = [Form(1)] };
        var watcher = new GameNativeUiFormWatcher(native, Resource);
        var session = (await watcher.ObserveAsync()).SessionId;
        var reads = native.FormReads;
        var paused = await watcher.ObserveAsync(watch: false);
        Assert.Equal((GameNativeUiFormWatchStatus.Dormant, GameNativeUiFormChanges.Closed), (paused.Status, paused.Changes));
        Assert.Equal(reads, native.FormReads);
        Assert.NotEqual(session, (await watcher.ObserveAsync()).SessionId);

        native.FormsFailure = new NotSupportedException();
        Assert.Equal(GameNativeUiFormWatchStatus.Unsupported, (await watcher.ObserveAsync()).Status);
        native.FormsFailure = null;
        reads = native.FormReads;
        Assert.Equal(GameNativeUiFormWatchStatus.Unsupported, (await watcher.ObserveAsync()).Status);
        Assert.Equal(reads, native.FormReads);
    }

    [Fact]
    public async Task WatcherLoopDeliversOnlyChangesAndStopsOnCancellation()
    {
        var native = new Scripted { Forms = [Form(1)] };
        var watcher = new GameNativeUiFormWatcher(native, Resource);
        using var stop = new CancellationTokenSource();
        var seen = new List<GameNativeUiFormChanges>();
        var run = watcher.RunAsync(TimeSpan.FromMilliseconds(2), observation =>
        {
            seen.Add(observation.Changes);
            if (observation.Changes.HasFlag(GameNativeUiFormChanges.Opened)) native.Forms = [];
            else stop.Cancel();
            return ValueTask.CompletedTask;
        }, cancellationToken: stop.Token);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([GameNativeUiFormChanges.Opened, GameNativeUiFormChanges.Closed], seen);
    }

    [Fact]
    public void RegionIsFormRelativeInClientPixels()
    {
        Assert.Equal(new OverlayAnchor(15, 643, 141, 27), Form(1, x: 10).Region(5, 623, 141, 27));
        Assert.Throws<ArgumentOutOfRangeException>(() => Form(1).Region(0, 0, 0, 27));
    }

    private sealed class Scripted : IGameNativeUi
    {
        public List<GameNativeUiResult> Invokes { get; } = [];
        // A null entry makes that result read throw a busy rejection.
        public List<GameNativeUiResult?> Results { get; } = [];
        public bool QueuedForever { get; init; }
        public bool BusyForever { get; init; }
        public IReadOnlyList<GameNativeUiForm> Forms { get; set; } = [];
        public Exception? FormsFailure { get; set; }
        public int InvokeCount { get; private set; }
        public int ResultCount { get; private set; }
        public int FormReads { get; private set; }

        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default)
        {
            var index = InvokeCount++;
            if (BusyForever) return ValueTask.FromResult(new GameNativeUiResult(request.OperationId, "busy", Reason: "channelBusy"));
            return ValueTask.FromResult(index < Invokes.Count ? Invokes[index] : Invokes[^1]);
        }

        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default)
        {
            var index = ResultCount++;
            if (QueuedForever) return ValueTask.FromResult(new GameNativeUiResult(operationId, "queued"));
            var result = index < Results.Count ? Results[index] : Results[^1];
            return result is null ? throw new GameNativeUiBusyException() : ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<GameNativeUiForm>> GetFormsAsync(string resource, CancellationToken cancellationToken = default)
        {
            FormReads++;
            if (FormsFailure is { } failure) throw failure;
            return ValueTask.FromResult(Forms);
        }
    }
}
