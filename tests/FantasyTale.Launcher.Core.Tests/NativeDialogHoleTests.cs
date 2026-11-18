using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class NativeDialogHoleTests
{
    private static GameNativeUiForm Form(string resource, bool visible, int x, int y, int width, int height, long sequence = 1) =>
        new(resource, sequence, visible, x, y, width, height, 1024, 768);

    [Fact]
    public void HolesCoverVisibleFormsWithAMargin()
    {
        var holes = OverlayNativeHole.FromForms([
            Form("a.ui", true, 100, 50, 300, 200),
            Form("b.ui", false, 0, 0, 50, 50),
            Form("c.ui", true, 0, 0, 0, 40)], 6);
        Assert.Equal([new OverlayNativeHole(94, 44, 312, 212)], holes);
    }

    [Fact]
    public void HolesAreOrderedDistinctAndBounded()
    {
        var forms = Enumerable.Range(0, 40).Select(i => Form("a.ui", true, 40 - i, 10, 5, 5, i + 1)).Append(Form("a.ui", true, 1, 10, 5, 5, 99));
        var holes = OverlayNativeHole.FromForms(forms);
        Assert.Equal(OverlayNativeHole.MaximumCount, holes.Length);
        Assert.Equal(holes.OrderBy(hole => hole.X).ToArray(), holes);
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayNativeHole.FromForms([], -1));
    }

    [Fact]
    public void OnlyBuildsWithConfirmedMessageBoxesGetResources()
    {
        var module = new BubbleFighterModule();
        Assert.Equal(["Ui/DlgMessageBox.ui", "Ui/DlgMessageBoxOk.ui", "Ui/DlgCustomMessageBox.ui", "Ui/DlgCommon/DlgCommonNotice.ui"],
            module.MessageBoxResources("kr-1256"));
        Assert.Contains("Ui/DlgMessageBox.ui", module.MessageBoxResources("kr-812"));
        Assert.Empty(module.MessageBoxResources("id-400"));
        Assert.Empty(module.MessageBoxResources("no-such-build"));
    }

    [Fact]
    public async Task WatcherSendsHolesWhileASurfaceIsShownAndClearsThemAfterwards()
    {
        var overlay = new FakeOverlay { Shown = true };
        var native = new FakeNative();
        native.Forms["Ui/DlgMessageBox.ui"] = [Form("Ui/DlgMessageBox.ui", true, 100, 100, 200, 100)];
        await using var watcher = new NativeDialogHoleWatcher(overlay, native, ["Ui/DlgMessageBox.ui", "Ui/DlgMessageBoxOk.ui"], TimeSpan.FromMilliseconds(10));
        Assert.Equal([new OverlayNativeHole(94, 94, 212, 112)], await overlay.NextAsync());

        native.Forms["Ui/DlgMessageBox.ui"] = [];
        Assert.Empty(await overlay.NextAsync());

        // Forms reappear, then the overlay stops showing anything: the holes are cleared and polling stops.
        native.Forms["Ui/DlgMessageBox.ui"] = [Form("Ui/DlgMessageBox.ui", true, 10, 10, 20, 20, 2)];
        Assert.Single(await overlay.NextAsync());
        overlay.Shown = false;
        Assert.Empty(await overlay.NextAsync());
        var reads = native.Reads;
        await Task.Delay(100);
        Assert.True(native.Reads - reads <= 2);
    }

    [Fact]
    public async Task WatcherStopsQuietlyWhenTheBuildDoesNotTrackForms()
    {
        var overlay = new FakeOverlay { Shown = true };
        var native = new FakeNative { Unsupported = true };
        await using var watcher = new NativeDialogHoleWatcher(overlay, native, ["Ui/DlgMessageBox.ui"], TimeSpan.FromMilliseconds(10));
        await Task.Delay(100);
        Assert.Equal(1, native.Reads);
        Assert.Empty(overlay.Sent);
    }

    [Fact]
    public async Task ABusyChannelKeepsTheHolesAndAFailureRemovesThem()
    {
        var overlay = new FakeOverlay { Shown = true };
        var native = new FakeNative();
        native.Forms["Ui/DlgMessageBox.ui"] = [Form("Ui/DlgMessageBox.ui", true, 10, 10, 20, 20)];
        await using var watcher = new NativeDialogHoleWatcher(overlay, native, ["Ui/DlgMessageBox.ui"], TimeSpan.FromMilliseconds(10));
        Assert.Single(await overlay.NextAsync());
        native.Busy = true;
        await Task.Delay(80);
        Assert.Single(overlay.Sent);
        native.Busy = false;
        native.Fail = true;
        Assert.Empty(await overlay.NextAsync());
    }

    [Fact]
    public void WatchingRequiresAResource()
    {
        Assert.Throws<ArgumentException>(() => new NativeDialogHoleWatcher(new FakeOverlay(), new FakeNative(), []));
    }

    private sealed class FakeOverlay : IOverlayHost
    {
        private readonly System.Collections.Concurrent.BlockingCollection<OverlayNativeHole[]> _queue = [];
        private bool _shown;
        public List<OverlayNativeHole[]> Sent { get; } = [];

        public bool Shown
        {
            get => _shown;
            set
            {
                _shown = value;
                PresentationChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public OverlayPresentation Presentation => _shown
            ? new(true, 1024, 768, [new OverlaySurface(0, 0, 100, 100, true, false)]) : OverlayPresentation.Hidden;
        public event EventHandler? PresentationChanged;
        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) => throw new NotSupportedException();

        public Task SetNativeHolesAsync(IReadOnlyList<OverlayNativeHole> holes, CancellationToken cancellationToken = default)
        {
            var copy = holes.ToArray();
            lock (Sent) Sent.Add(copy);
            _queue.Add(copy, CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task<OverlayNativeHole[]> NextAsync() => Task.Run(() => _queue.TryTake(out var holes, TimeSpan.FromSeconds(5))
            ? holes : throw new TimeoutException("The watcher sent no holes."));
    }

    private sealed class FakeNative : IGameNativeUi
    {
        private int _reads;
        public Dictionary<string, GameNativeUiForm[]> Forms { get; } = new(StringComparer.Ordinal);
        public bool Unsupported { get; set; }
        public bool Busy { get; set; }
        public bool Fail { get; set; }
        public int Reads => Volatile.Read(ref _reads);

        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GameNativeUiForm>> GetFormsAsync(string resource, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            if (Unsupported) throw new NotSupportedException();
            if (Busy) throw new GameNativeUiBusyException();
            if (Fail) throw new IOException("read failed");
            return ValueTask.FromResult<IReadOnlyList<GameNativeUiForm>>(Forms.TryGetValue(resource, out var forms) ? forms : []);
        }
    }
}
