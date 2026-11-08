using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using FantasyTale.Launcher.Overlay;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    private const string Document = "https://overlay.fantasytale.local/index.html?theme=v2";
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        var application = new Application();
        var browser = new WebView2();
        var window = new Window { Width = 640, Height = 480, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, Content = browser };
        var exitCode = 1;
        var root = Directory.CreateTempSubdirectory("ft-webview-policy-").FullName;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await RunAsync(browser, root);
                Console.WriteLine($"PASS WebView policy: {_checks} checks, {RuntimeInformation.ProcessArchitecture}, browser {browser.CoreWebView2.Environment.BrowserVersionString}");
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                browser.Dispose();
                window.Close();
                application.Shutdown();
            }
        };
        application.Run(window);
        try { Directory.Delete(root, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Console.WriteLine("Temporary browser profile remains locked after disposal."); }
        return exitCode;
    }

    private static async Task RunAsync(WebView2 browser, string root)
    {
        var files = Directory.CreateDirectory(Path.Combine(root, "pages")).FullName;
        await File.WriteAllTextAsync(Path.Combine(files, "index.html"), "<!doctype html><title>Policy fixture</title><body>fixture</body>");
        await File.WriteAllTextAsync(Path.Combine(files, "other.html"), "<!doctype html><title>Other fixture</title><body>other</body>");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile"));
        await browser.EnsureCoreWebView2Async(environment);
        var core = browser.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("overlay.fantasytale.local", files, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("other.fantasytale.local", files, CoreWebView2HostResourceAccessKind.Allow);
        var accepted = new List<string>();
        var rejected = new List<string>();

        // A foreign document already loaded before attachment exercises the actual event's Source gate.
        await NavigateAsync(core, "https://other.fantasytale.local/index.html");
        var policy = new OverlayWebViewPolicy(core, Document, rejected.Add);
        core.WebMessageReceived += (_, args) => { if (policy.TryReadMessage(args, out var message)) accepted.Add(message); };
        await core.ExecuteScriptAsync("chrome.webview.postMessage('foreign-message')");
        await UntilAsync(() => rejected.Contains("message"));
        Check(accepted.Count == 0, "Foreign event source reached the host.");

        await NavigateAsync(core, Document);
        await SendAndReceiveAsync(core, accepted, "trusted");
        var messagesBefore = rejected.Count;
        await core.ExecuteScriptAsync("chrome.webview.postMessage({type:'object-message'})");
        await UntilAsync(() => rejected.Count > messagesBefore);
        Check(accepted.Count == 1, "A non-string message reached the host.");

        await core.ExecuteScriptAsync("location.hash = '/settings'");
        await SendAndReceiveAsync(core, accepted, "hash-route");
        await NavigateAsync(core, Document);
        await SendAndReceiveAsync(core, accepted, "reload");
        Check(await core.ExecuteScriptAsync("fetch('other.html').then(r => r.ok)") != "false", "Fixture script evaluation failed.");
        await core.ExecuteScriptAsync("fetch('other.html').then(r => chrome.webview.postMessage(r.ok ? 'resource-ok' : 'resource-failed'))");
        await UntilAsync(() => accepted.Contains("resource-ok") || accepted.Contains("resource-failed"));
        Check(accepted.Contains("resource-ok"), "Local subresources stopped loading.");

        foreach (var target in new[] {
            "https://overlay.fantasytale.local/other.html", "https://overlay.fantasytale.local/",
            "https://overlay.fantasytale.local/index.html?theme=v3", "https://other.fantasytale.local/index.html",
            "https://overlay.fantasytale.local:444/index.html?theme=v2", "about:blank",
            "data:text/html,foreign", new Uri(Path.Combine(files, "other.html")).AbsoluteUri })
        {
            var before = rejected.Count;
            core.Navigate(target);
            await UntilAsync(() => rejected.Count > before);
            Check(rejected[^1] == "navigation", "A prohibited top-level navigation was not canceled.");
            await SendAndReceiveAsync(core, accepted, "still-trusted-" + _checks);
        }

        var popupBefore = rejected.Count;
        await core.ExecuteScriptAsync("window.open('https://other.fantasytale.local/index.html')");
        await UntilAsync(() => rejected.Count > popupBefore);
        Check(rejected[^1] == "popup", "A popup request was not handled.");

        // The settings frame has the same opaque-origin sandbox used by the Launcher.
        await core.ExecuteScriptAsync("""
            window.addEventListener('message', e => {
              if (e.source === document.querySelector('iframe').contentWindow && e.origin === 'null')
                chrome.webview.postMessage('sandbox:' + e.data);
            });
            const frame = document.createElement('iframe'); frame.sandbox = 'allow-scripts';
            frame.srcdoc = `<script>let blocked=false;try{parent.chrome.webview.postMessage('escaped-frame')}catch{blocked=true}parent.postMessage(blocked?'isolated':'escaped','*')<\/script>`;
            document.body.append(frame);
            """);
        await UntilAsync(() => accepted.Contains("sandbox:isolated") || accepted.Contains("sandbox:escaped"));
        Check(accepted.Contains("sandbox:isolated") && !accepted.Contains("escaped-frame"), "Sandboxed frame reached the full bridge.");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await SendAndReceiveAsync(core, accepted, "after-gc");
    }

    private static async Task SendAndReceiveAsync(CoreWebView2 core, List<string> accepted, string message)
    {
        await core.ExecuteScriptAsync("chrome.webview.postMessage(" + JsonSerializer.Serialize(message) + ")");
        await UntilAsync(() => accepted.Contains(message));
        Check(accepted.Count(value => value == message) == 1, "Trusted message was lost or duplicated.");
    }

    private static async Task NavigateAsync(CoreWebView2 core, string target)
    {
        var completion = new TaskCompletionSource<bool>();
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completion.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(target);
            Check(await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)), "Fixture document navigation failed.");
        }
        finally { core.NavigationCompleted -= Completed; }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Expected browser event was not observed.");
            await Task.Delay(20);
        }
    }

    private static void Check(bool success, string error)
    {
        _checks++;
        if (!success) throw new InvalidOperationException(error);
    }
}
