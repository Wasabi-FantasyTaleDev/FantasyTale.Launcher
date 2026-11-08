using System.Net;
using System.Net.Http.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerDirectoryTests
{
    private static ServerDirectoryTarget Node(string id, string host) => new(id, host, 7140, "formal", "", true, false, 1, 100);
    private static readonly ServerDefinition Server = new("cluster", "bubblefighter", "Cluster", "http://192.0.2.1:8090/", null, true, true);

    [Theory]
    [InlineData("dns")]
    [InlineData("connect")]
    [InlineData("timeout")]
    public async Task TransientNativeProbeFailureIsRetriedBeforeFreezingAnUnavailableEntry(string fault)
    {
        var directory = new ServerDirectory(1, "cn-324", [new(7, "Manual", false, [Node("worker", "worker.example")])]);
        var attempts = 0;
        var diagnostics = new ProbeDiagnostics();
        var resolver = new ServerDirectoryResolver((_, _) =>
        {
            if (fault == "dns" && Interlocked.Increment(ref attempts) == 1)
                throw new IOException("private DNS failure");
            return Task.FromResult(new[] { IPAddress.Parse("192.0.2.7") });
        }, (_, _) =>
        {
            if (fault != "dns" && Interlocked.Increment(ref attempts) == 1)
                return fault == "timeout" ? Task.FromException<TimeSpan?>(new OperationCanceledException())
                    : Task.FromException<TimeSpan?>(new IOException("private transport failure"));
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5));
        }, diagnostics: diagnostics);
        var target = Assert.Single(await resolver.ResolveNativeAsync(directory));
        Assert.Null(target.UnavailableCapacity);
        Assert.Equal(100, target.StatusCapacity);
        Assert.Equal("192.0.2.7", target.Endpoint.Host);
        Assert.Equal(2, attempts);
        Assert.Equal(2, diagnostics.Events.Count);
        Assert.Contains("center-7-connected-attempt-2", diagnostics.Events);
        Assert.All(diagnostics.Events, detail => Assert.DoesNotContain("private", detail));
    }

    [Fact]
    public async Task PersistentNativeFailureRemainsVisibleAfterBoundedRetries()
    {
        var attempts = 0;
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _) => { attempts++; throw new IOException(); }, nativeProbeAttempts: 3);
        var target = Assert.Single(await resolver.ResolveNativeAsync(new(1, "cn-324", [new(7, "Manual", false, [Node("worker", "192.0.2.7")])])));
        Assert.Equal(3, attempts);
        Assert.Equal(100, target.UnavailableCapacity);
        Assert.Equal(100, target.StatusCapacity);
        Assert.Equal(7, target.NativeCenterId);
    }

    [Fact]
    public async Task NativeRetryBudgetExpiresWithoutMultiplyingTheDirectoryDeadline()
    {
        var attempts = 0;
        CancellationToken directoryBudget = default;
        var resolver = new ServerDirectoryResolver((host, token) =>
            { directoryBudget = token; return Task.FromResult(new[] { IPAddress.Parse(host) }); },
            async (_, _) => { attempts++; await Task.Delay(Timeout.Infinite, directoryBudget); return null; },
            deadline: TimeSpan.FromMilliseconds(50), probeTimeout: TimeSpan.FromMilliseconds(50));
        var target = Assert.Single(await resolver.ResolveNativeAsync(new(1, "cn-324", [new(7, "Manual", false, [Node("worker", "192.0.2.7")])])));
        Assert.Equal(1, attempts);
        Assert.Equal(100, target.UnavailableCapacity);
    }

    [Fact]
    public async Task NativeCallerCancellationIsNotConvertedIntoAnUnavailableEntry()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, token) => { cancellation.Cancel(); return Task.FromCanceled<TimeSpan?>(token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveNativeAsync(
            new(1, "cn-324", [new(7, "Manual", false, [Node("worker", "192.0.2.7")])]), cancellation.Token));
    }

    private sealed class ProbeDiagnostics : ILaunchDiagnosticSink
    {
        internal List<string> Events { get; } = [];
        public void Record(string eventName, string? detail = null)
        {
            Assert.Equal("gateway-native-probe", eventName);
            Events.Add(detail!);
        }
    }

    [Theory]
    [InlineData("dns")]
    [InlineData("connect")]
    [InlineData("timeout")]
    public async Task NativeDirectoryPreservesFailedRowsNamesAndPhysicalBindings(string fault)
    {
        var first = Node("first", "192.0.2.1"); var failed = Node("failed", "failed.example");
        var directory = new ServerDirectory(1, "cn-324", [new(2, "First", false, [first]), new(7, "Unavailable", false, [failed])]);
        var resolver = new ServerDirectoryResolver((host, _) => fault == "dns" && host == "failed.example"
            ? Task.FromException<IPAddress[]>(new IOException("DNS failed"))
            : Task.FromResult(new[] { IPAddress.Parse(host == "failed.example" ? "192.0.2.7" : host) }),
            (endpoint, _) => endpoint.Host == "192.0.2.1" ? Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5))
                : fault == "timeout" ? Task.FromException<TimeSpan?>(new OperationCanceledException())
                : Task.FromException<TimeSpan?>(new IOException("Connection failed")));
        var result = await resolver.ResolveNativeAsync(directory);
        Assert.Equal(["2", "7"], result.Select(row => row.Id));
        Assert.Null(result[0].UnavailableCapacity);
        Assert.Equal("Unavailable", result[1].Name);
        Assert.Equal("failed", result[1].BackendId);
        Assert.Equal(7, result[1].NativeCenterId);
        Assert.Equal(100, result[1].UnavailableCapacity);
        Assert.Equal(fault == "dns" ? "failed.example" : "192.0.2.7", result[1].Endpoint.Host);
    }

    [Fact]
    public async Task FastestAutomaticTargetRetainsSeparateManualIdentityAndDirectoryOrder()
    {
        var first = Node("first", "192.0.2.1"); var second = Node("second", "192.0.2.2");
        var directory = new ServerDirectory(1, "cn-324", [new(10, "Automatic", true, [first, second]), new(11, "First", false, [first]), new(12, "Second", false, [second])]);
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (endpoint, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(endpoint.Host.EndsWith('2') ? 5 : 50)));
        var result = await resolver.ResolveAsync(directory);
        Assert.Equal([10, 11, 12], result.Select(row => row.Id));
        Assert.Equal("second", result[0].Target!.NodeId);
        Assert.Equal(result[0].Endpoint, result[2].Endpoint);
        Assert.Equal("Automatic", result[0].Name);
        Assert.Equal("Second", result[2].Name);
        Assert.NotEqual(result[0].Id, result[2].Id);
    }

    [Fact]
    public async Task SelectorShortNameIsPreservedOnResolvedGatewayTarget()
    {
        var node = Node("beijing", "192.0.2.8");
        var directory = new ServerDirectory(1, "cn-324", [new ServerSelector(2, "北京（阿里云）", false, [node])
        {
            ShortName = "北京",
        }]);
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _) => Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(5)));
        var target = Assert.Single(await resolver.ResolveNativeAsync(directory));
        Assert.Equal("北京", target.ShortName);
    }

    [Fact]
    public async Task EmptyAutomaticPoolStaysUnavailableAndOfflineManualTargetIsRetained()
    {
        var node = Node("offline", "192.0.2.3") with { Online = false };
        var directory = new ServerDirectory(1, "cn-324", [new(1, "Automatic", true, []), new(2, "Manual", false, [node])]);
        var resolver = new ServerDirectoryResolver((_, _) => throw new InvalidOperationException("Offline targets must not perform DNS."),
            (_, _) => throw new InvalidOperationException("A manual-only target must not be probed."));
        var result = await resolver.ResolveAsync(directory);
        Assert.Null(result[0].Target); Assert.Null(result[0].Endpoint);
        Assert.Equal("offline", result[1].Target!.NodeId);
        Assert.Null(result[1].Endpoint);
    }

    [Fact]
    public async Task UnconfiguredManualSelectorsRemainVisibleWithoutDnsOrConnectionAttempts()
    {
        var directory = new ServerDirectory(1, "cn-324",
            [new(1, "Automatic", true, []), new(4, "Tencent", false, []), new(5, "JD", false, []), new(6, "US", false, [])]);
        var resolver = new ServerDirectoryResolver(
            (_, _) => throw new InvalidOperationException("An empty selector must not resolve an invented host."),
            (_, _) => throw new InvalidOperationException("An empty selector must not attempt a connection."));
        var result = await resolver.ResolveAsync(directory);
        Assert.Equal([1, 4, 5, 6], result.Select(row => row.Id));
        Assert.All(result, row => { Assert.Null(row.Target); Assert.Null(row.Endpoint); });
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("draining")]
    [InlineData("full")]
    public async Task IneligibleManualAliasCannotBorrowAnEligibleNodesResolvedAddress(string state)
    {
        var node = Node("same", "192.0.2.3");
        var unavailable = state switch
        {
            "offline" => node with { Online = false },
            "draining" => node with { Draining = true },
            _ => node with { Players = node.Capacity },
        };
        var directory = new ServerDirectory(1, "kr-812", [new(1, "Available", false, [node]), new(2, "Unavailable", false, [unavailable])]);
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }));
        var result = await resolver.ResolveAsync(directory);
        Assert.NotNull(result[0].Endpoint);
        Assert.Null(result[1].Endpoint);
        Assert.Equal(node.NodeId, result[1].Target!.NodeId);
    }

    [Fact]
    public void ManualSelectorsRejectMultiplePhysicalTargets()
    {
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324",
            [new(2, "Manual", false, [Node("first", "192.0.2.1"), Node("second", "192.0.2.2")])]), "cn-324"));
    }

    [Fact]
    public async Task UnreachableAutomaticCandidateCannotEraseItsManualEndpoint()
    {
        var node = Node("node", "192.0.2.4");
        var directory = new ServerDirectory(1, "cn-324", [new(1, "Automatic", true, [node]), new(2, "Manual", false, [node])]);
        var resolver = new ServerDirectoryResolver((host, _) => Task.FromResult(new[] { IPAddress.Parse(host) }),
            (_, _) => Task.FromException<TimeSpan?>(new System.Net.Sockets.SocketException()));
        var result = await resolver.ResolveAsync(directory);
        Assert.Null(result[0].Endpoint); Assert.NotNull(result[1].Endpoint);
    }

    [Fact]
    public async Task DirectoryReadsUseOnlyPublicMetadataAndOnly404AllowsStandaloneFallback()
    {
        using var handler = new DirectoryHandler(); using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        handler.Status = HttpStatusCode.NotFound;
        Assert.Null(await api.ServerDirectoryAsync(Server, "cn-324"));
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<ServerFailureException>(() => api.ServerDirectoryAsync(Server, "cn-324"));
        handler.Status = HttpStatusCode.OK;
        Assert.Equal("cn-324", (await api.ServerDirectoryAsync(Server, "cn-324"))!.Build);
        Assert.Equal("/v1/servers/cn-324", handler.Path);
        Assert.False(handler.AuthorizationObserved);
        handler.Build = "other";
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ServerDirectoryAsync(Server, "cn-324"));
    }

    [Theory]
    [InlineData("empty-scope")]
    [InlineData("cross-scope")]
    [InlineData("offline")]
    [InlineData("draining")]
    [InlineData("full")]
    public void InvalidAutomaticAdmissionNeverBecomesAPermissionOrFallback(string scenario)
    {
        var first = Node("first", "192.0.2.1"); var second = Node("second", "192.0.2.2");
        second = scenario switch
        {
            "empty-scope" => second with { DataScope = "" },
            "cross-scope" => second with { DataScope = "independent" },
            "offline" => second with { Online = false },
            "draining" => second with { Draining = true },
            _ => second with { Players = second.Capacity }
        };
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324", [new(1, "Auto", true, [first, second])]) { Center = new(true, "gateway") }, "cn-324"));
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfSelectingAnUnmeasuredBackend()
    {
        var node = Node("first", "192.0.2.1");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var resolver = new ServerDirectoryResolver((_, ct) => Task.FromCanceled<IPAddress[]>(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(new(1, "cn-324", [new(1, "Auto", true, [node])]), canceled.Token));
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    [InlineData(0x0a)]
    public void InvalidNativeLabelsAreRejectedBeforeBootstrap(int unit)
    {
        var name = new string((char)unit, 1);
        var directory = new ServerDirectory(1, "cn-324", [new(1, name, false, [Node("node", "192.0.2.1")])]);
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(directory, "cn-324"));
    }

    [Fact]
    public void ValidSurrogatePairsRemainIntactInNativeLabels()
    {
        var name = char.ConvertFromUtf32(0x20000);
        LauncherApiClient.ValidateDirectory(new(1, "cn-324", [new(1, name, false, [Node("node", "192.0.2.1")])]), "cn-324");
    }

    [Fact]
    public async Task DifferentNodesCannotClaimTheSameResolvedEndpoint()
    {
        var resolver = new ServerDirectoryResolver((_, _) => Task.FromResult(new[] { IPAddress.Parse("192.0.2.1") }));
        var directory = new ServerDirectory(1, "cn-324", [new(1, "First", false, [Node("first", "one.example")]), new(2, "Second", false, [Node("second", "two.example")])]);
        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync(directory));
    }

    [Fact]
    public async Task ResolverBoundsConcurrentWorkAndKeepsUnmeasuredAutomaticPoolUnavailable()
    {
        var active = 0;
        var maximum = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nodes = Enumerable.Range(1, 8).Select(i => Node("node-" + i, "192.0.2." + i)).ToArray();
        var resolver = new ServerDirectoryResolver(async (host, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            InterlockedExtensionsMax(ref maximum, count);
            if (count == 2) entered.TrySetResult();
            try { await release.Task.WaitAsync(ct); return new[] { IPAddress.Parse(host) }; }
            finally { Interlocked.Decrement(ref active); }
        }, (_, _) => Task.FromResult<TimeSpan?>(null), parallelism: 2);
        var pending = resolver.ResolveAsync(new(1, "cn-324", [new(1, "Auto", true, nodes)]));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var result = await pending;
        Assert.Equal(2, maximum);
        Assert.Null(result[0].Endpoint);
    }

    private static void InterlockedExtensionsMax(ref int location, int value)
    {
        int observed;
        do { observed = Volatile.Read(ref location); if (observed >= value) return; }
        while (Interlocked.CompareExchange(ref location, value, observed) != observed);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("mode")]
    [InlineData("policy")]
    [InlineData("userinfo")]
    [InlineData("unsupported-scheme")]
    public void AuthenticationAuthorityCannotBeInferredOrRetargeted(string fault)
    {
        var authentication = new ServerDirectoryAuthentication("master", "https://master.example/", "master-issued", "authority");
        var second = fault switch
        {
            "origin" => authentication with { ApiUrl = "https://other.example/" },
            "mode" => authentication with { Mode = "data-scope" },
            "policy" => authentication with { PolicyMode = "inherit" },
            "unsupported-scheme" => authentication with { ApiUrl = "ftp://master.example/" },
            _ => authentication with { ApiUrl = "https://account:secret@master.example/" },
        };
        var directory = new ServerDirectory(1, "cn-324",
        [
            new(1, "First", false, [Node("first", "192.0.2.1") with { Authentication = authentication }]),
            new(2, "Second", false, [Node("second", "192.0.2.2") with { Authentication = second }]),
        ]);
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(directory, "cn-324"));
    }

    [Fact]
    public void LegacyMissingAuthorityAndExplicitIndependentAuthorityRemainDistinct()
    {
        var first = Node("first", "192.0.2.1");
        var second = Node("second", "192.0.2.2") with
        {
            Authentication = new("overseas", "https://overseas.example/", "own-authority", "authority"),
        };
        var directory = new ServerDirectory(1, "cn-324", [new(1, "First", false, [first]), new(2, "Second", false, [second])]);
        LauncherApiClient.ValidateDirectory(directory, "cn-324");
        Assert.Null(first.Authentication);
        Assert.Equal("own-authority", second.Authentication!.Mode);
    }

    [Theory]
    [InlineData("http://bf.600819.xyz:8090/")]
    [InlineData("https://master.example/")]
    public void ExplicitHttpAndHttpsAuthoritiesUseTheConfiguredTransport(string apiUrl)
    {
        var authentication = new ServerDirectoryAuthentication("master", apiUrl, "master-issued", "authority");
        var node = Node("formal", "192.0.2.1") with { Authentication = authentication };
        LauncherApiClient.ValidateDirectory(new(1, "cn-324", [new(1, "Manual", false, [node])]), "cn-324");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("url")]
    [InlineData("mode")]
    public void AutomaticCandidatesCannotMixAuthenticationAuthorities(string fault)
    {
        var authentication = new ServerDirectoryAuthentication("master", "http://master.example:8090/", "master-issued", "authority");
        var second = fault switch
        {
            "missing" => null,
            "identity" => authentication with { AuthorityId = "independent" },
            "url" => authentication with { ApiUrl = "http://master.example:8090/other/" },
            _ => authentication with { Mode = "own-authority" },
        };
        var firstNode = Node("first", "192.0.2.1") with { Authentication = authentication };
        var secondNode = Node("second", "192.0.2.2") with { Authentication = second };
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324",
            [new(1, "Automatic", true, [firstNode, secondNode])]) { Center = new(true, "gateway") }, "cn-324"));
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324",
            [new(1, "Automatic", true, [secondNode, firstNode])]) { Center = new(true, "gateway") }, "cn-324"));
    }

    [Theory]
    [InlineData("Uppercase")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    public void PhysicalIdentitiesMatchTheNativeGatewayContract(string nodeId)
    {
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324",
            [new(1, "Manual", false, [Node(nodeId, "192.0.2.1")])]), "cn-324"));
    }

    [Fact]
    public void DuplicateSelectorLabelsAreRejectedBeforeBootstrap()
    {
        Assert.Throws<InvalidDataException>(() => LauncherApiClient.ValidateDirectory(new(1, "cn-324",
            [new(1, "Same", false, [Node("first", "192.0.2.1")]), new(2, "Same", false, [Node("second", "192.0.2.2")])]), "cn-324"));
    }

    private sealed class DirectoryHandler : HttpMessageHandler
    {
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Build = "cn-324";
        internal string? Path;
        internal bool AuthorizationObserved;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            AuthorizationObserved |= request.Headers.Authorization is not null;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = JsonContent.Create(new ServerDirectory(1, Build, [])) });
        }
    }
}
