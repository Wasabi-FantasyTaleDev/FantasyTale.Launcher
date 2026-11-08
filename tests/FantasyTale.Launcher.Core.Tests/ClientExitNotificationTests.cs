using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientExitNotificationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionAlwaysInvalidatesProcessStateIncludingCleanupFailure(bool cleanupFailure)
    {
        var messages = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => messages.Add(JsonNode.Parse(raw)!));
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = LauncherBackend.ReportExitAsync(bridge, new RunningClient("fixture", 123, completion.Task));
        Assert.Empty(messages);
        if (cleanupFailure) completion.SetException(new IOException("private-cleanup-details"));
        else completion.SetResult(new LaunchResult("fixture", 23));
        await notification;
        Assert.Equal(cleanupFailure ? "instances.error" : "instances.exited", messages[0]["event"]!.GetValue<string>());
        Assert.Equal("instances.changed", messages[1]["event"]!.GetValue<string>());
        Assert.Equal("fixture", messages[1]["data"]!["instanceId"]!.GetValue<string>());
        Assert.DoesNotContain("private-cleanup-details", string.Join("", messages), StringComparison.Ordinal);
        if (cleanupFailure) Assert.Equal("launch_cleanup_failed", messages[0]["data"]!["error"]!.GetValue<string>());
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task CleanupFailureIncludesCopyableSafeStageAndProcessContext()
    {
        JsonNode? payload = null;
        var bridge = new BridgeRouter(raw =>
        {
            var row = JsonNode.Parse(raw)!;
            if (row["event"]!.GetValue<string>() == "instances.error") payload = row["data"];
        });
        var error = new LaunchCleanupException("bootstrap-dispose", true, new IOException("private-ticket-and-path"), unchecked((int)0xC0000005));
        await LauncherBackend.ReportExitAsync(bridge, new RunningClient("fixture", 123, Task.FromException<LaunchResult>(error)));
        Assert.NotNull(payload);
        Assert.Equal("fixture", payload["instanceId"]!.GetValue<string>());
        Assert.Equal(123, payload["processId"]!.GetValue<int>());
        Assert.Equal("bootstrap-dispose", payload["phase"]!.GetValue<string>());
        Assert.True(payload["exitConfirmed"]!.GetValue<bool>());
        Assert.Equal(unchecked((int)0xC0000005), payload["exitCode"]!.GetValue<int>());
        Assert.Equal("IOException", payload["failureType"]!.GetValue<string>());
        Assert.True(payload["utc"]!.GetValue<DateTimeOffset>() > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.DoesNotContain("private-ticket-and-path", payload.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnconfirmedProcessExitDoesNotReportAnExitCode()
    {
        var error = new LaunchCleanupException("observe-exit", false, new IOException(), 0);
        Assert.Null(error.ExitCode);
    }
}
