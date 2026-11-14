using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Overlay;

/// <summary>
/// Entry point. Parses the game window handle from the command line and opens the overlay window.
/// Usage: FantasyTale.Launcher.Overlay.exe &lt;hwnd-decimal&gt;
/// When no HWND is supplied the window starts in free-floating demo mode.
/// </summary>
public partial class App : Application
{
    private NamedPipeClientStream? _pipe;
    private OverlayHost? _overlayHost;
    private CancellationTokenSource? _pipeLifetime;
    private static readonly SemaphoreSlim PipeWriteGate = new(1, 1);
    private const int MaximumFrameBytes = 256 * 1024;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // The launcher's Photino profile can have incompatible browser options.
        // An inherited override would replace the overlay's own user-data folder.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", null);
        base.OnStartup(e);

        nint gameHwnd = IntPtr.Zero;
        var options = ParseArguments(e.Args);
        if (options.TryGetValue("game-hwnd", out var rawHwnd) && long.TryParse(rawHwnd, out var parsedHwnd))
            gameHwnd = (nint)parsedHwnd;
        else if (e.Args.Length > 0 && long.TryParse(e.Args[0], out parsedHwnd))
            gameHwnd = (nint)parsedHwnd;

        var overlayHost = new OverlayHost();
        _overlayHost = overlayHost;
        var theme = options.GetValueOrDefault("theme", "v2");
        if (theme is not ("v1" or "v2" or "v3")) throw new ArgumentException("The overlay theme must be v1, v2 or v3.");
        var resourceRoot = options.TryGetValue("resource-root", out var resourceDirectory)
            ? Path.GetFullPath(resourceDirectory) : null;
        if (resourceRoot is not null && !Directory.Exists(resourceRoot))
            throw new DirectoryNotFoundException("The overlay resource directory does not exist.");
        var boundGamePid = options.TryGetValue("game-pid", out var boundPidText) && uint.TryParse(boundPidText, out var boundPid)
            ? boundPid : 0;
        overlayHost.Attach(gameHwnd, theme, resourceRoot,
            options.TryGetValue("preview", out var preview) && preview == "game-ui", boundGamePid,
            options.TryGetValue("locale", out var locale) && OverlayLocales.IsKnown(locale) ? locale : null);
        if (options.TryGetValue("pipe", out var pipeName) && options.TryGetValue("launch-id", out var launchId)
            && options.TryGetValue("protocol", out var protocol) && int.TryParse(protocol, out var version)
            && options.TryGetValue("game-pid", out var gamePidText) && int.TryParse(gamePidText, out var gamePid))
        {
            var pipeLifetime = new CancellationTokenSource();
            _pipeLifetime = pipeLifetime;
            overlayHost.PanelMessageReceived += (_, args) => _ = SendPageEventAsync(args, pipeLifetime.Token);
            overlayHost.Ready += (_, _) => _ = SendReadyAsync(pipeLifetime.Token);
            _ = ConnectAsync(pipeName, launchId, version, gamePid, pipeLifetime.Token);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _pipeLifetime?.Cancel();
        _overlayHost?.Dispose();
        _pipe?.Dispose();
        _pipeLifetime?.Dispose();
        base.OnExit(e);
    }

    private async Task ConnectAsync(string pipeName, string launchId, int protocol, int gamePid, CancellationToken cancellationToken)
    {
        try
        {
            if (pipeName.Length is < 1 or > 200 || launchId.Length != 32 || protocol != 1) return;
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10000, cancellationToken).ConfigureAwait(false);
            _pipe = pipe;
            OverlayLifecycleLog.Write("pipe-connected");
            await WriteFrameAsync(pipe, JsonSerializer.Serialize(new { type = "hello", protocol, launchId, gamePid }), cancellationToken).ConfigureAwait(false);
            var ready = await ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
            using (var document = JsonDocument.Parse(ready))
            {
                var root = document.RootElement;
                if (root.GetProperty("type").GetString() != "ready" || root.GetProperty("protocol").GetInt32() != protocol)
                    throw new IOException("Invalid overlay handshake response.");
            }
            if (_overlayHost?.IsReady == true) await SendReadyAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var command = await ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
                await HandleCommandAsync(pipe, command, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            OverlayLifecycleLog.Write("pipe-ended", new { errorType = error.GetType().Name });
            _pipe?.Dispose();
            _pipe = null;
            _ = Dispatcher.BeginInvoke(() => Shutdown());
        }
    }

    private async Task SendReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_pipe is { } pipe)
                await WriteFrameAsync(pipe, "{\"type\":\"renderer-ready\"}", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task HandleCommandAsync(Stream pipe, string raw, CancellationToken cancellationToken)
    {
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            requestId = root.TryGetProperty("requestId", out var request) ? request.GetString() : null;
            if (requestId is not null && requestId.Length is < 1 or > 64) throw new ArgumentException("Invalid request id.");
            if (type is null) throw new ArgumentException("Missing command type.");
            OverlayLifecycleLog.Write("command-received", new { type });
            var host = _overlayHost ?? throw new InvalidOperationException("Overlay host is unavailable.");
            switch (type)
            {
                case "build-context":
                    host.SetBuildContext(root.GetProperty("context").Deserialize<OverlayBuildContext>()
                        ?? throw new ArgumentException("Missing build context."));
                    await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "package-roots":
                    {
                        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var entry in root.GetProperty("roots").EnumerateObject())
                            roots.Add(entry.Name, entry.Value.GetString() ?? throw new ArgumentException("Missing package folder."));
                        host.SetPackageRoots(roots);
                        await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                case "register":
                    {
                        var panelId = root.GetProperty("panelId").GetString() ?? throw new ArgumentException("Missing panel id.");
                        var descriptor = new OverlayPanelDescriptor
                        {
                            InlineHtml = root.TryGetProperty("html", out var html) && html.ValueKind != JsonValueKind.Null ? html.GetString() : null,
                            HtmlFileName = root.TryGetProperty("fileName", out var file) && file.ValueKind != JsonValueKind.Null ? file.GetString() : null,
                            Layer = root.TryGetProperty("layer", out var layer) ? layer.GetInt32() : 0,
                            Interactive = root.TryGetProperty("interactive", out var interactive) && interactive.GetBoolean(),
                            Modal = root.TryGetProperty("modal", out var modal) && modal.GetBoolean(),
                            PassThroughOutside = root.TryGetProperty("passThroughOutside", out var passThroughOutside) && passThroughOutside.GetBoolean(),
                            Backdrop = root.TryGetProperty("backdrop", out var backdrop) && backdrop.GetBoolean(),
                            BackdropColor = root.TryGetProperty("backdropColor", out var backdropColor) && backdropColor.ValueKind == JsonValueKind.String
                                ? backdropColor.GetString() : null,
                        };
                        _ = host.RegisterPanel(panelId, descriptor);
                        await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                case "unregister":
                    host.UnregisterPanel(root.GetProperty("panelId").GetString() ?? throw new ArgumentException("Missing panel id."));
                    await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "show":
                    host.ShowPanel(root.GetProperty("panelId").GetString() ?? throw new ArgumentException("Missing panel id."));
                    await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "hide":
                    host.HidePanel(root.GetProperty("panelId").GetString() ?? throw new ArgumentException("Missing panel id."));
                    await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                    break;
                case "send":
                    {
                        var panelId = root.GetProperty("panelId").GetString() ?? throw new ArgumentException("Missing panel id.");
                        var name = root.GetProperty("name").GetString() ?? throw new ArgumentException("Missing event name.");
                        var data = root.TryGetProperty("data", out var dataElement) ? dataElement : default;
                        host.SendToPanel(panelId, name, data);
                        await WriteResponseAsync(pipe, requestId, true, null, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                default: throw new ArgumentException("Unknown overlay command.");
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
        {
            await WriteResponseAsync(pipe, requestId, false, error.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendPageEventAsync(OverlayMessageEventArgs args, CancellationToken cancellationToken)
    {
        try
        {
            var pipe = _pipe;
            if (pipe is null || cancellationToken.IsCancellationRequested) return;
            await WriteFrameAsync(pipe, JsonSerializer.Serialize(new
            {
                type = "panel-event", panelId = args.PanelId, name = args.EventName, data = args.Data,
            }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            _ = error;
        }
    }

    private static async Task WriteResponseAsync(Stream pipe, string? requestId, bool ok, string? error, CancellationToken cancellationToken) =>
        await WriteFrameAsync(pipe, JsonSerializer.Serialize(new { type = "response", requestId, ok, error }), cancellationToken).ConfigureAwait(false);

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
            if (args[i].StartsWith("--", StringComparison.Ordinal) && args[i].Length > 2) result[args[i][2..]] = args[i + 1];
        return result;
    }

    private static async Task WriteFrameAsync(Stream stream, string body, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(body);
        if (bytes.Length is < 1 or > MaximumFrameBytes) throw new IOException("Overlay message is too large.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await PipeWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { PipeWriteGate.Release(); }
    }

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes) throw new IOException("Invalid overlay message size.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
