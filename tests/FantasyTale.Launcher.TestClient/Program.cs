// A real child process exercises the launcher lifetime without starting a game.
if (args.Length == 4 && args[0] == "--pipe" && args[2] == "--control-max-bytes"
    && int.TryParse(args[3], out var controlMaximum) && controlMaximum > 0)
    return await GatewayProbe.RunAsync(args[1], controlMaximum);

if (args.Contains("--module-probe", StringComparer.Ordinal))
    return ModuleProbe.Run(args[1], args[2]);

if (args.Contains("--transaction-probe", StringComparer.Ordinal))
{
    var installation = args[1];
    var checkpoint = args[2];
    var marker = args[3];
    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    void Hold()
    {
        File.WriteAllText(marker + ".tmp", checkpoint);
        File.Move(marker + ".tmp", marker);
        using var release = new ManualResetEventSlim();
        if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Transaction probe was not terminated at its checkpoint.");
    }
    var files = new FantasyTale.Launcher.Core.Stores.ClientReleaseFile[]
    {
        new("payload.dll", "payload", 3, Hash("new"u8.ToArray())),
        new("added.dll", "added", 5, Hash("added"u8.ToArray())),
    };
    var receipt = new FantasyTale.Launcher.Core.Stores.ClientInstallReceipt("fixture", "fixture", "", Hash("next-index"u8.ToArray()), Hash("source"u8.ToArray()), 2);
    await FantasyTale.Launcher.Core.Stores.ClientInstallTransaction.ApplyAsync(installation, files, percent =>
    {
        if ((checkpoint == "removed" && percent == 33) || (checkpoint == "replaced" && percent == 66) || (checkpoint == "added" && percent == 100)) Hold();
    }, default, receipt, deferCommit: true, pruneOwned: true);
    if (checkpoint == "committed") await FantasyTale.Launcher.Core.Stores.ClientInstallTransaction.CommitAsync(installation, Hold);
    return 0;
}
if (File.Exists("hosting-probe") && args.Contains("--managed-stdio", StringComparer.Ordinal))
    args = ["--hosting-probe", File.ReadAllText("hosting-probe")];
if (args.Contains("--hosting-probe", StringComparer.Ordinal))
{
    var mode = args[^1];
    if (mode == "crash") return 17;
    if (mode == "timeout") { await Console.In.ReadLineAsync(); return 0; }
    if (mode == "stderr")
    {
        Console.Error.WriteLine("FT_HOST_READY 1"); await Console.Error.FlushAsync();
        await Console.In.ReadLineAsync(); return 0;
    }
    if (mode == "flood") { for (var i = 0; i < 800; i++) Console.WriteLine(new string('x', 6000)); }
    if (mode == "plan")
    {
        var environment = System.Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(entry => ((string)entry.Key).StartsWith("FT_HOSTING_TEST_", StringComparison.Ordinal))
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        File.WriteAllText("hosting-input.json", System.Text.Json.JsonSerializer.Serialize(new { arguments = args, environment }));
    }
    if (mode is "descendant" or "descendant-exit")
    {
        var childStart = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        childStart.ArgumentList.Add("--hosting-probe"); childStart.ArgumentList.Add("hang");
        using var child = System.Diagnostics.Process.Start(childStart)!;
        File.WriteAllText("descendant.pid", child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    Console.WriteLine("FT_HOST_READY 1"); await Console.Out.FlushAsync();
    if (mode == "descendant-exit") return 0;
    if (mode == "hang") { await Task.Delay(TimeSpan.FromMinutes(2)); return 0; }
    var command = await Console.In.ReadLineAsync();
    File.WriteAllText("graceful-stop", command ?? "eof");
    return command is null or "stop" ? 0 : 2;
}
if (File.Exists("update-probe"))
{
    var dataRoot = args.SkipWhile(a => a != "--data-root").Skip(1).First();
    if (args.Contains("--prepare-recovery", StringComparer.Ordinal))
    {
        try
        {
            var prepared = await FantasyTale.Launcher.Core.Updates.LauncherUpdateHandoff.PrepareRecoveryAsync(AppContext.BaseDirectory, dataRoot, CancellationToken.None, showErrors: false);
            return prepared ? 0 : 3;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(dataRoot, "probe-error"), error.ToString()); return 1; }
    }
    if (args.Contains("--prepare-update", StringComparer.Ordinal))
    {
        try
        {
            await FantasyTale.Launcher.Core.Updates.LauncherUpdateHandoff.PrepareAsync(AppContext.BaseDirectory, dataRoot,
                Path.Combine(dataRoot, "launcher-update"), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(dataRoot, "launcher-update", "release.toml")))).ToLowerInvariant(), CancellationToken.None);
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(dataRoot, "probe-error"), error.ToString()); return 1; }
    }
    var health = args.SkipWhile(a => a != "--update-health").Skip(1).FirstOrDefault();
    if (health is null)
    {
        File.WriteAllText(Path.Combine(dataRoot, "restored"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }
    if (File.ReadAllText("update-probe") == "exit") return 1;
    if (File.ReadAllText("update-probe") == "invalid-health")
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", health, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(timeout.Token);
        File.WriteAllText(Path.Combine(dataRoot, "invalid-health-pid"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await pipe.WriteAsync(new byte[] { 0 }, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromSeconds(60));
        return 2;
    }
    var helper = int.Parse(args.SkipWhile(a => a != "--update-helper").Skip(1).First(), System.Globalization.CultureInfo.InvariantCulture);
    await FantasyTale.Launcher.Core.Updates.LauncherUpdateHandoff.AcknowledgeAsync(health, helper, CancellationToken.None);
    File.WriteAllText(Path.Combine(dataRoot, "healthy"), "yes");
    await Task.Delay(500);
    return 0;
}
File.WriteAllText("ready.tmp", File.Exists("session.toml") ? "session-present" : "session-missing");
File.Move("ready.tmp", "ready");
var deadline = DateTime.UtcNow.AddSeconds(20);
while (!File.Exists("release") && DateTime.UtcNow < deadline) await Task.Delay(25);
return File.Exists("release") ? 23 : 24;
