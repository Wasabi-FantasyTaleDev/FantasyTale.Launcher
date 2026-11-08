using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class HookStatusTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-readiness-").FullName;
    private const string Report = """
        schema = 1
        launch_id = "0123456789abcdef0123456789abcdef"
        game = "fixture"
        build = "test-1"
        instance = "sample"
        pid = 123
        process_started = 987654321
        runtime = "3.2.0"
        state = "ready"
        error = ""
        [[plugins]]
        id = "test.required"
        version = "1.0.0"
        outcome = "installed"
        required = true
        """;
    private static SessionSpec Expected => new("fixture", new BuildDefinition("test-1", "test", 1,
        new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" }), "fixture.exe", "standin", "", null, null, "", [], []),
        "sample", "runtime.log", null, null, null, [new("test.required", "1.0.0", "plugins", true, new Dictionary<string, object>())])
        { LaunchId = "0123456789abcdef0123456789abcdef" };

    private HookLaunchStatus Read(string report, SessionSpec? expected = null)
    {
        var path = Path.Combine(_root, "status.toml");
        File.WriteAllText(path, report);
        return HookStatusReader.Read(path, expected ?? Expected, 123, 987654321, "3.2.0");
    }

    [Fact]
    public void CompleteReportAcceptsOnlyThisLaunch() => Assert.Equal("installed", Assert.Single(Read(Report).Plugins).Outcome);

    [Fact]
    public void BootstrapCompletionRequiresThisReadyLaunch()
    {
        Assert.True(Read("bootstrap_complete = true\n" + Report).BootstrapComplete);
        Assert.False(Read(Report).BootstrapComplete);
        Assert.Throws<IOException>(() => Read("bootstrap_complete = true\n" + Report.Replace("pid = 123", "pid = 456", StringComparison.Ordinal)));
        Assert.Throws<IOException>(() => Read("bootstrap_complete = true\n" + Report.Replace("state = \"ready\"", "state = \"starting\"", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("failed", "patch-restoration", "rollback-failed", true)]
    [InlineData("ready", "", "rollback-failed", false)]
    [InlineData("failed", "required-plugin", "rollback-failed", false)]
    [InlineData("ready", "", "signature-mismatch", true)]
    public void OptionalPatchRestorationFailureBlocksReadiness(string state, string error, string outcome, bool accepted)
    {
        var expected = Expected with
        {
            Plugins = [new("test.required", "1.0.0", "plugins", false, new Dictionary<string, object>())]
        };
        var report = Report.Replace("required = true", "required = false", StringComparison.Ordinal)
            .Replace("state = \"ready\"", $"state = \"{state}\"", StringComparison.Ordinal)
            .Replace("error = \"\"", $"error = \"{error}\"", StringComparison.Ordinal)
            .Replace("outcome = \"installed\"", $"outcome = \"{outcome}\"", StringComparison.Ordinal);
        if (!accepted)
        {
            Assert.Throws<IOException>(() => Read(report, expected));
            return;
        }
        var result = Read(report, expected);
        Assert.Equal(state, result.State);
        Assert.Equal(error, result.Error);
        Assert.False(Assert.Single(result.Plugins).Required);
        Assert.Equal(outcome, result.Plugins[0].Outcome);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", "1123456789abcdef0123456789abcdef")]
    [InlineData("pid = 123", "pid = 124")]
    [InlineData("process_started = 987654321", "process_started = 987654320")]
    [InlineData("game = \"fixture\"", "game = \"different\"")]
    [InlineData("build = \"test-1\"", "build = \"test-2\"")]
    [InlineData("instance = \"sample\"", "instance = \"other\"")]
    [InlineData("runtime = \"3.2.0\"", "runtime = \"3.1.0\"")]
    [InlineData("id = \"test.required\"", "id = \"test.other\"")]
    [InlineData("version = \"1.0.0\"", "version = \"2.0.0\"")]
    [InlineData("required = true", "required = false")]
    [InlineData("outcome = \"installed\"", "outcome = \"failed\"")]
    [InlineData("state = \"ready\"", "state = \"failed\"")]
    public void ReplayedOrInconsistentReportsAreRejected(string from, string to) =>
        Assert.Throws<IOException>(() => Read(Report.Replace(from, to, StringComparison.Ordinal)));

    [Fact]
    public void MissingResultsUnknownFieldsAndOversizedFilesAreRejected()
    {
        Assert.Throws<IOException>(() => Read(Report[..Report.IndexOf("[[plugins]]", StringComparison.Ordinal)]));
        Assert.Throws<DefinitionException>(() => Read("unexpected = true\n" + Report));
        Assert.Throws<IOException>(() => Read(Report + "\n#" + new string('x', 65536)));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
