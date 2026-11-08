using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AutomaticSelectionFixtureTests
{
    private static readonly JsonSerializerOptions ReceiptJson = new() { WriteIndented = true };
    private sealed class FixtureFactAttribute : FactAttribute
    {
        public FixtureFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_AUTOMATIC_FIXTURE_INPUT"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")))
                Skip = "Requires an explicitly prepared disposable issuer, two actual workers and a verified Gateway executable.";
        }
    }

    [FixtureFact]
    public async Task ActualIssuerAndWorkersAcceptLiveGatewayRefreshWithoutStartingGame()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The isolated fixture uses current-user Windows session protection.");
        var inputFile = Environment.GetEnvironmentVariable("FT_TEST_AUTOMATIC_FIXTURE_INPUT")!;
        var executable = Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!;
        var data = Environment.GetEnvironmentVariable("FT_TEST_AUTOMATIC_DATA_ROOT")
            ?? throw new InvalidOperationException("An isolated test data root must be explicitly provided.");
        var buildId = Environment.GetEnvironmentVariable("FT_TEST_AUTOMATIC_BUILD") ?? "kr-812";
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var build = game.Builds.Single(row => row.Id == buildId);
        var module = new BubbleFighterModule();
        Assert.True(module.SupportsAutomaticSelection(build));
        var clientFolder = Environment.GetEnvironmentVariable("FT_TEST_AUTOMATIC_CLIENT_FOLDER");
        if (buildId != "kr-812" && clientFolder is null)
            throw new InvalidOperationException("This build requires the explicit disposable client path for actual version metadata.");
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(inputFile));
        var apiUrl = input.RootElement.GetProperty("api").GetString()!;
        var uri = new Uri(apiUrl);
        Assert.True(uri.IsLoopback);
        Assert.Equal("http", uri.Scheme);
        var server = new ServerDefinition(buildId.Replace("-", "", StringComparison.Ordinal) + "-native-auto", "bubblefighter", "Isolated native auto", apiUrl, null, true, false)
        { PreferIp = true };
        using var api = new LauncherApiClient(sessionStore: new ProtectedAccountSessionStore(data));
        await api.LoginAsync(server, input.RootElement.GetProperty("username").GetString()!, input.RootElement.GetProperty("password").GetString()!);
        var account = api.CaptureLaunchSelection(server.Id);
        var version = clientFolder is null ? new ClientVersionMetadata(null, "458") : module.ReadVersionMetadata(build, clientFolder);
        var credentials = await api.SelectionCredentialsAsync(server, build, version,
            new("en", build.NativeLanguage ?? "en", build.NativeLanguage ?? "en"), account);
        Assert.NotNull(credentials?.ServerSelection);
        var admittedApi = api.EffectiveApiUrl(server);
        var selection = await AutomaticServerSelection.CreateAsync(credentials.ServerSelection, admittedApi,
            ct => api.RefreshServerSelectionAsync(server, account, credentials, admittedApi, ct));
        Assert.Equal(2, selection.AdmittedCenterIds.Count);
        var diagnostics = new Diagnostics();
        await using (var lease = await GatewayBootstrap.StartAsync(executable, buildId, selection.Targets,
            CancellationToken.None, diagnostics, automatic: selection, launchId: Guid.NewGuid().ToString("N")))
        {
            Assert.Equal("127.0.0.1", lease.GameEndpoint!.Host);
            Assert.Null(lease.Metadata);
            await Task.Delay(TimeSpan.FromSeconds(6));
            Assert.Contains(diagnostics.Events, row => row.Name == "gateway-health-refresh" && row.Detail == "accepted");
        }
        Assert.Contains(diagnostics.Events, row => row.Name == "gateway-exited" && row.Detail!.StartsWith("code-0-", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(data, "automatic-fixture-dry-run.json"), JsonSerializer.Serialize(new
        {
            state = "producer-dry-run-passed", utc = DateTimeOffset.UtcNow, game_started = false,
            build = buildId,
            active_hook_plugins = 0, presentation_metadata = false,
            targets = selection.Targets.Select(row => new { row.Id, row.BackendId, row.NativeCenterId, row.Endpoint }),
            admitted_center_ids = selection.AdmittedCenterIds, events = diagnostics.Events,
        }, ReceiptJson));
    }

    private sealed class Diagnostics : ILaunchDiagnosticSink
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Event> _events = new();
        internal Event[] Events => _events.ToArray();
        public void Record(string eventName, string? detail = null) => _events.Enqueue(new(eventName, detail));
        internal sealed record Event(string Name, string? Detail);
    }
}
