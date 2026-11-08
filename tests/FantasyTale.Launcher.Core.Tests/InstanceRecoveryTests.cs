using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed class InstanceRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-client-recovery-").FullName;
    private readonly InstanceStore _store;
    private readonly GameDefinition _game;

    public InstanceRecoveryTests()
    {
        _store = new(Path.Combine(_root, "instances"));
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
        var build = new BuildDefinition("test-1", "test", 1, name, "FantasyTale.Launcher.TestClient.exe", "", "", null, null, "none", [], []);
        _game = new("test", GameStatus.Enabled, name, "test.dll", "external", "", null, "", [build]);
    }

    private async Task<string> Client(bool executable = false)
    {
        await _store.CreateAsync(new("sample", "Sample", "test", "test-1", null, []), executable ? Path.Combine(AppContext.BaseDirectory, "test-client") : null);
        return _store.ClientFolder("sample");
    }

    private void WriteSession(string client, string identity = "sample") =>
        File.WriteAllText(Path.Combine(client, "session.toml"), SessionWriter.Render(new("test", _game.Builds[0], identity, Path.Combine(_root, "log"), "account", "private-ticket", null, [])));

    [Fact]
    public async Task SessionOnlyRecoveryRequiresConfirmationAndExclusiveLease()
    {
        var client = await Client(); WriteSession(client);
        Assert.True(InstanceRecovery.Required(client));
        Assert.Throws<InvalidOperationException>(() => InstanceRecovery.Recover(_store, "sample", _game, false));
        using (var lease = _store.Acquire("sample")) Assert.Throws<InstanceBusyException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        Assert.True(File.Exists(Path.Combine(client, "session.toml")));
        InstanceRecovery.Recover(_store, "sample", _game, true);
        Assert.False(InstanceRecovery.Required(client));
    }

    [Fact]
    public async Task RestartRecoveryCleansStoppedSessionWithoutUserPrompt()
    {
        var client = await Client(); WriteSession(client);
        Assert.True(InstanceRecovery.TryRecoverStopped(_store, "sample", _game));
        Assert.False(InstanceRecovery.Required(client));
        Assert.False(File.Exists(Path.Combine(client, SessionWriter.FileName)));
    }

    [Fact]
    public async Task LargeReadableSessionStillRequiresMatchingIdentityBeforeRecovery()
    {
        var client = await Client(); WriteSession(client, "different");
        var path = Path.Combine(client, SessionWriter.FileName);
        var padding = "\n# " + new string('a', 256 * 1024) + "\n";
        File.AppendAllText(path, padding);
        Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        Assert.True(File.Exists(path));
        WriteSession(client); File.AppendAllText(path, padding);
        InstanceRecovery.Recover(_store, "sample", _game, true);
        Assert.False(InstanceRecovery.Required(client));
    }

    [Fact]
    public async Task SessionDirectoryCannotBeReportedAsSuccessfulRecovery()
    {
        var client = await Client();
        Directory.CreateDirectory(Path.Combine(client, "session.toml"));
        Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        Assert.True(InstanceRecovery.Required(client));
    }

    [Fact]
    public async Task WrongIdentityAndMalformedSessionArePreservedWithoutSecretErrors()
    {
        var client = await Client(); WriteSession(client, "different");
        Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        File.WriteAllText(Path.Combine(client, "session.toml"), "private-ticket = \"unterminated");
        var error = Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        Assert.DoesNotContain("private-ticket", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(client, "session.toml")));
    }

    [Fact]
    public async Task OverlayRecoveryPreservesExternalChangesThenRestoresOriginalAndClearsSession()
    {
        var client = await Client(); WriteSession(client);
        File.WriteAllText(Path.Combine(client, "host.dll"), "original");
        var package = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        var bytes = Encoding.UTF8.GetBytes("new-host");
        File.WriteAllBytes(Path.Combine(package, "host.dll"), bytes);
        File.WriteAllText(Path.Combine(package, "runtime.toml"), $$"""
            schema = 1
            [runtime]
            game = "test"
            version = "3.1.0"
            session_schema = 2
            [[files]]
            path = "host.dll"
            size = {{bytes.Length}}
            sha256 = "{{Convert.ToHexStringLower(SHA256.HashData(bytes))}}"
            """);
        _ = RuntimeDeployment.Apply(client, "test", new(package, [new("host.dll", "host.dll")]));
        await Assert.ThrowsAsync<IOException>(() => _store.CloneAsync("sample", "copy", "Copy"));
        File.WriteAllText(Path.Combine(client, "host.dll"), "external-change");
        Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
        Assert.True(File.Exists(Path.Combine(client, "session.toml")));
        Assert.True(File.Exists(Path.Combine(client, ".fantasytale-runtime", "journal.json")));
        File.WriteAllText(Path.Combine(client, "host.dll"), "new-host");
        InstanceRecovery.Recover(_store, "sample", _game, true);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.False(InstanceRecovery.Required(client));
    }

    [Fact]
    public async Task UntrackedLiveGamePreventsRecoveryEvenWithoutAnInstanceLease()
    {
        var client = await Client(executable: true); WriteSession(client);
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(client, _game.Builds[0].Executable)) { WorkingDirectory = client, UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
            Assert.True(File.Exists(Path.Combine(client, "session.toml")));
        }
        finally
        {
            File.WriteAllText(Path.Combine(client, "release"), "exit");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        InstanceRecovery.Recover(_store, "sample", _game, true);
        Assert.False(InstanceRecovery.Required(client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryChecksImageIdentityBeforeTheProcessLoaderInitializes(bool matchingClient)
    {
        if (!OperatingSystem.IsWindows()) return;
        var client = await Client(executable: true); WriteSession(client);
        var directory = matchingClient ? client : Path.Combine(AppContext.BaseDirectory, "test-client");
        using (var process = new SuspendedTestProcess(Path.Combine(directory, _game.Builds[0].Executable)))
        {
            if (matchingClient)
            {
                var error = Assert.Throws<IOException>(() => InstanceRecovery.Recover(_store, "sample", _game, true));
                Assert.Equal("Close the game before recovering its client files.", error.Message);
                Assert.True(InstanceRecovery.Required(client));
                Assert.True(File.Exists(Path.Combine(client, SessionWriter.FileName)));
            }
            else
            {
                InstanceRecovery.Recover(_store, "sample", _game, true);
                Assert.False(InstanceRecovery.Required(client));
            }
        }
        InstanceRecovery.Recover(_store, "sample", _game, true);
        Assert.False(InstanceRecovery.Required(client));
    }

    [Fact]
    public async Task StoppedOrphanRegistrationRecoversOnlyWithAVerifiedExecutableAndDeploymentJournal()
    {
        var client = await Client(executable: true);
        WriteSession(client, "retired-probe");
        var staging = Directory.CreateDirectory(Path.Combine(client, ".fantasytale-runtime")).FullName;
        var payload = Encoding.UTF8.GetBytes("temporary native host");
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        File.WriteAllBytes(Path.Combine(client, "temporary.dll"), payload);
        File.WriteAllText(Path.Combine(staging, "journal.json"), $$"""
            [{"Target":"temporary.dll","Hash":"{{hash}}","OriginalHash":null,"Slot":0,"KeepInstalled":false}]
            """);
        Assert.True(InstanceRecovery.TryRecoverStopped(_store, "sample", _game));
        Assert.False(InstanceRecovery.Required(client));
        Assert.False(File.Exists(Path.Combine(client, "temporary.dll")));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
