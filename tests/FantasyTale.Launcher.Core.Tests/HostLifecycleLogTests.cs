using System.Diagnostics;
using System.Text.Json;
using FantasyTale.Launcher.Host.Window;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class HostLifecycleLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-controller-log-").FullName;
    private string ControllerPath => Path.Combine(_root, "overlay-controller.jsonl");

    [Fact]
    public void ControllerEvidenceSurvivesUnrelatedFrontendPolling()
    {
        var log = new HostLifecycleLog(_root);
        log.Record("mode-extension-controller", "unsupported-room-binding");
        log.Write("overlay-controller-start-failed", "fixture-InvalidOperationException");
        for (var i = 0; i < 1600; i++) log.Write("bridge-received", "plugins.list");

        var controller = File.ReadAllText(ControllerPath);
        Assert.Contains("unsupported-room-binding", controller, StringComparison.Ordinal);
        Assert.Contains("overlay-controller-start-failed", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("plugins.list", controller, StringComparison.Ordinal);
        Assert.Equal(2, File.ReadLines(ControllerPath).Count());
        var host = File.ReadAllText(Path.Combine(_root, "host-lifecycle.jsonl"))
            + File.ReadAllText(Path.Combine(_root, "host-lifecycle.jsonl.1"));
        Assert.DoesNotContain("unsupported-room-binding", host, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "launch-lifecycle.jsonl")));
    }

    [Fact]
    public void ControllerRotationRetainsAtMostTwoBoundedJsonFiles()
    {
        var log = new HostLifecycleLog(_root);
        log.Write("mode-extension-controller", "first-controller-record");
        for (var i = 0; i < 900; i++) log.Write("mode-extension-controller", new string('a', 120));
        log.Write("overlay-controller-stop-failed", "last-controller-record");

        var paths = Directory.GetFiles(_root, "overlay-controller.jsonl*");
        Assert.Equal(2, paths.Length);
        Assert.Contains(ControllerPath, paths);
        Assert.Contains(ControllerPath + ".1", paths);
        foreach (var path in paths)
        {
            Assert.InRange(new FileInfo(path).Length, 1, HostLifecycleLog.MaximumBytes);
            foreach (var line in File.ReadLines(path))
            {
                using var record = JsonDocument.Parse(line);
                Assert.Equal(4, record.RootElement.EnumerateObject().Count());
                Assert.True(record.RootElement.GetProperty("pid").GetInt32() > 0);
            }
        }
        Assert.Contains("last-controller-record", File.ReadAllText(ControllerPath), StringComparison.Ordinal);
        Assert.DoesNotContain("first-controller-record", string.Concat(paths.Select(File.ReadAllText)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mode-extension-controller", "password=private-value")]
    [InlineData("overlay-controller-start-failed", "exception text with content")]
    [InlineData("mode-extension-controller\nprivate-value", "failed")]
    public void InvalidNamesAndDetailsAreRejectedBeforeAnySinkReceivesThem(string name, string detail)
    {
        var log = new HostLifecycleLog(_root);
        Assert.Throws<ArgumentException>(() => log.Write(name, detail));
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Theory]
    [InlineData("mode-extension-controller", true)]
    [InlineData("overlay-controller-start-failed", true)]
    [InlineData("overlay-controller-stop-failed", true)]
    [InlineData("overlay-controller", true)]
    [InlineData("bridge-received", false)]
    [InlineData("mode-extension-controller-notification", false)]
    [InlineData("overlay-controllerish", false)]
    [InlineData("OVERLAY-CONTROLLER-start-failed", false)]
    public void OnlyDeclaredControllerEventFamiliesReachTheDedicatedSink(string name, bool dedicated)
    {
        var log = new HostLifecycleLog(_root);
        log.Write(name, "fixture");
        Assert.Equal(dedicated, File.Exists(ControllerPath));
    }

    [Fact]
    public void AnUnavailableHostSinkDoesNotSuppressControllerEvidence()
    {
        using var held = new FileStream(Path.Combine(_root, "host-lifecycle.jsonl"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var log = new HostLifecycleLog(_root);
        log.Write("mode-extension-controller", "unsupported-room-binding");
        Assert.Contains("unsupported-room-binding", File.ReadAllText(ControllerPath), StringComparison.Ordinal);
        Assert.Equal(0, held.Length);
    }

    [Theory]
    [InlineData("overlay-controller.jsonl")]
    [InlineData("overlay-controller.jsonl.1")]
    public async Task ControllerSinkRefusesLinkedCurrentAndRotationPaths(string filename)
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var sentinel = Path.Combine(target, "untouched.txt");
        File.WriteAllText(sentinel, "original");
        var link = Path.Combine(_root, filename);
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        }))
        {
            Assert.NotNull(process);
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            var log = new HostLifecycleLog(_root);
            log.Write("mode-extension-controller", "unsupported-room-binding");
            Assert.Equal("original", File.ReadAllText(sentinel));
            Assert.Single(Directory.EnumerateFiles(target));
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Assert.Contains("unsupported-room-binding", File.ReadAllText(Path.Combine(_root, "host-lifecycle.jsonl")), StringComparison.Ordinal);
            Assert.False(File.Exists(ControllerPath));
        }
        finally { Directory.Delete(link); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
