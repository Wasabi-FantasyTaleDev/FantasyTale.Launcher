using System.Drawing;
using System.Globalization;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Host.Window;
using Photino.NET;

namespace FantasyTale.Launcher.Host;

/// <summary>Entry point: one Photino window that hosts the web frontend.</summary>
internal static class Program
{
    // Windows WebView navigation needs a standard scheme; resource interception stays local.
    private static readonly string Scheme = OperatingSystem.IsWindows() ? "https" : "app";
    private static readonly string AssetOrigin = OperatingSystem.IsWindows() ? "https://fantasytale.invalid/" : "app://localhost/";
    private static string ApplicationVersion => LauncherCompatibility.CurrentVersion;

    [STAThread]
    // Keep the actual executable entry point synchronous so the runtime applies STA before WebView initialization.
    private static void Main(string[] args)
    {
        NativeWindow.SetApplicationIdentity();
        var baseDir = AppContext.BaseDirectory;
        var webRoot = Path.Combine(baseDir, "wwwroot");
        var gamesRoot = Path.Combine(baseDir, "games");
        var configuredDataRoot = args.SkipWhile(a => a != "--data-root").Skip(1).FirstOrDefault();
        var dataRoot = configuredDataRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FantasyTale", "Launcher");
        if (OperatingSystem.IsWindows() && configuredDataRoot is not null)
        {
            var webViewRoot = Path.Combine(Path.GetFullPath(dataRoot), "webview");
            Directory.CreateDirectory(webViewRoot);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewRoot);
        }
        using var single = OperatingSystem.IsWindows() ? new SingleInstance(dataRoot) : null;
        if (single is { IsPrimary: false }) return;
        var lifecycle = new HostLifecycleLog(dataRoot);
        lifecycle.Write("startup", ApplicationVersion);
        lifecycle.Write("architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
        lifecycle.Write("apartment", Thread.CurrentThread.GetApartmentState().ToString());
        var healthPipe = args.SkipWhile(a => a != "--update-health").Skip(1).FirstOrDefault();
        if (healthPipe is null && OperatingSystem.IsWindows())
        {
            try
            {
                if (LauncherUpdateHandoff.PrepareRecoveryAsync(baseDir, dataRoot, CancellationToken.None).GetAwaiter().GetResult()) return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
            { LauncherUpdateHandoff.ShowRecoveryFailure(); return; }
        }
        var backend = new LauncherBackend(gamesRoot, dataRoot, lifecycle);
        var helperArgument = args.SkipWhile(a => a != "--update-helper").Skip(1).FirstOrDefault();
        backend.SetUpdateStartup(healthPipe is not null);
        var devServer = args.SkipWhile(a => a != "--dev-server").Skip(1).FirstOrDefault();

        var window = new PhotinoWindow()
            .SetLogVerbosity(0)
            .SetTitle("FantasyTale Launcher")
            .SetIconFile(Path.Combine(baseDir, "Assets", "launcher.ico"))
            .SetUseOsDefaultSize(false)
            .SetUseOsDefaultLocation(false)
            .SetSize(new Size(1280, 760))
            .SetMinSize(960, 600)
            .SetChromeless(true)
            .SetTransparent(true)
            .SetResizable(true)
            .SetContextMenuEnabled(false)
            .SetDevToolsEnabled(devServer is not null)
            .Center();

        var frontendReady = false;
        var startupComplete = false;
        var closing = false;
        TrayIcon? tray = null;
        var exitOperation = new WindowExitOperation(async () =>
        {
            lifecycle.Write("exit-stopping");
            try { await backend.StopAllAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                lifecycle.Write("exit-failed", error.GetType().Name);
                throw;
            }
            lifecycle.Write("exit-cleaned");
        }, () => window.Invoke(() =>
        {
            Volatile.Write(ref closing, true);
            lifecycle.Write("exit-close-requested");
            try { window.Close(); }
            catch { Volatile.Write(ref closing, false); throw; }
        }));
        async void ExitApplication()
        {
            if (tray is not null && !tray.ConfirmExit(backend.HasRunningClients)) return;
            try
            {
                await exitOperation.RequestAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { window.Invoke(() => { tray?.Show(); tray?.Error(); }); }
        }
        var bridge = new BridgeRouter(message =>
        {
            // Resize events precede WebView creation; client exits can outlive the window.
            if (!Volatile.Read(ref frontendReady) || Volatile.Read(ref closing)) return;
            window.Invoke(() =>
            {
                if (!closing) window.SendWebMessage(message);
            });
        }, (method, phase) => lifecycle.Write("bridge-" + phase, method));
        RegisterMethods(bridge, window, gamesRoot, dataRoot, backend.Games);
        backend.Register(bridge);
        bridge.AddWindow("window.exit", async (_, _) => { await exitOperation.RequestAsync().ConfigureAwait(false); return null; });
        bridge.AddWindow("window.ready", async (_, ct) =>
        {
            if (Volatile.Read(ref startupComplete)) return null;
            if (healthPipe is not null)
            {
                if (!int.TryParse(helperArgument, out var helperId)) throw new InvalidOperationException("Missing update helper identity.");
                await LauncherUpdateHandoff.AcknowledgeAsync(healthPipe, helperId, ct).ConfigureAwait(false);
            }
            Volatile.Write(ref startupComplete, true); backend.FrontendReady(); lifecycle.Write("frontend-ready"); return null;
        });
        bridge.Add("launcherUpdates.restart", async (p, ct) =>
        {
            if (p is not null && p.AsObject().Any(pair => pair.Key is not ("manifestSha256" or "stopRunningClients"))) throw new ArgumentException("Unknown update restart parameter.");
            var hash = p?["manifestSha256"]?.GetValue<string>();
            var stopClients = p?["stopRunningClients"]?.GetValue<bool>() ?? false;
            if (stopClients && (hash is not { Length: 64 } || !hash.All(char.IsAsciiHexDigit))) throw new ArgumentException("A verified update selection is required.");
            await backend.PrepareUpdateRestartAsync(ct, hash, stopClients).ConfigureAwait(false);
            window.Invoke(() => { Volatile.Write(ref closing, true); lifecycle.Write("update-close-requested"); window.Close(); });
            return null;
        });
        bridge.AddWindow("window.language", p => { var language = p?["language"]?.GetValue<string>(); if (language is not null) window.Invoke(() => { if (tray is not null) tray.Language = language; }); return null; });
        window.RegisterWebMessageReceivedHandler((_, message) =>
        {
            Volatile.Write(ref frontendReady, true);
            _ = bridge.DispatchAsync(message, CancellationToken.None);
        });
        window.RegisterWindowClosingHandler((_, _) =>
        {
            if (!closing && !Volatile.Read(ref startupComplete))
            {
                lifecycle.Write("close-during-startup");
                // Closing during initialization cancels startup instead of hiding an unfinished window.
                ExitApplication();
                return true;
            }
            if (!closing && tray is not null) { lifecycle.Write("close-to-tray"); tray.Hide(); return true; }
            Volatile.Write(ref closing, true);
            lifecycle.Write("close-accepted");
            return false;
        });
        window.RegisterCustomSchemeHandler(Scheme, (object _, string _, string url, out string contentType) =>
            StaticFiles.Open(webRoot, gamesRoot, AssetOrigin, url, out contentType, Path.Combine(dataRoot, "server-media")));
        window.RegisterWindowCreatedHandler((_, _) =>
        {
            lifecycle.Write("window-created");
            WindowChrome.Attach(window.WindowHandle);
            lifecycle.Write("window-chrome-attached");
            if (WindowChrome.InitialBounds(window.WindowHandle) is { } bounds)
            {
                window.SetMinSize(bounds.Minimum.Width, bounds.Minimum.Height);
                window.SetSize(bounds.Size);
                window.MoveTo(bounds.Location, allowOutsideWorkArea: true);
            }
            Backdrop.Apply(window.WindowHandle, Backdrop.Kind.Mica, NativeWindow.AppsUseDarkTheme());
            lifecycle.Write("window-backdrop-applied");
            NativeWindow.ShowBeforeWebViewInitialization(window.WindowHandle);
            lifecycle.Write("window-shown");
            if (OperatingSystem.IsWindows())
            {
                tray = new TrayIcon(window.WindowHandle, () => ExitApplication());
                single!.Listen(() =>
                {
                    lifecycle.Write("activation-requested");
                    if (!Volatile.Read(ref closing)) window.Invoke(() => tray.Show());
                });
            }
            lifecycle.Write("window-initialized");
        });
        window.WindowMaximizedHandler += (_, _) => bridge.Emit("window.state", new { maximized = true });
        window.WindowRestoredHandler += (_, _) => bridge.Emit("window.state", new { maximized = NativeWindow.IsMaximized(window.WindowHandle) });

        // The string overload treats custom schemes as filesystem paths.
        window.Load(new Uri(devServer ?? AssetOrigin + "index.html", UriKind.Absolute));
        window.WaitForClose();
        lifecycle.Write("window-loop-ended");
        tray?.Dispose();
        // Keep the process monitor alive after the window closes so tickets are removed on client exit.
        try { backend.DrainAsync().GetAwaiter().GetResult(); lifecycle.Write("shutdown-completed"); }
        catch (Exception error) { lifecycle.Write("shutdown-failed", error.GetType().Name); throw; }
    }

    private static void RegisterMethods(BridgeRouter bridge, PhotinoWindow window, string gamesRoot, string dataRoot, Func<IReadOnlyList<FantasyTale.Launcher.Sdk.GameDefinition>> games)
    {
        bridge.Add("app.info", _ => new
        {
            version = ApplicationVersion,
            language = CultureInfo.CurrentUICulture.Name,
            os = Environment.OSVersion.VersionString,
        });
        bridge.AddWindow("window.minimize", _ => { window.Invoke(() => NativeWindow.Minimize(window.WindowHandle)); return null; });
        bridge.AddWindow("window.toggleMaximize", _ =>
        {
            var maximized = false;
            window.Invoke(() => maximized = NativeWindow.ToggleMaximize(window.WindowHandle));
            return maximized;
        });
        bridge.AddWindow("window.state", _ => { var maximized = false; window.Invoke(() => maximized = NativeWindow.IsMaximized(window.WindowHandle)); return new { maximized }; });
        bridge.AddWindow("window.close", _ => { window.Invoke(() => window.Close()); return null; });
        bridge.AddWindow("window.startDrag", _ => { window.Invoke(() => NativeWindow.BeginDrag(window.WindowHandle)); return null; });
        bridge.Add("shell.pickFolder", p =>
        {
            string? selected = null;
            window.Invoke(() => selected = window.ShowOpenFolder(p?["title"]?.GetValue<string>() ?? "Select client folder").FirstOrDefault());
            return selected;
        });
        bridge.Add("shell.pickPluginZip", p =>
        {
            string? selected = null;
            window.Invoke(() => selected = window.ShowOpenFile(p?["title"]?.GetValue<string>() ?? "Select plugin package", multiSelect: false,
                filters: [("ZIP", ["zip"])]).FirstOrDefault());
            return selected;
        });
        bridge.Add("shell.pickPluginScheme", async (p, ct) =>
        {
            string? selected = null;
            window.Invoke(() => selected = window.ShowOpenFile(p?["title"]?.GetValue<string>() ?? "Import plugin scheme", multiSelect: false,
                filters: [("TOML", ["toml"])]).FirstOrDefault());
            return selected is null ? null : await File.ReadAllTextAsync(selected, ct).ConfigureAwait(false);
        });
        bridge.Add("shell.savePluginScheme", async (p, ct) =>
        {
            var text = p?["text"]?.GetValue<string>() ?? throw new ArgumentException("Scheme text is required.");
            var suggested = p?["fileName"]?.GetValue<string>() ?? "plugin-scheme.toml";
            if (Path.GetFileName(suggested) != suggested || !suggested.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid scheme file name.");
            string? selected = null;
            window.Invoke(() => selected = window.ShowSaveFile(p?["title"]?.GetValue<string>() ?? "Export plugin scheme", defaultPath: suggested, filters: [("TOML", ["toml"])]));
            if (string.IsNullOrEmpty(selected)) return false;
            if (!selected.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) selected += ".toml";
            await File.WriteAllTextAsync(selected, text, ct).ConfigureAwait(false);
            return true;
        });
        bridge.Add("shell.pickServerProfile", async (p, ct) =>
        {
            string? selected = null;
            window.Invoke(() => selected = window.ShowOpenFile(p?["title"]?.GetValue<string>() ?? "Import server profile", multiSelect: false,
                filters: [("TOML", ["toml"])]).FirstOrDefault());
            if (selected is null) return null;
            return await Core.Servers.ServerProfileFile.ReadAsync(selected, ct).ConfigureAwait(false);
        });
        bridge.Add("shell.saveServerProfile", async (p, ct) =>
        {
            var text = p?["text"]?.GetValue<string>() ?? throw new ArgumentException("Profile text is required.");
            var profile = Core.Servers.ServerProfileFile.Parse("export.toml", text);
            string? selected = null;
            var suggested = p?["fileName"]?.GetValue<string>() ?? Core.Servers.ServerProfileFile.FileName(profile);
            if (Path.GetFileName(suggested) != suggested || !suggested.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid profile file name.");
            window.Invoke(() => selected = window.ShowSaveFile(p?["title"]?.GetValue<string>() ?? "Export server profile", defaultPath: suggested,
                filters: [("TOML", ["toml"])]));
            if (string.IsNullOrEmpty(selected)) return false;
            if (!selected.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) selected += ".toml";
            await Core.Servers.ServerProfileFile.ExportAsync(selected, text, ct).ConfigureAwait(false);
            return true;
        });
        bridge.Add("shell.importServerImages", async (p, ct) =>
        {
            string[] selected = [];
            window.Invoke(() => selected = window.ShowOpenFile(p?["title"]?.GetValue<string>() ?? "Select server images",
                multiSelect: p?["multiple"]?.GetValue<bool>() ?? false,
                filters: [("Images", ["png", "jpg", "jpeg", "webp", "gif", "bmp", "ico"])]) ?? []);
            if (selected.Length == 0) return Array.Empty<string>();
            var artwork = new Core.Servers.ServerArtworkStore(Path.Combine(dataRoot, "server-media"));
            var references = new List<string>();
            foreach (var path in selected) references.Add(await artwork.ImportAsync(path, ct).ConfigureAwait(false));
            return references.Distinct(StringComparer.Ordinal).ToArray();
        });
        bridge.AddWindow("window.theme", p =>
        {
            var dark = p?["dark"]?.GetValue<bool>() ?? NativeWindow.AppsUseDarkTheme();
            window.Invoke(() => Backdrop.Apply(window.WindowHandle, Backdrop.Kind.Mica, dark));
            return null;
        });
        bridge.Add("shell.openExternal", p =>
        {
            var value = p?["url"]?.GetValue<string>();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ArgumentException("Only HTTP(S) links can be opened.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return null;
        });
        bridge.Add("games.list", _ => games().Select(g => new
        {
            id = g.Id,
            unsupportedRegions = g.UnsupportedRegions,
            status = g.Status.ToString().ToLowerInvariant(),
            supportsInstances = g.SupportsInstanceSelection, supportsPlugins = g.SupportsPlugins,
            navigationEntries = g.NavigationEntries,
            roomsEnabled = g.RoomsEnabled,
            localHostingEnabled = g.LocalHostingEnabled,
            launcherLogin = g.LauncherLogin, profileFields = g.ProfileFields, levelIcon = g.LevelIcon,
            clientLanguages = g.ClientLanguages, bundledInstances = g.BundledInstances,
            levels = g.Levels is null ? null : new { entries = g.Levels.Entries.Select(e => new { e.Level, minimumExperience = e.MinimumExperience.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Name, e.Icons }), g.Levels.Badges, g.Levels.DisplayOffset, g.Levels.NamesPending },
            assetBase = $"{AssetOrigin}games/{Path.GetFileName(g.Folder)}/",
            homeOrder = g.HomeOrder,
            heroTitle = g.HeroTitle,
            preparing = g.Preparing,
            serviceNotice = g.ServiceNotice?.Values,
            name = g.Name.Values,
            accent = g.Accent,
            // Media is shipped with releases but not with source checkouts; omit absent files like videos.
            backgroundImage = g.BackgroundImage is null || !File.Exists(g.BackgroundImage) ? null : $"{AssetOrigin}games/{Path.GetFileName(g.Folder)}/{Path.GetRelativePath(g.Folder, g.BackgroundImage).Replace('\\', '/')}",
            backgroundVideo = g.BackgroundVideo is null || !File.Exists(g.BackgroundVideo) ? null : $"{AssetOrigin}games/{Path.GetFileName(g.Folder)}/{Path.GetRelativePath(g.Folder, g.BackgroundVideo).Replace('\\', '/')}",
            backgroundVideos = g.BackgroundVideos.Where(File.Exists).Select(path => $"{AssetOrigin}games/{Path.GetFileName(g.Folder)}/{Path.GetRelativePath(g.Folder, path).Replace('\\', '/')}").ToArray(),
            backgroundAudioTracks = g.BackgroundAudioTracks.Select(path => path is null ? null : $"{AssetOrigin}games/{Path.GetFileName(g.Folder)}/{Path.GetRelativePath(g.Folder, path).Replace('\\', '/')}").ToArray(),
            builds = g.Builds.Where(b => b.Enabled && !b.Hidden).Select(b => new { id = b.Id, branch = b.Branch, region = b.Region, protocolVersion = b.ProtocolVersion, buildDate = b.BuildDate?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), releaseDate = b.ReleaseDate, name = b.Name.Values,
                releaseVersion = b.ReleaseVersion, patchVersion = b.PatchVersion, description = b.Description?.Values, nativeLanguage = b.NativeLanguage, additionalLanguages = b.AdditionalLanguages, launcherLogin = b.LauncherLogin,
                login = b.Login, defaultPort = b.DefaultPort, supportsHosting = b.HostingAdapter.Length > 0, requiresAccount = b.Arguments.Any(a => a.Contains("{account}", StringComparison.Ordinal)) }),
        }));
    }
}
