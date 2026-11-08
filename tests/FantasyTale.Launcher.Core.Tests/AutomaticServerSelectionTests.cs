using System.Net;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class AutomaticServerSelectionTests
{
    internal const string ApiUrl = "http://issuer.example:8090/";
    internal static LaunchServerSelection Grant(DateTimeOffset now)
    {
        var first = Node("first", "192.0.2.1", now);
        var second = Node("second", "192.0.2.2", now);
        var manualOnly = Node("manual-only", "192.0.2.3", now);
        return new(1, "grant", "kr-812", "issuer", "policy", "shared", now.AddMinutes(5), now,
            [new(1, "Automatic", true, [first, second]), new(2, "First", false, [first]),
             new(3, "Second", false, [second]), new(4, "Manual only", false, [manualOnly])]);
    }

    private static ServerDirectoryTarget Node(string id, string host, DateTimeOffset now) => new(id, host, 17110, "shared", "", true, false, 1, 100)
    {
        Authentication = new("issuer", ApiUrl, "master-issued", "authority"),
        ObservedAtUtc = now.AddSeconds(-1), ExpiresAtUtc = now.AddSeconds(15), StatusRevision = "opaque-Z",
    };

    private static LaunchServerSelection Map(LaunchServerSelection grant, Func<ServerDirectoryTarget, ServerDirectoryTarget> map) =>
        grant with { Servers = grant.Servers.Select(row => row with { Targets = row.Targets.Select(map).ToArray() }).ToArray() };

    private static Task<AutomaticServerSelection> Create(LaunchServerSelection grant, TestClock clock,
        Func<CancellationToken, Task<LaunchServerSelection?>>? refresh = null,
        Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>>? measure = null, ServerDirectory? directory = null,
        ILaunchDiagnosticSink? diagnostics = null) =>
        AutomaticServerSelection.CreateAsync(grant, ApiUrl, refresh ?? (_ => Task.FromResult<LaunchServerSelection?>(grant)),
            resolve: (host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            measure: measure ?? ((endpoint, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(endpoint.Host.EndsWith('1') ? 50 : 5))), clock: clock,
            directory: directory, diagnostics: diagnostics,
            measureManual: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));

    [Fact]
    public async Task RefreshFailureRecordsOnlyStatusAndKnownCategoryAndRemainsRetryable()
    {
        var clock = new TestClock();
        var grant = Grant(clock.GetUtcNow());
        var calls = 0;
        var diagnostics = new RefreshDiagnostics();
        var selection = await Create(grant, clock, refresh: _ => ++calls == 1
            ? Task.FromException<LaunchServerSelection?>(new ServerFailureException("server_not_ready", HttpStatusCode.ServiceUnavailable,
                reason: "private-response-body"))
            : Task.FromResult<LaunchServerSelection?>(grant), diagnostics: diagnostics);
        Assert.NotEmpty((await selection.RefreshAsync(CancellationToken.None))!);
        await Assert.ThrowsAsync<ServerFailureException>(() => selection.RefreshAsync(CancellationToken.None));
        Assert.NotEmpty((await selection.RefreshAsync(CancellationToken.None))!);
        Assert.Equal(["http-503-server_not_ready"], diagnostics.Failures);
        Assert.Equal(2, calls);
    }

    private sealed class RefreshDiagnostics : ILaunchDiagnosticSink
    {
        internal List<string?> Failures { get; } = [];
        public void Record(string eventName, string? detail = null)
        {
            if (eventName == "gateway-selection-refresh-failed") Failures.Add(detail);
        }
    }

    [Fact]
    public async Task AutomaticGatewayRetainsExplicitSelectorShortNames()
    {
        var clock = new TestClock();
        var original = Grant(clock.GetUtcNow());
        var grant = original with { Servers = original.Servers.Select(row => row with { ShortName = row.Automatic ? "AUTO" : "S" + row.Id }).ToArray() };
        var selection = await Create(grant, clock);
        using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, []));
        var config = json.RootElement.GetProperty("rows")[0].GetProperty("config");
        Assert.Equal("AUTO", config.GetProperty("automatic_entry").GetProperty("short_name").GetString());
        Assert.Equal("S2", config.GetProperty("centers")[0].GetProperty("short_name").GetString());
    }

    [Fact]
    public async Task SingleHealthyWorkerRemainsAnAutomaticCandidateWhenAllOtherWorkersAreUnavailable()
    {
        var clock = new TestClock();
        var grant = Map(Grant(clock.GetUtcNow()), node => node.NodeId == "first" ? node : node with { Online = false });
        var selection = await Create(grant, clock);
        var candidate = Assert.Single((await selection.RefreshAsync(CancellationToken.None))!);
        Assert.Equal("2", candidate.CenterId);
        using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, [candidate]));
        var automatic = json.RootElement.GetProperty("rows")[0].GetProperty("config").GetProperty("automatic_entry");
        Assert.Equal("Automatic", automatic.GetProperty("name").GetString());
        Assert.Equal(100, automatic.GetProperty("unavailable_capacity").GetInt32());
        Assert.Equal("2", Assert.Single(automatic.GetProperty("candidates").EnumerateArray()).GetProperty("center_id").GetString());
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("draining")]
    [InlineData("full")]
    [InlineData("probe-unavailable")]
    [InlineData("probe-timeout")]
    [InlineData("probe-failed")]
    public async Task DiagnosticsIdentifyRejectedWorkersWithoutTicketsOrRepeatedUnchangedEvents(string reason)
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var changed = Map(grant, node => reason switch
        {
            "offline" => node with { Online = false },
            "draining" => node with { Draining = true },
            "full" => node with { Players = node.Capacity },
            _ => node,
        });
        var diagnostics = new CandidateDiagnostics();
        var selection = await Create(grant, clock, _ => Task.FromResult<LaunchServerSelection?>(changed), diagnostics: diagnostics,
            measure: (_, _) => reason switch
            {
                "probe-unavailable" => Task.FromResult<TimeSpan?>(null),
                "probe-timeout" => throw new OperationCanceledException(),
                "probe-failed" => throw new IOException("untrusted transport detail"),
                _ => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)),
            });
        await selection.RefreshAsync(CancellationToken.None);
        Assert.Empty((await selection.RefreshAsync(CancellationToken.None))!);
        var count = diagnostics.Events.Count;
        await selection.RefreshAsync(CancellationToken.None);
        Assert.Equal(count, diagnostics.Events.Count);
        Assert.Contains(("gateway-automatic-candidate", "center-2-" + reason), diagnostics.Events);
        Assert.All(diagnostics.Events, entry => Assert.DoesNotContain("untrusted", entry.Detail));
    }

    private sealed class CandidateDiagnostics : ILaunchDiagnosticSink
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, string Detail)> Events = new();
        public void Record(string eventName, string? detail = null) => Events.Enqueue((eventName, detail!));
    }

    [Fact]
    public async Task LargeAuthorizedDirectoryIsNotTruncatedAndItsControlBudgetIncludesEveryPotentialCandidate()
    {
        var clock = new TestClock(); var now = clock.GetUtcNow();
        var nodes = Enumerable.Range(1, 129).Select(index => Node("worker-" + index, "192.0.2." + index, now)).ToArray();
        var grant = Grant(now) with { Servers = [new(1, "Automatic", true, nodes),
            .. nodes.Select((node, index) => new ServerSelector(index + 2, "Worker " + index, false, [node]))] };
        var selection = await Create(grant, clock);
        var candidates = (await selection.RefreshAsync(CancellationToken.None))!;
        Assert.Equal(129, selection.Targets.Count);
        Assert.Equal(129, selection.AdmittedCenterIds.Count);
        Assert.Equal(129, candidates.Count);
        var configuration = GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, candidates);
        using var json = JsonDocument.Parse(configuration);
        Assert.Equal(129, json.RootElement.GetProperty("rows")[0].GetProperty("config").GetProperty("centers").GetArrayLength());
        Assert.True(GatewayBootstrap.ControlMessageBudget(configuration, selection, new string('a', 32)) > 65536);
    }

    [Fact]
    public async Task TransientManualProbeFailureDoesNotRemoveAHealthyAutomaticCandidate()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var firstAttempts = 0;
        var selection = await AutomaticServerSelection.CreateAsync(grant, ApiUrl,
            _ => Task.FromResult<LaunchServerSelection?>(grant),
            resolve: (host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            measure: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)), clock: clock,
            measureManual: (endpoint, _) => endpoint.Host.EndsWith('1') && Interlocked.Increment(ref firstAttempts) == 1
                ? Task.FromException<TimeSpan?>(new IOException("temporary connection failure"))
                : Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));
        Assert.Equal(2, firstAttempts);
        Assert.Null(selection.Targets[0].UnavailableCapacity);
        Assert.Equal(["2", "3"], (await selection.RefreshAsync(CancellationToken.None))!.Select(row => row.CenterId));
    }

    [Fact]
    public async Task FailedManualSlotRemainsAdmittedButCannotBecomeAnActiveCandidateDuringTheSameLaunch()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var selection = await AutomaticServerSelection.CreateAsync(grant, ApiUrl,
            _ => Task.FromResult<LaunchServerSelection?>(grant),
            resolve: (host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            measure: (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)), clock: clock,
            measureManual: (endpoint, _) => endpoint.Host.EndsWith('1')
                ? Task.FromException<TimeSpan?>(new IOException("unreachable"))
                : Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
        Assert.Equal(3, selection.Targets.Count);
        Assert.Equal(100, selection.Targets[0].UnavailableCapacity);
        Assert.Equal("3", Assert.Single((await selection.RefreshAsync(CancellationToken.None))!).CenterId);
        Assert.Equal("3", Assert.Single((await selection.RefreshAsync(CancellationToken.None))!).CenterId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1073741823)]
    [InlineData(1073741824)]
    public async Task UnavailableStatusCapacityComesFromTheAuthorizedPoolAndCannotOverflow(int capacity)
    {
        var clock = new TestClock();
        var grant = Map(Grant(clock.GetUtcNow()), node => node with { Capacity = capacity, Players = 0 });
        var selection = await Create(grant, clock);
        if (capacity > int.MaxValue / 2)
            Assert.Throws<InvalidDataException>(() => GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, []));
        else
        {
            using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, []));
            Assert.Equal(capacity, json.RootElement.GetProperty("rows")[0].GetProperty("config")
                .GetProperty("automatic_entry").GetProperty("unavailable_capacity").GetInt32());
        }
    }

    [Fact]
    public async Task CompleteManualDirectorySurvivesAnIssuerGrantWithIndependentTargetsRemoved()
    {
        var clock = new TestClock(); var now = clock.GetUtcNow();
        var grant = Grant(now);
        var independent = Node("independent", "192.0.2.4", now) with
        {
            DataScope = "separate", Authentication = new("independent", "http://independent.example:8090/", "own-authority", "authority"),
        };
        var directory = new ServerDirectory(1, grant.Build, [.. grant.Servers, new(5, "Independent", false, [independent])])
        { Center = new(true, "gateway") };
        grant = grant with { Servers = [.. grant.Servers, new(5, "Independent", false, [])] };
        var measured = new List<string>();
        var selection = await Create(grant, clock, directory: directory, measure: (endpoint, _) =>
        {
            lock (measured) measured.Add(endpoint.Host);
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5));
        });
        Assert.Equal(["2", "3", "4", "5"], selection.Targets.Select(row => row.Id));
        Assert.Equal("192.0.2.4", selection.Targets[3].Endpoint.Host);
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
        var candidates = (await selection.RefreshAsync(CancellationToken.None))!;
        Assert.Equal(["2", "3"], candidates.Select(row => row.CenterId));
        Assert.DoesNotContain("192.0.2.4", measured);
        using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, candidates));
        var config = json.RootElement.GetProperty("rows")[0].GetProperty("config");
        Assert.Equal([2, 3, 4, 5], config.GetProperty("centers").EnumerateArray().Select(row => row.GetProperty("native_center_id").GetInt32()));
        Assert.Equal(["2", "3"], config.GetProperty("automatic_entry").GetProperty("admitted_center_ids").EnumerateArray().Select(row => row.GetString()));
    }

    [Fact]
    public async Task MasterIssuedManualTargetMayUseIndependentDataScope()
    {
        var clock = new TestClock(); var now = clock.GetUtcNow();
        var grant = Grant(now);
        var independent = Node("independent", "192.0.2.4", now) with { DataScope = "jd-database" };
        grant = grant with { Servers = [.. grant.Servers, new(5, "Independent", false, [independent])] };

        var selection = await Create(grant, clock);

        Assert.Equal(["2", "3", "4", "5"], selection.Targets.Select(row => row.Id));
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
        Assert.DoesNotContain("192.0.2.4", (await selection.RefreshAsync(CancellationToken.None))!.Select(row => row.CenterId));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("endpoint")]
    [InlineData("authority")]
    [InlineData("missing")]
    public async Task CanonicalDirectoryCannotReplaceAnAdmittedBinding(string fault)
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var changed = fault switch
        {
            "name" => grant with { Servers = grant.Servers.Select(row => row.Id == 2 ? row with { Name = "Changed" } : row).ToArray() },
            "endpoint" => Map(grant, node => node with { Host = "192.0.2.99" }),
            "authority" => Map(grant, node => node with { Authentication = node.Authentication! with { ApiUrl = "http://other.example:8090/" } }),
            _ => grant with { Servers = grant.Servers.Where(row => row.Id != 2).ToArray() },
        };
        var directory = new ServerDirectory(1, grant.Build, changed.Servers) { Center = new(true, "gateway") };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(grant, clock, directory: directory));
    }

    [Fact]
    public async Task RetainsEveryCandidateAndExcludesManualOnlyWorkersFromAutomaticAdmission()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var selection = await Create(grant, clock);
        Assert.Equal(["2", "3", "4"], selection.Targets.Select(row => row.Id));
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
        var candidates = await selection.RefreshAsync(CancellationToken.None);
        Assert.NotNull(candidates);
        Assert.Equal(["2", "3"], candidates.Select(row => row.CenterId));
        Assert.Equal([50000, 5000], candidates.Select(row => row.RttUs));
    }

    [Fact]
    public async Task NeverObservedWorkersStayAdmittedAndRecoverFromARealObservation()
    {
        var clock = new TestClock(); var healthy = Grant(clock.GetUtcNow());
        var offline = Map(healthy, node => node.NodeId != "second" ? node : node with
        { Online = false, ObservedAtUtc = null, ExpiresAtUtc = null, StatusRevision = null });
        var selection = await Create(offline, clock, _ => Task.FromResult<LaunchServerSelection?>(healthy));
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
        Assert.Equal("2", Assert.Single((await selection.RefreshAsync(CancellationToken.None))!).CenterId);
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
    }

    [Fact]
    public async Task HealthyHeartbeatAndSuccessfulProbeRemainEligibleAfterSourceTimestampExpiry()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var selection = await Create(grant, clock);
        var first = (await selection.RefreshAsync(CancellationToken.None))![0];
        Assert.Equal(clock.GetUtcNow(), first.ObservedAtUtc);
        Assert.Equal(selection.LocalExpiresAtUtc, first.ExpiresAtUtc);
        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(-22)]
    [InlineData(86400)]
    [InlineData(-86400)]
    public async Task IssuerClockSkewDoesNotExcludeAHealthySingleWorkerOrChangeLocalAdmissionLifetime(int skewSeconds)
    {
        var clock = new TestClock(); var localNow = clock.GetUtcNow();
        var grant = Grant(localNow.AddSeconds(skewSeconds));
        grant = Map(grant, node => node.NodeId == "first" ? node : node with { Online = false });
        var selection = await Create(grant, clock);

        var candidate = Assert.Single((await selection.RefreshAsync(CancellationToken.None))!);
        Assert.Equal("2", candidate.CenterId);
        Assert.Equal(localNow, candidate.ObservedAtUtc);
        Assert.Equal(localNow.AddMinutes(5), candidate.ExpiresAtUtc);
        Assert.Equal(localNow.AddMinutes(5), selection.LocalExpiresAtUtc);
        using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration(grant.Build, new string('a', 32), selection, [candidate]));
        Assert.Equal(selection.LocalExpiresAtUtc, json.RootElement.GetProperty("rows")[0].GetProperty("config")
            .GetProperty("automatic_entry").GetProperty("grant_expires_at_utc").GetDateTimeOffset());
    }

    [Fact]
    public async Task UnchangedHealthyRevisionDoesNotExpireBeforeImmutableAdmission()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow().AddSeconds(22));
        var selection = await Create(grant, clock);
        await selection.RefreshAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.All((await selection.RefreshAsync(CancellationToken.None))!, candidate =>
            Assert.Equal(selection.LocalExpiresAtUtc, candidate.ExpiresAtUtc));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
    }

    [Fact]
    public async Task RefreshElapsedTimeOnlyConsumesImmutableAdmission()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow().AddSeconds(-22));
        var selection = await Create(grant, clock, refresh: _ =>
        {
            var snapshot = Map(grant with { GeneratedAtUtc = grant.GeneratedAtUtc.AddSeconds(1) }, node => node with { StatusRevision = "next" });
            clock.Advance(TimeSpan.FromSeconds(14));
            return Task.FromResult<LaunchServerSelection?>(snapshot);
        });
        await selection.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(await selection.RefreshAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(-19)]
    [InlineData(-3600)]
    [InlineData(3600)]
    public async Task HealthyHeartbeatTimestampsCannotExcludeReachableWorkers(int offsetSeconds)
    {
        var clock = new TestClock(); var now = clock.GetUtcNow();
        var grant = Map(Grant(now), node => node with
        { ObservedAtUtc = now.AddSeconds(offsetSeconds - 1), ExpiresAtUtc = now.AddSeconds(offsetSeconds) });
        var selection = await Create(grant, clock);
        var candidates = (await selection.RefreshAsync(CancellationToken.None))!;
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal(selection.LocalExpiresAtUtc, candidate.ExpiresAtUtc));
    }

    [Fact]
    public async Task OnlineReadinessDoesNotRequireOptionalHeartbeatTimestamps()
    {
        var clock = new TestClock();
        var grant = Map(Grant(clock.GetUtcNow()), node => node with
        { ObservedAtUtc = null, ExpiresAtUtc = null, StatusRevision = null });
        var selection = await Create(grant, clock);
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
    }

    [Fact]
    public async Task EmptyFullSnapshotWithdrawsCandidatesAndMayRecoverWithinOriginalGrant()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow()); var calls = 0;
        var withdrawn = grant with { Servers = grant.Servers.Select(row => row with { Targets = Array.Empty<ServerDirectoryTarget>() }).ToArray() };
        var selection = await Create(grant, clock, _ => Task.FromResult<LaunchServerSelection?>(++calls == 1 ? withdrawn : grant));
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
        Assert.Empty((await selection.RefreshAsync(CancellationToken.None))!);
        Assert.Equal(2, (await selection.RefreshAsync(CancellationToken.None))!.Count);
        Assert.Equal(["2", "3"], selection.AdmittedCenterIds);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("authority")]
    [InlineData("scope")]
    [InlineData("grant")]
    [InlineData("expiry")]
    [InlineData("selector")]
    public async Task RefreshCannotReplaceAnyOriginalBinding(string fault)
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var changed = fault switch
        {
            "host" => Map(grant, row => row with { Host = "192.0.2.99" }),
            "port" => Map(grant, row => row with { Port = row.Port + 1 }),
            "authority" => Map(grant, row => row with { Authentication = row.Authentication! with { ApiUrl = "http://elsewhere.example:8090/" } }),
            "scope" => Map(grant, row => row with { DataScope = "other" }),
            "grant" => grant with { GrantId = "other" },
            "expiry" => grant with { ExpiresAtUtc = grant.ExpiresAtUtc.AddSeconds(1) },
            _ => grant with { Servers = grant.Servers.Select(row => row.Id == 2 ? row with { Id = 9 } : row).ToArray() },
        };
        var selection = await Create(grant, clock, _ => Task.FromResult<LaunchServerSelection?>(changed));
        await selection.RefreshAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => selection.RefreshAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OpaqueSourceRevisionsMayDecreaseWithoutChangingParentOrdering()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow());
        var selection = await Create(grant, clock, _ => Task.FromResult<LaunchServerSelection?>(Map(grant, row => row with { StatusRevision = "a" })));
        await selection.RefreshAsync(CancellationToken.None);
        Assert.All((await selection.RefreshAsync(CancellationToken.None))!, row => Assert.Equal("a", row.StatusRevision));
    }

    [Fact]
    public async Task AdmissionClosureAndExpiryNeverRequestAnotherTicket()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow()); var refreshes = 0;
        var selection = await Create(grant, clock, _ => { refreshes++; return Task.FromResult<LaunchServerSelection?>(null); });
        await selection.RefreshAsync(CancellationToken.None);
        Assert.Null(await selection.RefreshAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null(await selection.RefreshAsync(CancellationToken.None));
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task CandidateMeasurementsRemainSerialAndBounded()
    {
        var clock = new TestClock(); var grant = Grant(clock.GetUtcNow()); var active = 0; var maximum = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selection = await Create(grant, clock, measure: async (_, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            int observed;
            do { observed = Volatile.Read(ref maximum); if (observed >= count) break; }
            while (Interlocked.CompareExchange(ref maximum, count, observed) != observed);
            if (count == grant.Servers.Single(row => row.Automatic).Targets.Count) started.TrySetResult();
            try { await release.Task.WaitAsync(ct); return TimeSpan.FromMilliseconds(2); }
            finally { Interlocked.Decrement(ref active); }
        });
        var first = selection.RefreshAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => selection.RefreshAsync(CancellationToken.None));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally { release.TrySetResult(); }
        await first;
        Assert.Equal(2, maximum);
    }

    [Fact]
    public async Task GatewayConfigurationPreservesPhysicalIdentitiesAndContainsNoTicketOrPresentationHook()
    {
        var clock = new TestClock(); var selection = await Create(Grant(clock.GetUtcNow()), clock);
        var candidates = (await selection.RefreshAsync(CancellationToken.None))!;
        using var json = JsonDocument.Parse(GatewayBootstrap.BuildAutomaticConfiguration("kr-812", new string('a', 32), selection, candidates));
        var config = json.RootElement.GetProperty("rows")[0].GetProperty("config");
        Assert.Equal(0, config.GetProperty("connect_timeout_ms").GetInt32());
        Assert.Equal(3000, config.GetProperty("native_probe_timeout_ms").GetInt32());
        Assert.Equal(8, config.GetProperty("native_probe_parallelism").GetInt32());
        Assert.All(config.GetProperty("centers").EnumerateArray(), target =>
            Assert.Equal(100, target.GetProperty("status_capacity").GetInt32()));
        Assert.False(config.TryGetProperty("selector_identity", out _));
        var automatic = config.GetProperty("automatic_entry");
        Assert.Equal(["2", "3"], automatic.GetProperty("admitted_center_ids").EnumerateArray().Select(row => row.GetString()));
        Assert.Equal(2, automatic.GetProperty("candidates").GetArrayLength());
        Assert.Equal([2, 3, 4], config.GetProperty("centers").EnumerateArray().Select(row => row.GetProperty("native_center_id").GetInt32()));
        Assert.DoesNotContain("ticket", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("accepted", "updated", 1, "accepted")]
    [InlineData("rejected", "candidates", 1, "rejected")]
    [InlineData("rejected", "grant-expired", 1, "expired")]
    [InlineData("finished", "retired", 1, "finished")]
    [InlineData("accepted", "updated", 2, null)]
    [InlineData("rejected", "binding", 1, null)]
    public void GatewayAcknowledgementsAreCorrelatedBeforeAnyNextRefresh(string status, string reason, long revision, string? expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { type = "automatic-refresh-result", row_id = "selector", revision, status, reason }));
        if (expected is null) Assert.Throws<InvalidDataException>(() => GatewayBootstrap.ValidateAutomaticAck(json.RootElement, 1));
        else Assert.Equal(expected, GatewayBootstrap.ValidateAutomaticAck(json.RootElement, 1));
    }

    [Fact]
    public async Task CandidateDeadlineCannotExceedFrozenGrantWhenWallAndElapsedReadingsDiffer()
    {
        var clock = new TestClock();
        var selection = await Create(Grant(clock.GetUtcNow()), clock);
        clock.ShiftWall(TimeSpan.FromTicks(1));
        var candidates = (await selection.RefreshAsync(CancellationToken.None))!;
        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(selection.LocalExpiresAtUtc, candidate.ExpiresAtUtc);
            Assert.True(candidate.ObservedAtUtc <= candidate.ExpiresAtUtc);
        });
    }

    internal sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan amount) { _now += amount; _timestamp += amount.Ticks; }
        internal void ShiftWall(TimeSpan amount) => _now += amount;
    }
}
