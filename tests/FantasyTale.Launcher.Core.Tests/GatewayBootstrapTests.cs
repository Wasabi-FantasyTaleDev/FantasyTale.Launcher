using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed class GatewayBootstrapTests
{
    [Fact]
    public void OnlyAuditedBuildDefinitionsExposeNativeAutomaticSelection()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var module = new BubbleFighterModule();
        Assert.Equal(["cn-279", "cn-324", "cn-404", "cn-406", "cn-408", "cn-706", "cn-717", "cn-735", "cn-774", "id-1809", "id-400", "kr-1256", "kr-812", "tw-454"],
            game.Builds.Where(module.SupportsAutomaticSelection).Select(row => row.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NativeAutomaticSelectionCapabilityDescribesOnlyAuditedAdapters()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var build = game.Builds.Single(row => row.Id == "kr-812");
        var module = new BubbleFighterModule();
        Assert.True(module.SupportsAutomaticSelection(build));
        Assert.False(module.SupportsAutomaticSelection(build with { Id = "fixture-unadapted" }));
        Assert.True(GatewayBuildCapabilities.SupportsBootstrap("cn-774"));
        Assert.True(module.SupportsAutomaticSelection(game.Builds.Single(row => row.Id == "cn-774")));
        Assert.True(GatewayBuildCapabilities.SupportsBootstrap("cn-735"));
    }

    [Fact]
    public async Task UnauditedAdapterRejectsAMultipleDestinationBootstrap()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var build = game.Builds.Single(row => row.Id == "kr-812") with { Id = "fixture-unadapted" };
        var context = new LaunchContext(game, build, "unused", null, "player")
        {
            GameTargets = [new("first", "First", new("192.0.2.1", 7110)), new("second", "Second", new("192.0.2.2", 7110))],
        };
        await Assert.ThrowsAsync<InvalidDataException>(async () => await new BubbleFighterModule().PrepareBootstrapAsync(context, CancellationToken.None));
    }

    [Theory]
    [InlineData("automatic awaiting fresh snapshot")]
    [InlineData("automatic native redirect sent")]
    [InlineData("automatic source promotion acknowledged")]
    [InlineData("automatic source released")]
    [InlineData("automatic auxiliary accepted")]
    [InlineData("automatic native login received")]
    [InlineData("automatic native status sent; available=True")]
    [InlineData("automatic native status sent; available=False")]
    [InlineData("automatic unavailable; manual bootstrap retained")]
    [InlineData("unavailable auxiliary retirement started")]
    [InlineData("rejected automatic frame; opcode=125, payload_bytes=9")]
    public void AutomaticDiagnosticsPreserveOnlyFixedEvents(string message)
    {
        Assert.NotEqual("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize("bf-center-selector: " + message + "."));
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize("bf-center-selector: " + message + ". ticket=private"));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("connect")]
    [InlineData("selection")]
    [InlineData("release")]
    public void AutomaticDeadlineDiagnosticsRetainOnlyKnownPhases(string phase)
    {
        Assert.Equal("automatic-deadline-" + phase,
            GatewayDiagnostic.Sanitize("Fantasy Tale Gateway failed: TimeoutException: Automatic Center " + phase + " deadline expired."));
        Assert.Equal("suppressed-unrecognized-line",
            GatewayDiagnostic.Sanitize("Fantasy Tale Gateway failed: TimeoutException: Automatic Center " + phase + " deadline expired. private"));
    }

    [Theory]
    [InlineData("kr-812", false)]
    [InlineData("kr-812", true)]
    [InlineData("cn-324", false)]
    [InlineData("cn-324", true)]
    [InlineData("kr-1256", false)]
    [InlineData("kr-1256", true)]
    public async Task SingleDestinationConnectsDirectlyButDistinctAliasesCannotBeCollapsed(string buildId, bool aliases)
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var target = new GameServerTarget("manual", "Manual", new("192.0.2.1", 7110)) { BackendId = "first", NativeCenterId = 2 };
        var context = new LaunchContext(game, game.Builds.Single(row => row.Id == buildId), "unused", null, "player")
        {
            GameTargets = aliases ? [target with { Id = "auto", Name = "Automatic", NativeCenterId = 1 }, target] : [target],
            GameChoices = aliases ? [new("auto", "Automatic", 1, "auto", null), new("manual", "Manual", 2, "manual", null)] : [],
        };
        if (aliases)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new BubbleFighterModule().PrepareBootstrapAsync(context, CancellationToken.None));
            return;
        }
        await using var lease = await new BubbleFighterModule().PrepareBootstrapAsync(context, CancellationToken.None);
        Assert.NotNull(lease);
        Assert.Equal(target.Endpoint, lease.GameEndpoint);
        Assert.Null(lease.Metadata);
    }

    [Fact]
    public void PinnedGatewayConfigurationPreservesCenterShortName()
    {
        var target = new GameServerTarget("beijing", "北京（阿里云）", new("192.0.2.8", 7110))
        {
            ShortName = "北京",
            BackendId = "beijing",
            NativeCenterId = 2,
        };
        using var document = System.Text.Json.JsonDocument.Parse(GatewayBootstrap.BuildConfiguration("cn-324", [target]));
        var center = document.RootElement.GetProperty("rows")[0].GetProperty("config").GetProperty("centers")[0];
        Assert.Equal(target.Name, center.GetProperty("name").GetString());
        Assert.Equal(target.ShortName, center.GetProperty("short_name").GetString());
        var config = document.RootElement.GetProperty("rows")[0].GetProperty("config");
        Assert.True(config.GetProperty("connection_trace").GetBoolean());
        Assert.Equal(65536, config.GetProperty("connection_trace_max_bytes").GetInt32());
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("65535", true)]
    [InlineData("65536", false)]
    [InlineData("-1", false)]
    [InlineData("secret", false)]
    [InlineData("54. ticket=private", false)]
    public void AuxiliaryDiagnosticsAcceptOnlyBoundedOpcodeMetadata(string opcode, bool accepted)
    {
        var line = "bf-center-selector: native auxiliary request observed; opcode=" + opcode + ".";
        Assert.Equal(accepted, GatewayDiagnostic.Sanitize(line) != "suppressed-unrecognized-line");
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(line + " private"));
    }

    [Fact]
    public void GatewayDiagnosticsBoundAndRedactCredentialLikeStderr()
    {
        var value = GatewayDiagnostic.Sanitize("session=secret ticket:abc token=xyz password=hunter2 fatal pipe closed");

        Assert.DoesNotContain("secret", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xyz", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", value, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(value.Length, 1, 120);
        Assert.DoesNotContain(value, c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'));
    }

    [Fact]
    public void GatewayDiagnosticsUseSafePlaceholderForEmptyStderr()
    {
        Assert.Equal("empty", GatewayDiagnostic.Sanitize("\r\n\t"));
    }

    [Fact]
    public void GatewayDiagnosticsRetainOnlyTheExactKeepAliveEvent()
    {
        Assert.Equal("bf-center-selector__native_keep-alive_accepted.", GatewayDiagnostic.Sanitize("bf-center-selector: native keep-alive accepted."));
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize("bf-center-selector: native keep-alive accepted. private-value"));
    }

    [Theory]
    [InlineData("native startup exchange answered; opcode=1500")]
    [InlineData("pre-handshake opcode accepted; opcode=22")]
    [InlineData("bootstrap failure; phase=connect, kind=SocketException")]
    [InlineData("bootstrap failure; phase=handshake, kind=PacketFormatException")]
    [InlineData("bootstrap failure; phase=selection, kind=EndOfStreamException")]
    [InlineData("bootstrap failure; phase=selection, kind=IOException")]
    [InlineData("bootstrap failure; phase=selection, kind=InvalidOperationException")]
    [InlineData("automatic failure; phase=connect, kind=SocketException")]
    [InlineData("automatic failure; phase=status, kind=PacketFormatException")]
    [InlineData("automatic failure; phase=selection, kind=EndOfStreamException")]
    [InlineData("automatic failure; phase=release, kind=IOException")]
    [InlineData("automatic failure; phase=freshness, kind=InvalidOperationException")]
    public void BootstrapFailureDiagnosticsKeepOnlyFixedMetadata(string message)
    {
        var line = "bf-center-selector: " + message + ".";
        Assert.NotEqual("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(line));
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(line + " private"));
    }

    [Theory]
    [InlineData("bootstrap failure; phase=private, kind=IOException")]
    [InlineData("bootstrap failure; phase=selection, kind=PrivateException")]
    [InlineData("native startup exchange answered; opcode=private")]
    [InlineData("native startup exchange answered; opcode=1500, payload=private")]
    [InlineData("pre-handshake opcode accepted; opcode=secret")]
    [InlineData("automatic failure; phase=private, kind=IOException")]
    [InlineData("automatic failure; phase=release, kind=PrivateException")]
    public void BootstrapFailureDiagnosticsRejectArbitraryMetadata(string message) =>
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize("bf-center-selector: " + message + "."));

    [Theory]
    [InlineData("{\"ticket\":\"hidden\"}")]
    [InlineData("Authorization: Bearer hidden")]
    [InlineData("hidden-with-no-label")]
    [InlineData("bf-center-selector: native greeting sent. hidden")]
    public void UnknownStderrIsSuppressedEntirely(string stderr)
    {
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(stderr));
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("automatic")]
    [InlineData("unavailable")]
    public void ConnectionDiagnosticsRetainEveryFixedLifecycleEvent(string role)
    {
        foreach (var name in new[] { "listening", "accepted", "status-sent", "uniqueness-received",
            "uniqueness-replied", "eof", "closed", "accept-stopped", "cancelled", "failed", "directory-sent",
            "login-received", "redirect-sent", "promotion-acknowledged", "unavailable", "deadline" })
        {
            var line = $"bf-center-selector: connection role={role} port=65535 event={name}.";
            Assert.Contains("port_65535", GatewayDiagnostic.Sanitize(line));
            Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(line + " ticket=private"));
        }
    }

    [Theory]
    [InlineData("private", "123", "accepted")]
    [InlineData("automatic", "0", "accepted")]
    [InlineData("automatic", "65536", "accepted")]
    [InlineData("automatic", "-1", "accepted")]
    [InlineData("automatic", "private", "accepted")]
    [InlineData("automatic", "123", "private")]
    public void ConnectionDiagnosticsRejectInvalidOrArbitraryMetadata(string role, string port, string name) =>
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(
            $"bf-center-selector: connection role={role} port={port} event={name}."));

    [Fact]
    public async Task LateConnectionEvidenceSurvivesRepeatedUntrustedStderrAndRemainsBounded()
    {
        var lines = Enumerable.Repeat("unlabelled-private-ticket", 100)
            .Concat(Enumerable.Range(1, 1000).Select(port =>
                $"bf-center-selector: connection role=unavailable port={port} event=closed."));
        var sink = new GatewayTraceSink();
        var tail = new System.Text.StringBuilder();
        await GatewayBootstrap.DrainStandardErrorAsync(new StringReader(string.Join('\n', lines)), tail, sink);
        Assert.Single(sink.Events, row => row.Detail == "suppressed-unrecognized-line");
        Assert.Contains(sink.Events, row => row.Detail?.Contains("port_100_", StringComparison.Ordinal) == true);
        Assert.Contains(sink.Events, row => row.Name == "gateway-stderr-limit" && row.Detail == "bytes-65536");
        Assert.Single(sink.Events, row => row.Name == "gateway-stderr-limit");
        Assert.InRange(sink.Events.Where(row => row.Name == "gateway-stderr").Sum(row => row.Detail!.Length + 1), 1, 65536);
        Assert.InRange(tail.Length, 1, 2048);
        Assert.Contains("port_1000_", tail.ToString());
        Assert.DoesNotContain("private-ticket", tail.ToString());
        Assert.DoesNotContain(sink.Events, row => row.Detail?.Contains("private-ticket", StringComparison.Ordinal) == true);
    }

    private sealed class GatewayTraceSink : ILaunchDiagnosticSink
    {
        internal System.Collections.Concurrent.ConcurrentQueue<(string Name, string? Detail)> Events { get; } = new();
        public void Record(string eventName, string? detail = null) => Events.Enqueue((eventName, detail));
    }

    [Fact]
    public void GatewayBudgetMarkerHasNoUntrustedSuffix() =>
        Assert.Equal("Fantasy-Tale-Gateway-log-budget-exhausted.",
            GatewayDiagnostic.Sanitize("Fantasy Tale Gateway log budget exhausted."));

    [Theory]
    [InlineData("automatic", "1", "0", "4294967295", true)]
    [InlineData("unavailable", "65535", "4294967295", "0", true)]
    [InlineData("bootstrap", "123", "1", "2", false)]
    [InlineData("automatic", "0", "1", "2", false)]
    [InlineData("automatic", "65536", "1", "2", false)]
    [InlineData("automatic", "123", "4294967296", "2", false)]
    [InlineData("automatic", "123", "1", "4294967296", false)]
    [InlineData("automatic", "123", "-1", "2", false)]
    public void BindingDiagnosticsAcceptOnlyNumericDirectoryIdentities(string role, string port, string group,
        string subkey, bool accepted)
    {
        var line = $"bf-center-selector: binding role={role} port={port} group={group} sub={subkey}.";
        Assert.Equal(accepted, GatewayDiagnostic.Sanitize(line) != "suppressed-unrecognized-line");
        Assert.Equal("suppressed-unrecognized-line", GatewayDiagnostic.Sanitize(line + " private"));
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("exit-7")]
    [InlineData("invalid-ready")]
    public async Task ProcessAndPipeDiagnosticsPersistWithoutCredentials(string mode)
    {
        var root = Directory.CreateTempSubdirectory("ft-gateway-probe-").FullName;
        var exitVerified = false;
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "test-client")))
                File.Copy(path, Path.Combine(root, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(root, "gateway-probe"), mode);
            var executable = Path.Combine(root, "FantasyTale.Launcher.TestClient.exe");
            var sink = new HostLifecycleLog(root);
            var targets = new[] { new GameServerTarget("fixture", "Fixture", new("192.0.2.1", 7140)) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (mode == "invalid-ready")
                await Assert.ThrowsAsync<InvalidDataException>(() => GatewayBootstrap.StartAsync(executable, "cn-324", targets, timeout.Token, sink));
            else
            {
                var lease = await GatewayBootstrap.StartAsync(executable, "cn-324", targets, timeout.Token, sink);
                if (mode == "exit-7")
                {
                    var error = await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
                    Assert.DoesNotContain("fixture-secret", error.Message, StringComparison.Ordinal);
                    Assert.Contains("gateway-process-exited", File.ReadAllText(Path.Combine(root, "host-lifecycle.jsonl")), StringComparison.Ordinal);
                }
                else await lease.DisposeAsync();
                await lease.DisposeAsync();
            }
            var text = File.ReadAllText(Path.Combine(root, "host-lifecycle.jsonl"));
            Assert.DoesNotContain("fixture-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-identity", text, StringComparison.Ordinal);
            Assert.Contains("native_greeting_sent", text, StringComparison.Ordinal);
            Assert.Contains("opcode_271", text, StringComparison.Ordinal);
            Assert.Contains("gateway-config-sent", text, StringComparison.Ordinal);
            Assert.Contains("build-cn-324-targets-1", text, StringComparison.Ordinal);
            Assert.Contains(mode == "invalid-ready" ? "gateway-start-failed" : "gateway-process-exited", text, StringComparison.Ordinal);
            Assert.InRange(new FileInfo(Path.Combine(root, "host-lifecycle.jsonl")).Length, 1, HostLifecycleLog.MaximumBytes);
            Assert.Contains("gateway-process-exited", text, StringComparison.Ordinal);
            exitVerified = true;
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { Directory.Delete(root, true); break; }
                // Windows can retain the image section briefly after the exit signal and handle disposal.
                catch (Exception error) when (exitVerified && OperatingSystem.IsWindows()
                    && error is IOException or UnauthorizedAccessException
                    && (error.HResult & 0xffff) is 5 or 32 or 33 && elapsed.Elapsed < TimeSpan.FromSeconds(2))
                { await Task.Delay(25); }
            }
        }
    }

    private sealed class GatewayInteropFactAttribute : FactAttribute
    {
        public GatewayInteropFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")))
                Skip = "Set FT_TEST_GATEWAY_EXE to a published Fantasy Tale Gateway executable for process and pipe interoperability.";
        }
    }

    [Fact]
    public void DirectoryValidationRejectsDuplicateTargetsAndNonIpv4Endpoints()
    {
        var duplicate = new[]
        {
            new GameServerTarget("official", "Official", new("192.0.2.1", 7140)),
            new GameServerTarget("test", "Test", new("192.0.2.1", 7140)),
        };
        var domain = new[] { new GameServerTarget("official", "Official", new("server.example", 7140)) };

        Assert.Throws<InvalidOperationException>(() => GatewayBootstrap.ValidateTargets(duplicate));
        Assert.Throws<InvalidOperationException>(() => GatewayBootstrap.ValidateTargets(domain));
    }

    [Fact]
    public void OrdinaryNativeDirectoryPreservesEveryDistinctPhysicalEntryWithoutPresentationMetadata()
    {
        var targets = new[]
        {
            new GameServerTarget("first", "First", new("192.0.2.1", 7110)) { BackendId = "first", NativeCenterId = 2 },
            new GameServerTarget("second", "Second", new("192.0.2.2", 7110)) { BackendId = "second", NativeCenterId = 3 },
        };
        var result = GatewayBootstrap.ProjectOrdinaryTargets(targets);
        Assert.Equal([new ServerEndpoint("192.0.2.1", 7110), new ServerEndpoint("192.0.2.2", 7110)], result.Select(row => row.Endpoint));
        Assert.All(result, row => { Assert.Null(row.BackendId); Assert.Null(row.NativeCenterId); });
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void NativeStatusProbeRetainsCapacitiesAndIdentitiesForUnavailableFallback()
    {
        var targets = new[]
        {
            new GameServerTarget("first", "First", new("192.0.2.1", 7110))
                { BackendId = "first", NativeCenterId = 2, StatusCapacity = 512 },
            new GameServerTarget("second", "Second", new("192.0.2.2", 7110))
                { BackendId = "second", NativeCenterId = 3, StatusCapacity = 731 },
        };
        var projected = GatewayBootstrap.ProjectOrdinaryTargets(targets);
        using var json = System.Text.Json.JsonDocument.Parse(GatewayBootstrap.BuildConfiguration("tw-454", projected));
        var config = json.RootElement.GetProperty("rows")[0].GetProperty("config");
        Assert.Equal(3000, config.GetProperty("native_probe_timeout_ms").GetInt32());
        var centers = config.GetProperty("centers").EnumerateArray().ToArray();
        Assert.Equal([2, 3], centers.Select(row => row.GetProperty("native_center_id").GetInt32()));
        Assert.Equal([512, 731], centers.Select(row => row.GetProperty("status_capacity").GetInt32()));
        Assert.False(config.TryGetProperty("selector_identity", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("different")]
    public void OrdinaryNativeDirectoryRejectsUnprovenAliasOwnership(string? backend)
    {
        var targets = new[]
        {
            new GameServerTarget("auto", "Automatic", new("192.0.2.1", 7110)) { BackendId = "first" },
            new GameServerTarget("other", "Other", new("192.0.2.1", 7110)) { BackendId = backend },
        };
        Assert.Throws<InvalidOperationException>(() => GatewayBootstrap.ProjectOrdinaryTargets(targets));
    }

    [GatewayInteropFact]
    public async Task PublishedGatewayUsesPhysicalTargetsWithoutClientPresentationMetadata()
    {
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!);
        var targets = new[]
        {
            new GameServerTarget("direct", "Official direct", new("192.0.2.1", 7140)) { BackendId = "formal-a", NativeCenterId = 11 },
            new GameServerTarget("second", "Second", new("192.0.2.2", 7140)) { BackendId = "formal-b", NativeCenterId = 12 },
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var lease = await GatewayBootstrap.StartAsync(executable, "cn-324", GatewayBootstrap.ProjectOrdinaryTargets(targets), timeout.Token);
        Assert.Null(lease.Metadata);
        Assert.Equal("127.0.0.1", lease.GameEndpoint!.Host);
    }

    [Fact]
    public async Task OfflineChoicesDoNotInstallAClientPresentationAdapter()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var targets = new[] { new GameServerTarget("automatic", "Official automatic", new("192.0.2.1", 7140))
            { BackendId = "formal-a", NativeCenterId = 1 } };
        var choices = new[] { new GameServerChoice("automatic", "Official automatic", 1, "automatic", null) }
            .Concat(Enumerable.Range(2, 5).Select(index => new GameServerChoice("offline-" + index, "Offline " + index, index, null, "unavailable"))).ToArray();
        var context = new LaunchContext(game, game.Builds.Single(row => row.Id == "cn-324"), "unused", null, "player")
        {
            GameTargets = targets, GameChoices = choices,
        };
        await using var lease = await new BubbleFighterModule().PrepareBootstrapAsync(context, CancellationToken.None);
        Assert.NotNull(lease);
        Assert.Null(lease.Metadata);
        Assert.Equal(targets[0].Endpoint, lease.GameEndpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCenterChoicesDoNotRequireTheRetiredPresentationPlugin(bool nameLogin)
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(row => row.Id == "bubblefighter");
        var context = new LaunchContext(game, game.Builds.Single(row => row.Id == "cn-324"), "unused", new("192.0.2.1", 7140), "player")
        {
            NameLogin = nameLogin,
            GameChoices = [new("manual", "Manual", 1, "manual", null), new("offline", "Offline", 2, null, "unavailable")],
            FilePluginTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GameWidget.dll" },
        };
        var plan = new BubbleFighterModule().CreateLaunchPlan(context);
        if (nameLogin) Assert.Equal("1.0.2", plan.MinimumPluginVersions["bubblefighter.fantasy.login-fix-cn"]);
        else Assert.False(plan.MinimumPluginVersions.ContainsKey("bubblefighter.fantasy.login-fix-cn"));
    }

    /// <summary>The shipped control pipe accepts failed singleton and large manual directories without contacting their destinations.</summary>
    [GatewayInteropFact]
    public async Task PublishedGatewayRetainsUnavailableManualDirectories()
    {
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        foreach (var count in new[] { 1, 65 })
        {
            var targets = Enumerable.Range(1, count).Select(i => new GameServerTarget("failed-" + i, "Failed " + i,
                new("target-" + i + ".invalid", 17110))
            {
                NativeCenterId = i, BackendId = "backend-" + i, UnavailableCapacity = 512,
            }).ToArray();
            await using var lease = await GatewayBootstrap.StartAsync(executable, "cn-324", targets, timeout.Token);
            Assert.Equal("127.0.0.1", lease.GameEndpoint!.Host);
        }
    }

    /// <summary>The shipped Gateway consumes locally projected leases and a dynamically sized control message for all admitted workers.</summary>
    [GatewayInteropFact]
    public async Task PublishedGatewayAcceptsSkewedGrantAndLargeAutomaticPool()
    {
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        foreach (var count in new[] { 1, 129 })
        {
            var grant = AutomaticServerSelectionTests.Grant(DateTimeOffset.UtcNow.AddSeconds(22));
            var template = grant.Servers[0].Targets[0];
            var nodes = Enumerable.Range(1, count).Select(i => template with { NodeId = "node-" + i, Host = "192.0.2." + i }).ToArray();
            grant = grant with { Servers = new ServerSelector[] { new(1, "Automatic", true, nodes) }
                .Concat(nodes.Select((node, i) => new ServerSelector(i + 2, "Manual " + i, false, [node]))).ToArray() };
            var selection = await FantasyTale.Launcher.Core.Servers.AutomaticServerSelection.CreateAsync(grant,
                AutomaticServerSelectionTests.ApiUrl, _ => Task.FromResult<LaunchServerSelection?>(grant),
                resolve: (host, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse(host) }),
                measure: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)),
                measureManual: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)), ct: timeout.Token);
            Assert.Equal(count, selection.AdmittedCenterIds.Count);
            await using var lease = await GatewayBootstrap.StartAsync(executable, grant.Build, selection.Targets,
                timeout.Token, automatic: selection, launchId: Guid.NewGuid().ToString("N"));
            Assert.Equal("127.0.0.1", lease.GameEndpoint!.Host);
        }
    }

    [GatewayInteropFact]
    public async Task PublishedGatewayRetainsLeaseAfterAdmissionClosesUntilGameDisposal()
    {
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!);
        var root = Directory.CreateTempSubdirectory("ft-gateway-admission-").FullName;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var grant = AutomaticServerSelectionTests.Grant(DateTimeOffset.UtcNow) with { Build = "cn-774" };
            var original = await FantasyTale.Launcher.Core.Servers.AutomaticServerSelection.CreateAsync(grant,
                AutomaticServerSelectionTests.ApiUrl, _ => Task.FromResult<LaunchServerSelection?>(grant),
                resolve: (host, _) => Task.FromResult(new[] { System.Net.IPAddress.Parse(host) }),
                measure: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)),
                measureManual: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)), ct: deadline.Token);
            var selection = new ClosingAdmission(original);
            var sink = new GatewayTraceSink();
            var lease = await GatewayBootstrap.StartAsync(executable, grant.Build, selection.Targets, deadline.Token,
                sink, selection, Guid.NewGuid().ToString("N"));
            try
            {
                selection.Closed = true;
                while (!sink.Events.Any(item => item.Name == "gateway-admission-withdrawn"))
                    await Task.Delay(25, deadline.Token);
                var events = string.Join('\n', sink.Events.Select(item => item.Name));
                Assert.DoesNotContain("gateway-stop-requested", events, StringComparison.Ordinal);
                Assert.DoesNotContain("gateway-exited", events, StringComparison.Ordinal);
                // Inspect readiness without opening an incomplete native handshake.
                Assert.Contains(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners(),
                    endpoint => endpoint.Port == lease.GameEndpoint!.Port);
            }
            finally { await lease.DisposeAsync(); }
            var final = string.Join('\n', sink.Events.Select(item => item.Name));
            Assert.Contains("gateway-stop-requested", final, StringComparison.Ordinal);
            Assert.Contains("gateway-exited", final, StringComparison.Ordinal);
            Assert.DoesNotContain("gateway-kill-requested", final, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ClosingAdmission(IAutomaticServerSelection original) : IAutomaticServerSelection
    {
        internal bool Closed { get; set; }
        public LaunchServerSelection Grant => original.Grant;
        public DateTimeOffset LocalExpiresAtUtc => original.LocalExpiresAtUtc;
        public IReadOnlyList<GameServerTarget> Targets => original.Targets;
        public ServerSelector Automatic => original.Automatic;
        public IReadOnlyList<string> AdmittedCenterIds => original.AdmittedCenterIds;
        public Task<IReadOnlyList<AutomaticServerCandidate>?> RefreshAsync(CancellationToken cancellationToken) =>
            Closed ? Task.FromResult<IReadOnlyList<AutomaticServerCandidate>?>(null) : original.RefreshAsync(cancellationToken);
    }

    [GatewayInteropFact]
    public async Task PublishedGatewayAcceptsConfigurationReturnsReadyAndStops()
    {
        var executable = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_GATEWAY_EXE")!);
        var targets = new[]
        {
            new GameServerTarget("official", "正式服", new("192.0.2.1", 7140)),
            new GameServerTarget("test", "测试服", new("198.51.100.2", 7140)),
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using var lease = await GatewayBootstrap.StartAsync(executable, "cn-324", targets, timeout.Token);

        Assert.Equal(new ServerEndpoint("127.0.0.1", lease.GameEndpoint!.Port), lease.GameEndpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulHandoffPreservesTheControlPipeUntilNaturalRetirementOrGameExit(bool gameExit)
    {
        var root = Directory.CreateTempSubdirectory("ft-gateway-handoff-").FullName;
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "test-client")))
                File.Copy(path, Path.Combine(root, Path.GetFileName(path)));
            File.WriteAllText(Path.Combine(root, "gateway-probe"), gameExit ? "retire-on-stop" : "retire-naturally");
            var sink = new HostLifecycleLog(root);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var lease = await GatewayBootstrap.StartAsync(Path.Combine(root, "FantasyTale.Launcher.TestClient.exe"), "cn-324",
                [new("fixture", "Fixture", new("192.0.2.1", 7140))], deadline.Token, sink);
            try
            {
                using var lifetime = new CancellationTokenSource();
                var retirement = lease.CompleteHandoffAsync(lifetime.Token).AsTask();
                Assert.False(retirement.IsCompleted);
                var before = File.ReadAllText(Path.Combine(root, "host-lifecycle.jsonl"));
                Assert.Contains("gateway-handoff-waiting", before, StringComparison.Ordinal);
                Assert.DoesNotContain("gateway-stop-requested", before, StringComparison.Ordinal);
                if (gameExit)
                {
                    await lifetime.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retirement);
                }
                else
                {
                    File.WriteAllText(Path.Combine(root, "retire-gateway"), "retire");
                    await retirement.WaitAsync(deadline.Token);
                }
            }
            finally { await lease.DisposeAsync(); }
            var events = File.ReadAllText(Path.Combine(root, "host-lifecycle.jsonl"));
            Assert.Contains("gateway-exited", events, StringComparison.Ordinal);
            Assert.DoesNotContain("gateway-kill-requested", events, StringComparison.Ordinal);
            if (gameExit) Assert.Contains("gateway-stop-requested", events, StringComparison.Ordinal);
            else
            {
                Assert.Contains("gateway-handoff-retired", events, StringComparison.Ordinal);
                Assert.DoesNotContain("gateway-stop-requested", events, StringComparison.Ordinal);
            }
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { Directory.Delete(root, true); break; }
                catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException
                    && (error.HResult & 0xffff) is 5 or 32 or 33 && elapsed.Elapsed < TimeSpan.FromSeconds(2))
                { await Task.Delay(25); }
            }
        }
    }
}
